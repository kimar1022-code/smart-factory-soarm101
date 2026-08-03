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
  - 🆕 ik:  {"type":"ik", "current":[deg×5], "target":[x,y,z],
             "rot_delta":[dRx,dRy,dRz] (선택, 공구 축 기준 증분 deg),
             "orientation_weight":... (선택)}
            -> {"ok":true, "joints":[deg×5], "reached":[x,y,z], "reached_rpy":[r,p,y],
                "error_mm":..., "rot_error_deg":..., "converged":true}
            rot_delta 를 빼면 현재 자세를 유지한 채 위치만 옮긴다(예전 동작).
  - 🆕 fk:  {"type":"fk", "joints":[deg×5]}
            -> {"ok":true, "position":[x,y,z], "rpy":[roll,pitch,yaw]}

  ⚠️ ik/fk 는 계산만 한다. 모터를 건드리지 않는다. 단위는 degree/meter 이고
     서버의 정규화값(-100~100)이 아니다. 실제 이동은 유니티가 기존 set 경로로
     하므로 속도 제한·소프트 리밋·비상정지가 그대로 적용된다.
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

# IK 대상은 팔 5축뿐이다. gripper 는 자세를 안 바꾸므로 뺀다.
# (SO-ARM101 은 팔이 5축이다. 6축이 아니다 — 6번째는 그리퍼다)
ARM_JOINT_NAMES = [
    'shoulder_pan', 'shoulder_lift', 'elbow_flex',
    'wrist_flex', 'wrist_roll'
]

# 기구학 전용 URDF. so101.urdf 에서 visual/collision 을 걷어낸 것.
# 메시가 붙어 있으면 placo 가 DAE 를 찾다가 적재 자체가 실패한다.
IK_URDF = '/home/sw/ik/so101_kin.urdf'

# TCP 프레임. LeRobot 의 RobotKinematics 기본값과 같다.
# ⚠️ 순정 조 끝 기준이다. PincOpen 손끝으로 옮기는 건 아직 안 했다 (FR-39).
IK_TIP_FRAME = 'gripper_frame_link'

# 자세 가중치. 위치를 1.0 으로 두고 자세를 0.01 로 낮춘다.
#
# 왜 낮추나: 팔이 5축이라 임의의 6D 자세를 만들 수 없다. J2·J3·J4 가 서로 평행한
# pitch 축이라, 공구의 yaw 는 J1(팔이 놓인 평면)에 묶여 있다. 자세를 위치와
# 같은 무게로 요구하면 솔버가 둘을 맞바꾸며 위치가 어긋난다.
# LeRobot 본체(lerobot/model/kinematics.py)도 같은 이유로 0.01 을 기본값으로 쓴다.
# 0.0 으로 두면 자세를 아예 안 본다.
IK_ORIENTATION_WEIGHT = 0.01

# 자세를 **일부러 돌릴 때** 쓰는 가중치.
#
# 위 0.01 은 "자세는 대충 두고 위치만 맞춰라" 는 뜻이라, 자세 목표를 줘도
# 솔버가 거의 무시한다. Rx/Ry/Rz 버튼을 눌렀는데 아무 일도 안 나는 이유가 된다.
# 자세 목표(target_rpy)가 실제로 들어온 요청에만 이 값을 쓴다.
#
# ⚠️ 그래도 5축의 한계는 그대로다. J2·J3·J4 가 평행한 pitch 축이라
#    yaw 는 J1 에 묶여 독립적으로 안 돈다. 실제로 따라오는 건 손목 쪽이고,
#    나머지는 "가능한 만큼만" 맞춘다. converged 가 false 로 오면 그 경우다.
IK_ORIENTATION_WEIGHT_ROT = 0.6

# 회전을 시켰을 때 TCP 가 밀려나도 되는 한계(mm).
#
# 이 팔은 5축이라 회전과 위치를 동시에 다 못 맞춘다. 솔버는 자세를 맞추려고
# 위치를 희생하는데, 실측하면 그 정도가 축마다 극단적으로 다르다:
#   dRz(공구 롤) +5° → 0.6mm   — J5 만 돌면 되므로 사실상 공짜
#   dRy        +5° → 1.2mm   — 손목으로 흡수된다
#   dRx        +5° → 25mm    — yaw 가 J1 에 묶여 팔 전체가 돈다
#   dRx       +30° → 151mm   — 그대로 넣으면 팔이 날아간다
#
# "돌려라" 라고 눌렀는데 팔이 15cm 옆으로 가면 그건 사고다. 한계를 넘으면
# 결과를 주지 않고 거절한다. 유니티가 실수로 적용하는 경로 자체를 없앤다.
IK_ROT_MAX_DRIFT_MM = 5.0

