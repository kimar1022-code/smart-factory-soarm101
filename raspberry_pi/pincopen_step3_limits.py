#!/usr/bin/env python3
"""
PincOpen 3단계 — 펌웨어 각도 리밋 + 보호 파라미터 굽기

⚠️ 이 스크립트는 모터 펌웨어에 값을 **씁니다**. 되돌리려면 다시 써야 합니다.
   반드시 아래 순서를 마친 뒤에 실행하세요.

     1단계  pincopen_step1_check.py 로 열림 -140° / 닫힘 0° 확인   ← 통과해야 함
     2단계  재캘리브레이션 (--robot.id 반드시 지정)
              python -m lerobot.scripts.lerobot_calibrate \
                --robot.type=so100_follower \
                --robot.port=<by-id 경로> --robot.id=robot1
     3단계  이 스크립트

값 출처: pollen-robotics/PincOpen  flash_and_tests/flash_test.ipynb
  같은 노트북 안에서 주석과 코드가 다른데, 코드 쪽이 더 보수적이라 코드 값을 쓴다.
    overload   주석 65% / 코드 40   → 40
    protective 주석 20% / 코드 5    → 5
    protect_t  주석 2(20ms) / 코드 7 → 7 (70ms)

실행:
    source ~/lerobot-env/bin/activate
    pkill -f robot_server_dual.py
    python pincopen_step3_limits.py            # 미리보기만 (쓰지 않음)
    python pincopen_step3_limits.py --write    # 실제로 굽기
"""

import argparse
import sys

PORT = "/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00"  # 로봇1
GRIPPER_ID = 6

# ── 굽을 값 ────────────────────────────────────────────────
MIN_ANGLE = -147     # 열림 하드리밋 (-140 대비 7° 여유)
MAX_ANGLE = 0        # 닫힘 하드리밋
TORQUE_LIMIT = 1000
OVERLOAD_TORQUE = 40      # 초과 시 보호 발동
PROTECTIVE_TORQUE = 5     # 발동 후 이 값으로 강하
PROTECTION_TIME = 7       # 70ms
ACCELERATION = 200

# LeRobot 레지스터 이름이 확실하지 않아 후보를 여러 개 둔다.
# 1단계에서 출력한 실제 이름과 대조해 필요하면 수정할 것.
REGISTERS = [
    (["Min_Angle_Limit", "Min_Position_Limit"], MIN_ANGLE, "열림 하드리밋"),
    (["Max_Angle_Limit", "Max_Position_Limit"], MAX_ANGLE, "닫힘 하드리밋"),
    (["Torque_Limit", "Max_Torque_Limit"], TORQUE_LIMIT, "토크 상한"),
    (["Overload_Torque", "Over_Load_Torque"], OVERLOAD_TORQUE, "과부하 임계"),
    (["Protective_Torque", "Protection_Torque"], PROTECTIVE_TORQUE, "보호 시 토크"),
    (["Protection_Time", "Protect_Time"], PROTECTION_TIME, "보호 발동 시간"),
    (["Acceleration", "Accel"], ACCELERATION, "가속도"),
]

try:
    from lerobot.motors.feetech import FeetechMotorsBus
    from lerobot.motors.motors_bus import Motor, MotorNormMode
except ImportError as e:
    sys.exit(f"LeRobot 임포트 실패: {e}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true",
                    help="실제로 펌웨어에 쓴다. 없으면 미리보기만.")
    ap.add_argument("--port", default=PORT)
    ap.add_argument("--id", type=int, default=GRIPPER_ID)
    args = ap.parse_args()

    print("=" * 60)
    print("PincOpen 3단계 — 펌웨어 리밋/보호 설정")
    print("=" * 60)
    if not args.write:
        print("🔍 미리보기 모드 — 아무것도 쓰지 않습니다.")
        print("   실제로 굽으려면 --write 를 붙이세요.\n")
    else:
        print("⚠️  쓰기 모드입니다. 펌웨어가 변경됩니다.\n")

    bus = FeetechMotorsBus(
        port=args.port,
        motors={"gripper": Motor(args.id, "sts3215", MotorNormMode.RANGE_M100_100)},
    )
    bus.connect()

    table = bus.model_ctrl_table["sts3215"]

    def resolve(names):
        for n in names:
            if n in table:
                return n
        return None

    # ── 현재 값 읽기 + 이름 확인 ──
    print("─" * 60)
    print(f"{'항목':<16}{'레지스터':<22}{'현재':>8} → {'설정':>8}")
    print("─" * 60)

    plan = []
    missing = []
    for names, value, label in REGISTERS:
        reg = resolve(names)
        if reg is None:
            missing.append((label, names))
            print(f"{label:<16}{'(못 찾음)':<22}{'-':>8}   {value:>8}")
            continue
        try:
            cur = bus.read(reg, "gripper")
        except Exception as e:
            cur = f"err"
        print(f"{label:<16}{reg:<22}{str(cur):>8} → {value:>8}")
        plan.append((reg, value, label))

    if missing:
        print("\n⚠️ 아래 항목은 이 LeRobot 버전에서 이름을 못 찾았습니다:")
        for label, names in missing:
            print(f"   {label}: 후보 {names}")
        print("   1단계 스크립트가 출력한 실제 이름과 대조해 REGISTERS 를 고치세요.")
        print("   각도 리밋을 못 쓰면 하드웨어 보호가 없는 상태이니 진행하지 마세요.")

    if not args.write:
        print("\n미리보기 종료. 문제 없으면 --write 로 다시 실행하세요.")
        bus.disconnect()
        return

    if missing:
        sys.exit("\n❌ 이름을 못 찾은 항목이 있어 중단합니다. (안전을 위해)")

    # ── 확인 ──
    ans = input("\n정말 펌웨어에 쓸까요? 'yes' 입력: ").strip().lower()
    if ans != "yes":
        print("취소했습니다.")
        bus.disconnect()
        return

    # ── 굽기 ──
    lock_reg = resolve(["Lock"])
    try:
        if lock_reg:
            bus.write(lock_reg, "gripper", 0)      # 잠금 해제
            print("잠금 해제")

        for reg, value, label in plan:
            bus.write(reg, "gripper", value)
            print(f"  ✅ {label:<16} {reg} = {value}")

        if lock_reg:
            bus.write(lock_reg, "gripper", 1)      # 잠금 (EEPROM 확정)
            print("잠금 완료")

        print("\n✅ 완료. 이제 모터가 스스로 범위를 벗어나는 명령을 거부합니다.")
        print("   다음: Unity 에서 PincOpenSafety.RealGripperEnabled 를 켜세요.")
        print("   (Assets/Script/PincOpenSafety.cs 또는 인스펙터)")
    except Exception as e:
        print(f"\n❌ 쓰기 실패: {e}")
        print("   전원을 끄고 배선/ID 를 확인하세요.")
    finally:
        bus.disconnect()


if __name__ == "__main__":
    main()
