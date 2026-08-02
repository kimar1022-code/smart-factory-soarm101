"""
SO-ARM 듀얼 로봇 서버 v5 - 홈포즈 기능 안전화
==============================================

v4 → v5 변경사항:
  ✨ "홈으로 이동" 명령 추가 (모든 모터 0°로)
  ✨ "안전한 홈포즈 지정" 추가
     - LeRobot autocorrect 알고리즘 적용 (±4096 wrap-around)
     - 범위 체크 + 실패 시 자동 복구
     - 모터 버스 안 끊김
  ✅ 백업 파일 안전하게 관리

홈포즈 알고리즘 (LeRobot 검증된 방식):
  1. 현재 raw 모터 위치 읽기
  2. 새 homing_offset = 현재_homing_offset + (raw위치 - 2048)
     ※ 2048 = 12bit 엔코더 중심 (4096/2)
  3. 만약 절대값이 2047을 초과하면:
     - 양수면 → new_offset -= 4096 (wrap-around)
     - 음수면 → new_offset += 4096
  4. 보정 후에도 한계 초과면 → 에러 (캘리브 안 건드림)

통신 프로토콜:
  - set: {"mode":"...", "motor":"...", "value":-100~100}
  - get: {"type":"get", "mode":"..."}
  - torque: {"type":"torque", "mode":"...", "enable":true|false}
  - set_speed: {"type":"set_speed", "mode":"...", "velocity":..., "acceleration":...}
  - 🆕 home: {"type":"home", "mode":"robot1|robot2|both"}
  - 🆕 set_home: {"type":"set_home", "mode":"robot1|robot2", "confirm":true}
"""

import socket
import threading
import json
import time
import signal
import sys
import shutil
from pathlib import Path
from datetime import datetime

from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode, MotorCalibration

# ============================================================
# 설정
# ============================================================

PORTS = {
    'robot1': '/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00',
    'robot2': '/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14029636-if00',
}

CAL_FILE = {
    'robot1': '/home/sw/.cache/huggingface/lerobot/calibration/robots/so_follower/robot1.json',
    'robot2': '/home/sw/.cache/huggingface/lerobot/calibration/robots/so_follower/robot2.json',
}

MOTOR_NAMES = [
    'shoulder_pan', 'shoulder_lift', 'elbow_flex',
    'wrist_flex', 'wrist_roll', 'gripper'
]

LISTEN_HOST = '0.0.0.0'
LISTEN_PORT = 5000

# 기본 속도 설정
DEFAULT_VELOCITY = 800
DEFAULT_ACCELERATION = 50

# 12-bit 엔코더 상수
ENCODER_CENTER = 2048    # 4096 / 2
ENCODER_RANGE = 4096
MAX_OFFSET = 2047        # sign_bit_index=11일 때 magnitude 한계


# ============================================================
# 캘리브레이션 관련
# ============================================================

def load_calibration(path):
    """캘리브레이션 JSON 파일 로드"""
    with open(path) as f:
        data = json.load(f)
    cal = {}
    for motor_name, params in data.items():
        cal[motor_name] = MotorCalibration(
            id=params['id'],
            drive_mode=params['drive_mode'],
            homing_offset=params['homing_offset'],
            range_min=params['range_min'],
            range_max=params['range_max'],
        )
    return cal


def save_calibration(path, cal_dict):
    """캘리브레이션 JSON 파일 저장"""
    data = {}
    for motor_name, mc in cal_dict.items():
        data[motor_name] = {
            'id': mc.id,
            'drive_mode': mc.drive_mode,
            'homing_offset': mc.homing_offset,
            'range_min': mc.range_min,
            'range_max': mc.range_max,
        }
    with open(path, 'w') as f:
        json.dump(data, f, indent=2)