# 수렴 조건. 라파4에서 1회 solve 가 0.11ms, 보통 4회면 0.1mm 밑으로 떨어진다.
IK_MAX_ITERS = 40
IK_TOLERANCE_M = 0.0002   # 0.2mm

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

# ============================================================
# 역기구학 (IK) — 계산만 한다. 모터는 건드리지 않는다.
# ============================================================
#
# 설계 의도:
#   이 명령은 모터에 아무것도 쓰지 않는다. (현재 각도, 목표 XYZ) → 관절 각도
#   를 돌려주는 순수 함수다. 실제로 팔을 움직이는 건 유니티가 기존 set 경로로
#   한다. 그래야 속도 제한 · 소프트 리밋 · 비상정지 · 그리퍼 안전 게이트가
#   전부 그대로 걸린다. 여기서 직접 모터를 돌리면 그 방어선을 통째로 우회한다.
#
#   단위는 degree / meter 다. 서버의 정규화값(-100~100)이 아니다.
#   정규화 변환은 유니티의 SOArmMotorMapper 가 이미 하고 있으므로 두 번 하지 않는다.
#
#   두 로봇은 기구학이 같으므로 solver 를 공유한다. mode 를 받지 않는 이유다.

_ik_lock = threading.Lock()
_ik_solver = None
_ik_error = None


def get_ik_solver():
    """placo 솔버를 처음 쓸 때 한 번만 만든다. 실패해도 서버는 계속 산다."""
    global _ik_solver, _ik_error
    if _ik_solver is not None or _ik_error is not None:
        return _ik_solver

    try:
        from lerobot.model.kinematics import RobotKinematics
        _ik_solver = RobotKinematics(
            urdf_path=IK_URDF,
            target_frame_name=IK_TIP_FRAME,
            joint_names=ARM_JOINT_NAMES,
        )
        print(f"  🧮 IK 준비됨 — {IK_URDF} / tip={IK_TIP_FRAME}")
    except Exception as e:
        # placo 미설치, URDF 없음, 프레임 이름 오타 등.
        # 서버를 죽이지 않는다. IK 명령만 실패로 답한다.
        _ik_error = f"{type(e).__name__}: {e}"
        print(f"  ⚠️ IK 사용 불가 — {_ik_error}")
    return _ik_solver


def rpy_from_matrix(R):
    """
    회전행렬 → (roll, pitch, yaw) 도.

    규약은 R = Rz(yaw) @ Ry(pitch) @ Rx(roll) 다. 유니티 쪽과 반드시 같아야
    하므로 여기서 한 번만 정하고 양쪽이 이것만 쓴다.

    pitch 가 ±90° 근처면 roll 과 yaw 가 같은 축이 되어(짐벌락) 나눌 수 없다.
    그때는 roll 을 0 으로 두고 yaw 에 몰아준다 — 값이 튀는 것보다 낫다.
    """
    import numpy as np
    sy = -float(R[2, 0])
    sy = max(-1.0, min(1.0, sy))
    pitch = np.arcsin(sy)

    if abs(sy) > 0.99999:
        roll = 0.0
        yaw = np.arctan2(-float(R[0, 1]), float(R[1, 1]))
    else:
        roll = np.arctan2(float(R[2, 1]), float(R[2, 2]))
        yaw = np.arctan2(float(R[1, 0]), float(R[0, 0]))

    return [round(float(np.degrees(v)), 3) for v in (roll, pitch, yaw)]


def matrix_from_rpy(rpy_deg):
    """(roll, pitch, yaw) 도 → 회전행렬. rpy_from_matrix 의 역이다."""
    import numpy as np
    r, p, y = [np.radians(float(v)) for v in rpy_deg[:3]]
    cr, sr = np.cos(r), np.sin(r)
    cp, sp = np.cos(p), np.sin(p)
    cy, sy = np.cos(y), np.sin(y)

    Rx = np.array([[1, 0, 0], [0, cr, -sr], [0, sr, cr]], dtype=float)
    Ry = np.array([[cp, 0, sp], [0, 1, 0], [-sp, 0, cp]], dtype=float)
    Rz = np.array([[cy, -sy, 0], [sy, cy, 0], [0, 0, 1]], dtype=float)
    return Rz @ Ry @ Rx


