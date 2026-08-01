#!/usr/bin/env python3
"""
PincOpen 그리퍼 전용 재캘리브레이션

【왜 필요한가】
  현재 캘리브레이션의 gripper range 는 순정 moving_jaw 로 측정한 값이다.
  PincOpen 은 행정 거리가 달라서, -100(완전 닫힘)을 명령해도
  실제로는 1~2cm 벌어진 채로 멈춘다. (실측: 로봇1 20mm, 로봇2 11mm)

  → 손으로 실제 양 끝까지 움직여서 그 raw 값을 range_min/max 로 다시 기록한다.

【안전】
  · 그리퍼 모터(ID 6)의 토크만 끈다. 팔(ID 1~5)은 건드리지 않는다.
  · 그리퍼는 중력으로 떨어질 무게가 없어 토크를 꺼도 안전하다.
  · 이 스크립트는 --write 를 붙이기 전까지 아무것도 쓰지 않는다.

【실행 전】
  서버를 반드시 먼저 정지할 것. 시리얼 포트를 두 프로세스가 같이 못 쓴다.
      pkill -f "[r]obot_server_dual"
  ⚠️ 서버를 정지해도 모터 토크는 그대로 남아 팔은 제자리를 유지한다.

【실행】
      source ~/lerobot-env/bin/activate
      python pincopen_calibrate_gripper.py --robot robot1
      python pincopen_calibrate_gripper.py --robot robot1 --write
"""
import argparse
import json
import shutil
import sys
import time

PORTS = {
    "robot1": "/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00",
    "robot2": "/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14029636-if00",
}
CAL_DIR = "/home/sw/.cache/huggingface/lerobot/calibration/robots/so_follower"
GRIPPER_ID = 6

try:
    from lerobot.motors.feetech import FeetechMotorsBus
    from lerobot.motors.motors_bus import Motor, MotorNormMode
except ImportError as e:
    sys.exit(f"LeRobot 임포트 실패: {e}\n  → source ~/lerobot-env/bin/activate 확인")


def read_raw(bus):
    """오프셋이 반영된 Present_Position 을 정규화 없이 읽는다."""
    return int(bus.read("Present_Position", "gripper", normalize=False))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--robot", choices=["robot1", "robot2"], required=True)
    ap.add_argument("--write", action="store_true", help="실제로 캘리브레이션 파일에 기록")
    args = ap.parse_args()

    cal_path = f"{CAL_DIR}/{args.robot}.json"
    with open(cal_path) as f:
        cal = json.load(f)
    old = dict(cal["gripper"])

    print("=" * 62)
    print(f"PincOpen 그리퍼 재캘리브레이션 — {args.robot}")
    print("=" * 62)
    print(f"  현재 range        : {old['range_min']} ~ {old['range_max']}  (폭 {old['range_max']-old['range_min']})")
    print(f"  현재 homing_offset: {old['homing_offset']}")
    if not args.write:
        print("\n  🔍 미리보기 모드 — 측정만 하고 파일은 안 건드립니다.")
    print()

    bus = FeetechMotorsBus(
        port=PORTS[args.robot],
        motors={"gripper": Motor(GRIPPER_ID, "sts3215", MotorNormMode.RANGE_M100_100)},
    )
    bus.connect()
    print("모터 연결 완료\n")

    # ── 그리퍼 토크만 끈다 ──
    try:
        bus.write("Torque_Enable", "gripper", 0)
        print("✅ 그리퍼 토크 OFF — 이제 손으로 움직일 수 있습니다.")
    except Exception as e:
        bus.disconnect()
        sys.exit(f"❌ 토크 끄기 실패: {e}\n   손으로 안 움직이면 진행하지 마세요.")

    print("⚠️  팔은 건드리지 마세요. 팔 토크는 살아 있습니다.\n")

    try:
        # ── 완전히 닫힘 ──
        print("─" * 62)
        input("① 그리퍼를 손으로 **완전히 닫고**(손가락이 서로 닿을 때까지) Enter\n"
              "   ⚠️ 억지로 더 조이지 마세요. 닿는 지점까지만.\n> ")
        closed_raw = read_raw(bus)
        print(f"   → 닫힘 raw = {closed_raw}\n")

        # ── 완전히 열림 ──
        print("─" * 62)
        input("② 그리퍼를 손으로 **완전히 벌리고** Enter\n"
              "   ⚠️ 끝에서 더 힘주지 마세요.\n> ")
        open_raw = read_raw(bus)
        print(f"   → 열림 raw = {open_raw}\n")

    except KeyboardInterrupt:
        bus.disconnect()
        sys.exit("\n중단했습니다. 아무것도 바꾸지 않았습니다.")

    # ── 검증 ──
    span = abs(open_raw - closed_raw)
    print("=" * 62)
    print("측정 결과")
    print("=" * 62)
    print(f"  닫힘 raw : {closed_raw}")
    print(f"  열림 raw : {open_raw}")
    print(f"  행정 폭  : {span} 틱  =  {span * 360 / 4096:.1f}°  (모터축 기준)")
    print(f"  기존 폭  : {old['range_max']-old['range_min']} 틱")

    errs = []
    if span < 200:
        errs.append(f"행정 폭 {span}틱 은 너무 좁다. 양 끝까지 안 움직였을 가능성.")
    if span > 3000:
        errs.append(f"행정 폭 {span}틱 은 너무 넓다. 측정 오류 가능성.")
    lo, hi = min(closed_raw, open_raw), max(closed_raw, open_raw)
    if not (0 <= lo and hi <= 4095):
        errs.append(f"raw 값이 0~4095 범위를 벗어남: {lo}~{hi}")

    if errs:
        print("\n❌ 검증 실패 — 적용하지 않습니다:")
        for e in errs:
            print("   " + e)
        bus.disconnect()
        sys.exit(1)

    # 어느 쪽 끝이 -100 이 되는지 알려준다
    inverted = closed_raw > open_raw
    print(f"\n  range_min={lo}, range_max={hi} 로 기록하면")
    if inverted:
        print(f"    -100 = 열림({open_raw}),  +100 = 닫힘({closed_raw})   ← 방향이 뒤집힘")
        print(f"    → Unity 에서 PincOpenSafety.InvertDirection = true 로 켤 것")
    else:
        print(f"    -100 = 닫힘({closed_raw}),  +100 = 열림({open_raw})   ← 정방향")
        print(f"    → Unity 의 InvertDirection 은 false 그대로")

    if not args.write:
        print("\n🔍 미리보기 종료. 값이 타당하면 --write 로 다시 실행하세요.")
        bus.disconnect()
        return

    # ── 기록 ──
    backup = cal_path + f".before_gripcal_{int(time.time())}"
    shutil.copy2(cal_path, backup)
    print(f"\n백업: {backup}")

    cal["gripper"]["range_min"] = lo
    cal["gripper"]["range_max"] = hi
    with open(cal_path, "w") as f:
        json.dump(cal, f, indent=4)

    print("✅ 적용 완료.")
    print("   다음: 서버 재시작 → bash /home/sw/start_server.sh")
    print("   그 뒤 Unity 에서 그리퍼를 0%/100% 로 보내 실제로 닫히는지 확인하세요.")
    print("   ⚠️ 실물 그리퍼 명령은 PincOpenSafety.RealGripperEnabled 가 꺼져 있으면 차단됩니다.")

    bus.disconnect()


if __name__ == "__main__":
    main()
