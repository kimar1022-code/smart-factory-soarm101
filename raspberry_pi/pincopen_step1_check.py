#!/usr/bin/env python3
"""
PincOpen 1단계 — 실물 확인 (읽기 전용, 아무것도 쓰지 않음)

목적: 그리퍼 모터(ID 6)를 손으로 열고 닫으면서 실제 각도를 읽는다.
      열림이 -140° 근처, 닫힘이 0° 근처여야 다음 단계로 갈 수 있다.

⚠️ 이 스크립트는 모터에 아무 값도 쓰지 않는다. 토크만 끄고 읽기만 한다.
   따라서 이 단계에서 그리퍼가 부러질 일은 없다.

실행:
    source ~/lerobot-env/bin/activate
    pkill -f robot_server_dual.py      # 서버가 포트를 잡고 있으면 종료
    python pincopen_step1_check.py
"""

import sys
import time

PORT = "/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00"  # 로봇1
GRIPPER_ID = 6

try:
    from lerobot.motors.feetech import FeetechMotorsBus
    from lerobot.motors.motors_bus import Motor, MotorNormMode
except ImportError as e:
    sys.exit(f"LeRobot 임포트 실패: {e}\n  → source ~/lerobot-env/bin/activate 했는지 확인")


def main():
    print("=" * 60)
    print("PincOpen 1단계 — 실물 각도 확인 (읽기 전용)")
    print("=" * 60)

    # RANGE_M100_100 이 아니라 원시 각도를 보려고 DEGREES 를 쓴다.
    # 이 모드가 없으면 정규화값으로 읽고 나중에 환산한다.
    try:
        norm = MotorNormMode.DEGREES
        mode_name = "DEGREES (각도 직접)"
    except AttributeError:
        norm = MotorNormMode.RANGE_M100_100
        mode_name = "RANGE_M100_100 (정규화값)"

    bus = FeetechMotorsBus(
        port=PORT,
        motors={"gripper": Motor(GRIPPER_ID, "sts3215", norm)},
    )

    print(f"\n포트 : {PORT}")
    print(f"모드 : {mode_name}")

    bus.connect()
    print("연결 완료\n")

    # ── 레지스터 이름 확인 (PINCOPEN.md 3-(1) 미검증 항목) ──
    print("─" * 60)
    print("이 펌웨어가 가진 레지스터 이름 (각도/토크 관련만)")
    print("─" * 60)
    try:
        keys = list(bus.model_ctrl_table["sts3215"].keys())
        interesting = [k for k in keys
                       if any(w in k.lower() for w in
                              ("angle", "limit", "torque", "load", "protect", "lock", "accel"))]
        for k in sorted(interesting):
            print(f"  {k}")
        print(f"\n  (전체 {len(keys)}개 중 {len(interesting)}개 표시)")
        print("  ⚠️ 이 목록을 docs/PINCOPEN.md 3-(1)절에 기록해 두세요.")
    except Exception as e:
        print(f"  레지스터 목록 읽기 실패: {e}")

    # ── 토크 OFF — 손으로 움직일 수 있게 ──
    print("\n" + "─" * 60)
    print("토크를 끕니다. 그리퍼를 손으로 움직일 수 있게 됩니다.")
    print("─" * 60)
    try:
        bus.disable_torque()
    except Exception:
        try:
            bus.write("Torque_Enable", "gripper", 0)
        except Exception as e:
            print(f"  ⚠️ 토크 끄기 실패: {e}")
            print("  손으로 안 움직이면 여기서 중단하세요.")

    print("\n⚠️ 그리퍼만 손으로 움직이세요. 팔은 건드리지 마세요.")
    print("   (팔은 토크가 살아 있습니다)\n")

    def read_pos():
        try:
            return bus.read("Present_Position", "gripper")
        except Exception as e:
            return f"읽기 실패({e})"

    # ── 열림 위치 ──
    input("① 그리퍼를 손으로 **완전히 벌린** 뒤 Enter: ")
    opened = read_pos()
    print(f"   → 열림 위치 = {opened}\n")

    # ── 닫힘 위치 ──
    input("② 그리퍼를 손으로 **완전히 닫은** 뒤 Enter: ")
    closed = read_pos()
    print(f"   → 닫힘 위치 = {closed}\n")

    # ── 실시간 모니터 ──
    print("─" * 60)
    print("실시간 위치 (그리퍼를 천천히 여닫아 보세요). Ctrl+C 로 종료")
    print("─" * 60)
    try:
        while True:
            print(f"\r  현재 위치: {read_pos()}        ", end="", flush=True)
            time.sleep(0.1)
    except KeyboardInterrupt:
        print("\n")

    # ── 판정 ──
    print("=" * 60)
    print("판정")
    print("=" * 60)
    print(f"  열림 = {opened}")
    print(f"  닫힘 = {closed}")
    print()
    print("  기대값 (공식 노트북 flash_test.ipynb 기준)")
    print("    열림 ≈ -140°,  닫힘 ≈ 0°")
    print()
    print("  ✅ 근처면  → 2단계(재캘리브레이션)로 진행")
    print("  ❌ 다르면  → 여기서 중단. 조립 문제 또는 모터 0점 불일치.")
    print("               억지로 진행하면 플라스틱이 부러집니다.")
    print()
    print("  ※ 정규화값(-100~100)으로 읽혔다면 각도가 아닙니다.")
    print("     그 경우 값의 '범위'와 '방향'만 보고, 각도 판정은")
    print("     재캘리브레이션 뒤에 다시 하세요.")

    try:
        bus.disconnect()
    except Exception:
        pass


if __name__ == "__main__":
    main()