def make_bus(port, cal_path):
    """모터 버스 생성 + 초기화"""
    calibration = load_calibration(cal_path)

    motors = {
        name: Motor(
            id=calibration[name].id,
            model='sts3215',
            norm_mode=MotorNormMode.RANGE_M100_100,
        )
        for name in MOTOR_NAMES
    }

    bus = FeetechMotorsBus(
        port=port,
        motors=motors,
        calibration=calibration,
        protocol_version=0,
    )

    bus.connect()
    bus.write_calibration(calibration)
    apply_speed_settings(bus, DEFAULT_VELOCITY, DEFAULT_ACCELERATION)

    # ⚠️ 토크를 켜기 전에 목표를 현재 위치로 맞춰야 한다.
    #    전원을 껐다 켜면 팔이 주저앉은 채로 시작하는데, 이걸 안 하면
    #    토크가 들어오는 순간 예전 목표 위치로 팔이 스스로 올라간다.
    #    handle_teach() 가 쓰는 것과 같은 방식이다.
    for motor_name in MOTOR_NAMES:
        try:
            pos = int(bus.read("Present_Position", motor_name, normalize=False))
            bus.write("Goal_Position", motor_name, pos, normalize=False)
        except Exception as e:
            print(f"  ⚠ {motor_name} 목표 동기화 실패: {e}")

    bus.enable_torque()
    return bus


def apply_speed_settings(bus, velocity, acceleration):
    """모든 모터에 속도/가속도 설정"""
    for motor_name in MOTOR_NAMES:
        try:
            # ⚠️ 레지스터 이름은 Goal_Velocity 다. Max_Velocity 는 sts3215
            #    컨트롤 테이블에 없어서 예외로 죽고, 속도 제한이 통째로 안 걸렸다.
            bus.write("Goal_Velocity", motor_name, velocity)
        except Exception as e:
            print(f"  ⚠ {motor_name} Goal_Velocity 실패: {e}")
        try:
            bus.write("Acceleration", motor_name, acceleration)
        except Exception as e:
            print(f"  ⚠ {motor_name} Acceleration 실패: {e}")


# ============================================================
# 🆕 홈으로 이동 기능 (안전!)
# ============================================================

def handle_go_home(robots, mode):
    """모든 모터를 0° 위치로 이동.
    
    매우 안전함:
    - 캘리브 파일 안 건드림
    - 그냥 Goal_Position을 0으로 보냄
    - 모터가 자동으로 천천히 이동 (속도 제한 적용됨)
    """
    targets = []
    if mode in ('robot1', 'both'):
        targets.append(('robot1', robots['robot1']))
    if mode in ('robot2', 'both'):
        targets.append(('robot2', robots['robot2']))

    for name, bus in targets:
        print(f"  🏠 {name} 홈으로 이동 (모든 모터 0°)")
        for motor in MOTOR_NAMES:
            try:
                # 정규화 모드: 0 = 홈 위치 (homing_offset이 있는 곳)
                bus.write("Goal_Position", motor, 0.0)
            except Exception as e:
                print(f"     ⚠ {motor} 이동 실패: {e}")

    return {"ok": True, "message": "Moving to home position"}


# ============================================================
# 🆕 안전한 홈포즈 지정 기능
# ============================================================

def calculate_new_homing_offset(current_raw_position, current_homing_offset):
    """현재 raw 위치를 새 홈포즈로 만들기 위한 homing_offset 계산.
    
    LeRobot의 autocorrect_calibration 알고리즘 적용:
    1. 새 offset = 기존 offset + (raw위치 - 2048)
    2. ±2047 범위 안 들어오게 wrap-around
    3. 그래도 한계 초과면 에러
    
    Args:
        current_raw_position: 모터의 현재 raw 위치 (0~4095)
        current_homing_offset: 현재 캘리브의 homing_offset
    
    Returns:
        새 homing_offset (성공 시), None (불가능 시)
    """
    # 기본 계산
    new_offset = current_homing_offset + (current_raw_position - ENCODER_CENTER)
    
    # Wrap-around (autocorrect 알고리즘)
    while new_offset > MAX_OFFSET:
        new_offset -= ENCODER_RANGE
    while new_offset < -MAX_OFFSET:
        new_offset += ENCODER_RANGE
    
    # 최종 범위 체크
    if abs(new_offset) > MAX_OFFSET:
        return None  # 보정 불가능
    
    return int(new_offset)