def handle_fk(joints_deg):
    """관절 각도(deg 5개) → TCP 위치(m) + 자세(rpy deg)."""
    solver = get_ik_solver()
    if solver is None:
        return {"ok": False, "type": "fk", "error": _ik_error}

    if not isinstance(joints_deg, list) or len(joints_deg) < len(ARM_JOINT_NAMES):
        return {"ok": False, "type": "fk",
                "error": f"joints 는 {len(ARM_JOINT_NAMES)}개여야 한다"}

    import numpy as np
    q = np.array(joints_deg[:len(ARM_JOINT_NAMES)], dtype=float)
    with _ik_lock:
        T = solver.forward_kinematics(q)
    p = T[:3, 3]
    # 자세도 같이 준다. 유니티가 Rx/Ry/Rz 버튼의 시작값으로 쓴다 —
    # 현재 자세에서 출발해야 첫 클릭에 손목이 안 튄다.
    return {"ok": True, "type": "fk",
            "position": [round(float(v), 5) for v in p],
            "rpy": rpy_from_matrix(T[:3, :3])}


def handle_ik(current_deg, target_xyz, orientation_weight=None, rot_delta=None):
    """
    (현재 관절 deg, 목표 TCP xyz m, 회전 증분 deg) → 관절 deg.

    placo 의 solve() 는 QP 한 스텝이라 한 번 부르면 목표로 조금 다가갈 뿐이다.
    수렴할 때까지 돌린다. 라파4에서 1회 0.11ms 라 40회를 돌아도 5ms 안쪽이다.

    ■ 회전을 왜 **절대 자세(rpy)가 아니라 증분**으로 받나
      두 가지 이유로 절대 rpy 는 이 팔에서 못 쓴다.

      1) 짐벌락. 홈 자세(관절 전부 0)의 TCP 자세가 rpy = [90, 87, 90] 이다.
         pitch 가 90° 코앞이라 roll 과 yaw 가 사실상 같은 축이 된다.
         이 근처에서 roll 을 건드리면 엉뚱한 축이 돈다.

      2) 목표가 너무 멀다. 절대값 [30,0,0] 을 주면 지금 자세에서 90° 가까이
         떨어진 목표가 되고, 5축 팔은 그걸 못 만든다. 실측으로 관절이 한계에
         박히고 위치가 272mm 벗어났다. 그대로 모터에 넣으면 팔이 날아간다.

      그래서 "지금 자세에서 공구 축 기준으로 이만큼 더 돌려라" 로 받는다.
      R_target = R_current @ Rz(dz) @ Ry(dy) @ Rx(dx)
      목표가 항상 현재 근처라 솔버가 안정적이고, 못 돌면 조금만 돌고 만다.

    rot_delta 를 빼면 예전과 똑같이 현재 자세를 유지한 채 위치만 옮긴다 —
    기존 호출부는 아무것도 안 바꿔도 그대로 돈다.
    """
    solver = get_ik_solver()
    if solver is None:
        return {"ok": False, "type": "ik", "error": _ik_error}

    n = len(ARM_JOINT_NAMES)
    if not isinstance(current_deg, list) or len(current_deg) < n:
        return {"ok": False, "type": "ik", "error": f"current 는 {n}개여야 한다"}
    if not isinstance(target_xyz, list) or len(target_xyz) < 3:
        return {"ok": False, "type": "ik", "error": "target 은 [x,y,z] 여야 한다"}

    import numpy as np

    want_rot = (isinstance(rot_delta, list) and len(rot_delta) >= 3
                and any(abs(float(v)) > 1e-6 for v in rot_delta[:3]))
    if orientation_weight is not None:
        ow = float(orientation_weight)
    else:
        # 회전을 일부러 시킨 요청이면 자세를 실제로 따라가게 무게를 올린다.
        ow = IK_ORIENTATION_WEIGHT_ROT if want_rot else IK_ORIENTATION_WEIGHT

    q = np.array(current_deg[:n], dtype=float)
    q_start = q.copy()
    target = np.array(target_xyz[:3], dtype=float)

    with _ik_lock:
        # 목표 4x4 행렬. 회전을 안 주면 현재 자세를 그대로 물려준다 —
        # 어차피 가중치가 낮아 구속이 약하고, 단위행렬을 넣으면
        # 솔버가 엉뚱한 자세로 끌려가려다 위치를 놓친다.
        T_target = solver.forward_kinematics(q).copy()
        R_start = T_target[:3, :3].copy()
        T_target[:3, 3] = target
        if want_rot:
            # 공구 축 기준. 오른쪽에 곱해야 "지금 잡고 있는 자세에서
            # 그 축으로" 가 된다. 왼쪽에 곱하면 베이스 축 기준이 된다.
            T_target[:3, :3] = R_start @ matrix_from_rpy(rot_delta)

        iters = 0
        err = None
        for _ in range(IK_MAX_ITERS):
            q = solver.inverse_kinematics(q, T_target,
                                          position_weight=1.0,
                                          orientation_weight=ow)
            iters += 1
            reached = solver.forward_kinematics(q)[:3, 3]
            err = float(np.linalg.norm(reached - target))
            if err < IK_TOLERANCE_M:
                break

        T_reached = solver.forward_kinematics(q)
        reached = T_reached[:3, 3]
        reached_rpy = rpy_from_matrix(T_reached[:3, :3])

        # 자세 오차 — 목표 자세를 준 경우에만 뜻이 있다.
        # 두 회전의 차이를 각도 하나로 줄인다: trace 로 회전각을 뽑는다.
        rot_err_deg = 0.0
        if want_rot:
            R_err = T_target[:3, :3].T @ T_reached[:3, :3]
            c = (float(np.trace(R_err)) - 1.0) / 2.0
            rot_err_deg = round(float(np.degrees(np.arccos(max(-1.0, min(1.0, c))))), 2)

    err_mm = round(err * 1000.0, 3)

    # 관절이 한 번에 얼마나 도는지. 팔이 거의 다 뻗은 자세(홈이 리치의 96%)에서는
    # 자코비안이 나빠서 TCP 를 몇 mm 옮기는 데도 관절이 수십 도 돈다. 사용자가
    # "조금 눌렀는데 팔이 확 움직인다" 고 느끼는 게 이것이라, 값을 같이 준다.
    max_step_deg = round(float(np.max(np.abs(q[:n] - q_start[:n]))), 2)

    # 회전 요청인데 팔이 밀려났으면 결과를 버린다.
    # 5축으로는 그 회전이 안 되는 것이고, 억지로 넣으면 팔이 크게 움직인다.
    if want_rot and err_mm > IK_ROT_MAX_DRIFT_MM:
        return {
            "ok": False,
            "type": "ik",
            "error": (f"그 축으로는 더 못 돈다 — 팔이 {err_mm:.0f}mm 밀려난다 "
                      f"(한계 {IK_ROT_MAX_DRIFT_MM:.0f}mm). 5축이라 이 자세에서는 "
                      f"그 회전이 안 된다."),
            "error_mm": err_mm,
            "rot_error_deg": rot_err_deg,
            "blocked_by": "rot_drift",
        }

    return {
        "ok": True,
        "type": "ik",
        "joints": [round(float(v), 3) for v in q[:n]],
        "reached": [round(float(v), 5) for v in reached],
        "reached_rpy": reached_rpy,
        "rot_error_deg": rot_err_deg,
        "error_mm": err_mm,
        "max_joint_step_deg": max_step_deg,
        "iters": iters,
        # 목표에 못 닿았으면 유니티가 사용자에게 알려줄 수 있게 표시한다.
        # 팔 길이를 넘었거나, 5축으로는 그 자세가 안 되는 경우다.
        "converged": err_mm <= 1.0,
    }


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

                    # 🆕 역기구학 — 계산만 한다. 모터는 안 건드린다.
                    #    실제 이동은 유니티가 기존 set 경로로 하므로
                    #    속도 제한 · 소프트 리밋 · 비상정지가 그대로 걸린다.
                    elif msg_type == 'ik':
                        response = handle_ik(
                            msg.get('current'),
                            msg.get('target'),
                            msg.get('orientation_weight'),
                            msg.get('rot_delta'),
                        )

                    elif msg_type == 'fk':
                        response = handle_fk(msg.get('joints'))

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