def handle_set_home_safe(robots, mode, calibrations, robot_name):
    """🛡️ 안전한 홈포즈 지정.
    
    안전 장치:
    1. 캘리브 파일을 타임스탬프와 함께 백업
    2. 새 offset 계산 (autocorrect 포함)
    3. 모든 모터의 새 offset이 유효한지 먼저 검증
    4. 검증 통과 시에만 적용
    5. 적용 실패 시 백업에서 즉시 복구
    """
    if robot_name not in ('robot1', 'robot2'):
        return {"ok": False, "error": f"Invalid robot: {robot_name}"}
    
    bus = robots[robot_name]
    cal_path = CAL_FILE[robot_name]
    current_cal = calibrations[robot_name]
    
    print(f"\n  🏠 [{robot_name}] 안전한 홈포즈 지정 시작...")
    
    # === 1단계: 현재 raw 위치 읽기 ===
    raw_positions = {}
    try:
        for motor in MOTOR_NAMES:
            # 정규화 안 한 raw 위치 읽기
            raw = bus.read("Present_Position", motor, normalize=False)
            raw_positions[motor] = int(raw)
            print(f"     [{motor}] 현재 raw: {raw}")
    except Exception as e:
        return {"ok": False, "error": f"raw 위치 읽기 실패: {e}"}
    
    # === 2단계: 새 homing_offset 계산 + 검증 (먼저 다 계산만!) ===
    new_offsets = {}
    for motor in MOTOR_NAMES:
        old_offset = current_cal[motor].homing_offset
        new_offset = calculate_new_homing_offset(
            raw_positions[motor], old_offset
        )
        
        if new_offset is None:
            return {
                "ok": False, 
                "error": f"{motor}: homing_offset 계산 실패 "
                         f"(raw={raw_positions[motor]}, old={old_offset}). "
                         f"모터를 중심 근처로 옮기고 다시 시도하세요."
            }
        
        new_offsets[motor] = new_offset
        print(f"     [{motor}] 새 homing_offset: {old_offset} → {new_offset}")
    
    # === 3단계: 백업 ===
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    backup_path = f"{cal_path}.before_sethome_{timestamp}"
    try:
        shutil.copy(cal_path, backup_path)
        print(f"     💾 백업: {backup_path}")
    except Exception as e:
        return {"ok": False, "error": f"백업 실패: {e}"}
    
    # === 4단계: 새 캘리브 데이터 생성 ===
    new_cal = {}
    for motor in MOTOR_NAMES:
        old = current_cal[motor]
        new_cal[motor] = MotorCalibration(
            id=old.id,
            drive_mode=old.drive_mode,
            homing_offset=new_offsets[motor],
            range_min=old.range_min,
            range_max=old.range_max,
        )
    
    # === 5단계: 모터에 적용 ===
    # 5-1) 토크 해제 (안전)
    try:
        bus.disable_torque()
    except Exception as e:
        print(f"     ⚠ 토크 해제 실패 (무시): {e}")
    
    # 5-2) 캘리브 파일 저장
    try:
        save_calibration(cal_path, new_cal)
        print(f"     ✅ 캘리브 파일 저장")
    except Exception as e:
        return {"ok": False, "error": f"파일 저장 실패: {e}"}
    
    # 5-3) 모터에 새 캘리브 적용
    try:
        bus.write_calibration(new_cal)
        print(f"     ✅ 모터에 새 캘리브 적용")
    except Exception as e:
        # 실패! 백업에서 복구
        print(f"     ❌ 적용 실패: {e}")
        print(f"     🔄 백업에서 복구 중...")
        try:
            shutil.copy(backup_path, cal_path)
            bus.write_calibration(current_cal)
            print(f"     ✅ 복구 완료")
        except Exception as e2:
            print(f"     🚨 복구도 실패: {e2}")
        return {"ok": False, "error": f"적용 실패 (복구됨): {e}"}
    
    # 5-4) 토크 다시 켜기
    try:
        bus.enable_torque()
    except Exception as e:
        print(f"     ⚠ 토크 재활성화 실패: {e}")
    
    # 5-5) 메모리상 calibrations도 업데이트
    calibrations[robot_name] = new_cal
    
    print(f"     🎉 [{robot_name}] 홈포즈 지정 완료!\n")
    return {
        "ok": True,
        "message": "Home pose updated successfully",
        "robot": robot_name,
        "new_offsets": new_offsets,
        "backup": backup_path,
    }


# ============================================================
# 기존 명령 처리 (v4와 동일)
# ============================================================

def handle_get_positions(robots, mode):
    result = {"ok": True}
    targets = []
    if mode in ('robot1', 'both'):
        targets.append(('robot1', robots['robot1']))
    if mode in ('robot2', 'both'):
        targets.append(('robot2', robots['robot2']))
    for name, bus in targets:
        positions = {}
        for motor in MOTOR_NAMES:
            try:
                val = bus.read("Present_Position", motor)
                positions[motor] = round(float(val), 2)
            except Exception:
                positions[motor] = None
        result[name] = positions
    return result


def handle_set_torque(robots, mode, enable):
    targets = []
    if mode in ('robot1', 'both'):
        targets.append(robots['robot1'])
    if mode in ('robot2', 'both'):
        targets.append(robots['robot2'])
    for bus in targets:
        if enable:
            bus.enable_torque()
        else:
            bus.disable_torque()
    return {"ok": True}


# ── 수동(Teach) 모드 ────────────────────────────────────────────
# 손으로 팔을 밀어 자세를 잡는 모드.
#
# 【왜 토크를 "낮추는" 게 아니라 "끄는" 건가】
#   STS3215 는 1:345 감속기라 역구동이 구조적으로 안 된다.
#   Torque_Limit 을 500 → 192 (38%) 까지 내려도 손으로 안 꺾였다.
#   모터가 힘을 안 줘도 기어 마찰 자체가 남기 때문이다.
#   → 완전히 끄는 것 말고는 방법이 없다.
#
# 【그런데 다 끄면 팔이 주저앉는다】
#   그래서 중력 모멘트가 없는 관절만 끈다.
#     shoulder_pan  수직축 회전  → 중력 토크 0
#     wrist_roll    툴 축 회전   → 중력 토크 ~0
#     wrist_flex    그리퍼만 듦  → 살짝 처지지만 팔은 안 무너짐
#   무게를 드는 shoulder_lift / elbow_flex 는 토크를 유지하되 낮춘다.
#
# 【왜 서버 안에서 처리하나】
#   별도 스크립트로 끄려면 서버를 죽여야 한다(같은 시리얼 포트).
#   그러면 유니티가 끊겨 모델이 안 움직인다.
#   서버 명령으로 만들면 연결을 유지한 채 토크만 바꿀 수 있다.
#
# 【토크 OFF 관절은 Goal_Position 쓰기를 무시한다】
#   덕분에 서버가 계속 명령을 보내도 손으로 민 자리에 머문다.
# 그리퍼는 일부러 뺐다. 한 번 풀어서 시험해 봤지만 그리퍼도 1:345 감속이라
# 손으로 벌리거나 오므릴 수가 없었다. 풀어봐야 조작은 안 되면서 슬라이더만
# 못 쓰게 되므로, 수동모드에서도 토크를 유지하고 슬라이더로만 조작한다.
TEACH_FREE = ("shoulder_pan", "wrist_roll", "wrist_flex")

# 수동모드가 켜진 로봇. 지금은 진단·보고용으로만 쓴다.
TEACH_ON = {"robot1": False, "robot2": False}
# 중력을 버티는 관절(shoulder_lift / elbow_flex)은 수동모드에서도 토크를 유지한다.
#
# 예전에는 여기서 한계를 500 → 320/260 으로 낮췄다. "조금이라도 손으로 밀리게"
# 하려던 것인데, 두 가지가 확인되면서 없앴다.
#
#  1) 손으로 미는 데는 도움이 안 된다.
#     220/180 까지 내려도 느낌이 같았다. 1:345 감속에서는 버티는 힘이
#     모터가 아니라 기어 마찰이라 한계를 내려도 소용이 없다.
#
#  2) 오히려 해가 된다.
#     한계를 낮추면 중력을 이기고 팔을 **들어올릴 힘**이 모자란다.
#     내려가는 쪽은 중력이 도와 잘 가지만 올라가는 쪽은 도중에 스톨해서
#     "특정 각도 이상 안 움직이고 락 걸린 것처럼" 보인다.
#
# 이 두 관절은 슬라이더·각도 입력으로 조작한다. 비워 두면 아래 get() 이
# NORMAL_TORQUE_LIMIT 을 돌려주므로 평상시와 같은 힘으로 움직인다.
TEACH_HOLD = {}   # 평상값 500 그대로 사용
NORMAL_TORQUE_LIMIT = 500


def handle_teach(robots, mode, enable):
    targets = []
    if mode in ('robot1', 'both'):
        targets.append(('robot1', robots['robot1']))
    if mode in ('robot2', 'both'):
        targets.append(('robot2', robots['robot2']))

    result = {"ok": True, "mode": mode, "enable": enable, "robots": {}}
    for rname, bus in targets:
        detail = {}
        for name in MOTOR_NAMES:
            # 그리퍼도 푼다. 중력으로 무너질 게 없어서 완전 해제해도 안전하고,
            # 손으로 벌리고 오므린 상태까지 그대로 녹화되어야 한다.
            try:
                # ⚠️ 토크를 켜기 전에 목표를 현재 위치로 맞춰야 한다.
                #    안 그러면 예전 목표로 팔이 튄다.
                pos = int(bus.read("Present_Position", name, normalize=False))
                bus.write("Goal_Position", name, pos, normalize=False)

                if not enable:
                    bus.write("Torque_Limit", name, NORMAL_TORQUE_LIMIT)
                    bus.write("Torque_Enable", name, 1)
                    detail[name] = "hold"
                elif name in TEACH_FREE:
                    bus.write("Torque_Enable", name, 0)
                    detail[name] = "free"
                else:
                    bus.write("Torque_Limit", name,
                              TEACH_HOLD.get(name, NORMAL_TORQUE_LIMIT))
                    bus.write("Torque_Enable", name, 1)
                    detail[name] = "hold"
            except Exception as e:
                detail[name] = f"error: {e}"
                result["ok"] = False

        # 쓴 값을 믿지 말고 실제 레지스터를 되읽는다.
        # "토크가 안 빠진 것 같다" 를 로그만 보고 판단할 수 없어서 넣었다.
        for name in MOTOR_NAMES:
            try:
                te = int(bus.read("Torque_Enable", name, normalize=False))
                tl = int(bus.read("Torque_Limit", name, normalize=False))
                detail[name] = f"{detail.get(name, '?')}(실제 TE={te} TL={tl})"
            except Exception as e:
                detail[name] = f"{detail.get(name, '?')}(되읽기 실패: {e})"

        result["robots"][rname] = detail
        TEACH_ON[rname] = bool(enable)
        print(f"  {'🔓 수동모드 ON' if enable else '🔒 수동모드 OFF'} — {rname}: {detail}")
    return result


def handle_set_speed(robots, mode, velocity, acceleration):
    targets = []
    if mode in ('robot1', 'both'):
        targets.append(('robot1', robots['robot1']))
    if mode in ('robot2', 'both'):
        targets.append(('robot2', robots['robot2']))
    velocity = max(0, min(3000, int(velocity)))
    acceleration = max(1, min(254, int(acceleration)))
    for name, bus in targets:
        apply_speed_settings(bus, velocity, acceleration)
        print(f"  🎚️ {name} 속도: velocity={velocity}, accel={acceleration}")
    return {"ok": True, "velocity": velocity, "acceleration": acceleration}


def handle_temp_detail(robots, mode):
    """모터별 온도와 **서보에 설정된 과열 한계**를 그대로 돌려준다.

    최댓값 하나만 보면 "팔이 뜨겁다" 로 오해하기 쉽고,
    그 값이 위험한지 아닌지는 서보의 Max_Temperature_Limit 과 견줘야 안다.
    """
    targets = []
    if mode in ('robot1', 'both'):
        targets.append(('robot1', robots['robot1']))
    if mode in ('robot2', 'both'):
        targets.append(('robot2', robots['robot2']))

    out = {"ok": True, "type": "temp_detail"}
    for name, bus in targets:
        rows = {}
        for motor in MOTOR_NAMES:
            try:
                t = int(bus.read("Present_Temperature", motor, normalize=False))
                lim = int(bus.read("Max_Temperature_Limit", motor, normalize=False))
                load = int(bus.read("Present_Load", motor, normalize=False))
                if load > 511:
                    load -= 1024              # 10비트 2의 보수
                rows[motor] = {"temp": t, "limit": lim, "load": load}
            except Exception as e:
                rows[motor] = {"error": str(e)}
        out[name] = rows
    return out


def handle_status(robots, mode):
    """로봇 상태(발열·전압·부하)를 읽어 관제 화면으로 보낸다.

    ⚠️ 응답을 **평평한 키**로 준다. 유니티 JsonUtility 는 중첩 딕셔너리를
       다루기 번거로워서, 중첩 구조로 주면 파싱에서 시간을 버린다.
    """
    targets = []
    if mode in ('robot1', 'both'):
        targets.append(('r1', robots['robot1']))
    if mode in ('robot2', 'both'):
        targets.append(('r2', robots['robot2']))

    out = {"ok": True, "type": "status"}
    for tag, bus in targets:
        temps, volts, loads = [], [], []
        for motor in MOTOR_NAMES:
            try:
                t = int(bus.read("Present_Temperature", motor, normalize=False))
                v = int(bus.read("Present_Voltage", motor, normalize=False))
                l = int(bus.read("Present_Load", motor, normalize=False))
                # Present_Load 는 10비트 2의 보수다. 964 는 과부하가 아니라 -60.
                if l > 511:
                    l -= 1024
                temps.append(t)
                volts.append(v)
                loads.append(abs(l))
            except Exception as e:
                print(f"  ⚠ {tag} {motor} 상태 읽기 실패: {e}")

        # 값이 하나도 안 읽히면 키를 아예 넣지 않는다.
        # 0 을 넣으면 관제에서 "0도 / 0V" 로 오해된다.
        if temps:
            out[f"{tag}_temp"] = max(temps)          # 가장 뜨거운 모터가 기준
            # 어느 모터가 뜨거운지 모르면 "팔이 뜨겁다" 로 오해하게 된다.
            # 최댓값만 보내던 것을 모터 이름까지 함께 보낸다.
            out[f"{tag}_hot"] = MOTOR_NAMES[temps.index(max(temps))]
        if volts:
            out[f"{tag}_volt"] = round(sum(volts) / len(volts) / 10.0, 1)
        if loads:
            out[f"{tag}_load"] = max(loads)

    return out


def handle_set_position(robots, mode, motor, value):
    targets = []
    if mode == 'robot1':
        targets.append(('robot1', robots['robot1']))
    elif mode == 'robot2':
        targets.append(('robot2', robots['robot2']))
    elif mode == 'mirror':
        targets.append(('robot1', robots['robot1']))
        targets.append(('robot2', robots['robot2']))
    else:
        raise ValueError(f"Unknown mode: {mode}")

    for rname, bus in targets:
        # 그리퍼는 수동모드에서도 토크를 유지하므로(TEACH_FREE 주석 참조)
        # 여기서 걸러낼 것이 없다. 수동 중에도 슬라이더가 그대로 먹는다.
        bus.write("Goal_Position", motor, float(value))


# ============================================================
# 클라이언트 처리
# ============================================================

def handle_client(conn, addr, robots, calibrations):
    print(f"[+] 유니티 연결됨: {addr}")
    buffer = ""
    try:
        while True:
            data = conn.recv(4096)
            if not data:
                break
            buffer += data.decode('utf-8', errors='ignore')

            while '\n' in buffer:
                line, buffer = buffer.split('\n', 1)
                line = line.strip()
                if not line:
                    continue
                try:
                    msg = json.loads(line)
                except json.JSONDecodeError:
                    print(f"[{addr}] JSON 파싱 실패: {line!r}")
                    continue

                msg_type = msg.get('type', 'set')
                response = None

                try:
                    if msg_type == 'get':
                        response = handle_get_positions(robots, msg.get('mode', 'both'))

                    elif msg_type == 'torque':
                        response = handle_set_torque(
                            robots, msg.get('mode', 'both'), 
                            bool(msg.get('enable', True))
                        )

                    # 🆕 수동(Teach) 모드 — 손으로 밀 수 있게 토크 해제
                    elif msg_type == 'teach':
                        response = handle_teach(
                            robots, msg.get('mode', 'both'),
                            bool(msg.get('enable', False))
                        )

                    # 🆕 로봇 상태 — 발열 / 전압 / 부하
                    elif msg_type == 'temp_detail':
                        response = handle_temp_detail(robots, msg.get('mode', 'both'))

                    elif msg_type == 'status':
                        response = handle_status(robots, msg.get('mode', 'both'))

                    elif msg_type == 'set_speed':
                        response = handle_set_speed(
                            robots, msg.get('mode', 'both'),
                            msg.get('velocity', DEFAULT_VELOCITY),
                            msg.get('acceleration', DEFAULT_ACCELERATION)
                        )

                    # 🆕 홈으로 이동
                    elif msg_type == 'home':
                        response = handle_go_home(
                            robots, msg.get('mode', 'both')
                        )

                    # 🆕 안전한 홈포즈 지정
                    elif msg_type == 'set_home':
                        if not msg.get('confirm', False):
                            response = {
                                "ok": False,
                                "error": "confirm=true required for safety"
                            }
                        else:
                            robot_name = msg.get('mode', 'robot1')
                            response = handle_set_home_safe(
                                robots, msg.get('mode'), 
                                calibrations, robot_name
                            )

                    else:
                        # 기본 set 명령
                        mode = msg.get('mode')
                        motor = msg.get('motor')
                        value = msg.get('value')
                        if not mode or motor is None or value is None:
                            print(f"[{addr}] 필수 키 누락: {motor!r}, raw: {line!r}")
                            continue
                        handle_set_position(robots, mode, motor, value)
                        # set 명령은 응답 없음

                    if response is not None:
                        conn.sendall((json.dumps(response) + '\n').encode('utf-8'))

                except Exception as e:
                    print(f"[{addr}] 명령 처리 오류: {e}")
                    error_resp = {"ok": False, "error": str(e)}
                    try:
                        conn.sendall((json.dumps(error_resp) + '\n').encode('utf-8'))
                    except Exception:
                        pass

    except Exception as e:
        print(f"[{addr}] 연결 오류: {e}")
    finally:
        print(f"[-] 연결 종료: {addr}")
        try:
            conn.close()
        except Exception:
            pass


# ============================================================
# 메인
# ============================================================

def main():
    robots = {}
    calibrations = {}  # 메모리 캐시

    print("로봇 1 연결중...")
    robots['robot1'] = make_bus(PORTS['robot1'], CAL_FILE['robot1'])
    calibrations['robot1'] = load_calibration(CAL_FILE['robot1'])

    print("로봇 2 연결중...")
    robots['robot2'] = make_bus(PORTS['robot2'], CAL_FILE['robot2'])
    calibrations['robot2'] = load_calibration(CAL_FILE['robot2'])

    print("두 로봇 연결 완료!")
    print(f"  🎚️ 초기 속도: velocity={DEFAULT_VELOCITY}, accel={DEFAULT_ACCELERATION}")
    print(f"유니티 연결 대기중... (포트 {LISTEN_PORT})")
    print("Ctrl+C로 종료")
    print("v5: 홈으로 이동 + 안전한 홈포즈 지정 추가")

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((LISTEN_HOST, LISTEN_PORT))
    server.listen(5)

    def shutdown(signum, frame):
        print("\n[종료] 서버 중지 중...")
        try:
            server.close()
        except Exception:
            pass
        for name, bus in robots.items():
            try:
                bus.disable_torque()
                bus.disconnect()
            except Exception as e:
                print(f"  {name} 종료 실패: {e}")
        print("[종료] 완료. 안녕!")
        sys.exit(0)

    signal.signal(signal.SIGINT, shutdown)
    signal.signal(signal.SIGTERM, shutdown)

    try:
        while True:
            conn, addr = server.accept()
            t = threading.Thread(
                target=handle_client,
                args=(conn, addr, robots, calibrations),
                daemon=True,
            )
            t.start()
    except KeyboardInterrupt:
        shutdown(None, None)


if __name__ == '__main__':
    main()
