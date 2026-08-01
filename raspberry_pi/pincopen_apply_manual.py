#!/usr/bin/env python3
"""
사람이 눈으로 확인한 그리퍼 양 끝을 캘리브레이션 + 펌웨어에 적용한다.

【왜 수동인가】
  자동 탐색(pincopen_probe_*.py)은 실패했다.
  STS3215 는 목표가 20틱 이내면 아예 움직이지 않는 불감대가 있는데,
  스톨 판정(4틱 미만 × 3회)이 이걸 "기계적 스토퍼"로 오인했다.
  그 결과 robot1 의 닫힘 끝을 839틱(73°) 이나 넘겨 잡았고,
  캠이 최대 닫힘을 지나 되벌어지는 구간까지 범위에 포함시켜
  "닫으라 했는데 열리는" 현상이 났다.
  → 눈으로 확인한 값이 유일하게 믿을 수 있다.

【규약】
  이 프로젝트는 percent 0 = 닫힘, 100 = 열림 이고
  PercentToServerValue 가 0% → norm -100 으로 보낸다.
  RANGE_M100_100 은 range_min 이 norm -100 이므로
  **range_min 에 '닫힘' raw 를 넣으면** 반전(invertSign) 없이 의미가 맞는다.

【실행】
  python pincopen_apply_manual.py --robot robot1 --closed 2525 --open 770
  python pincopen_apply_manual.py --robot robot1 --closed 2525 --open 770 --write
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
CAL = "/home/sw/.cache/huggingface/lerobot/calibration/robots/so_follower/{}.json"
MARGIN = 20      # 양 끝에서 물러날 틱 (스토퍼를 밀지 않도록)

from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--robot", choices=list(PORTS), required=True)
    ap.add_argument("--closed", type=int, required=True, help="눈으로 확인한 닫힘 raw")
    ap.add_argument("--open", type=int, required=True, help="눈으로 확인한 열림 raw")
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    path = CAL.format(args.robot)
    with open(path) as f:
        cal = json.load(f)
    old = dict(cal["gripper"])

    closed, opened = args.closed, args.open
    # 여유는 항상 '안쪽'으로 물러나게 계산한다 (어느 쪽이 크든 무관하게)
    sign = 1 if closed > opened else -1
    use_closed = closed - sign * MARGIN
    use_open = opened + sign * MARGIN

    lo, hi = min(use_open, use_closed), max(use_open, use_closed)

    print("=" * 58)
    print(f"{args.robot} 그리퍼 — 수동 확인값 적용")
    print("=" * 58)
    print(f"  확인한 닫힘 raw : {closed}")
    print(f"  확인한 열림 raw : {opened}")
    print(f"  행정            : {abs(closed-opened)} 틱 = {abs(closed-opened)*360/4096:.1f}°")
    print(f"  여유 {MARGIN}틱 적용 → 실사용 {use_open} ~ {use_closed}")
    print()
    print(f"  기존 range : {old['range_min']} ~ {old['range_max']}")
    print(f"  새   range : {lo} ~ {hi}")

    if not (0 <= lo < hi <= 4095):
        sys.exit(f"❌ range [{lo}, {hi}] 가 0~4095 범위를 벗어남")

    # norm -100 이 어느 쪽인지 알려준다
    if use_closed < use_open:
        print(f"\n  range_min({lo}) = 닫힘  → norm -100 = 닫힘  ✅ invertSign 불필요")
    else:
        print(f"\n  range_min({lo}) = 열림  → norm -100 = 열림  ⚠️ invertSign = true 필요")

    if not args.write:
        print("\n🔍 미리보기. 적용은 --write")
        return

    bak = path + f".before_manual_{int(time.time())}"
    shutil.copy2(path, bak)
    cal["gripper"]["range_min"] = lo
    cal["gripper"]["range_max"] = hi
    with open(path, "w") as f:
        json.dump(cal, f, indent=4)
    print(f"\n캘리브 백업: {bak}")
    print("✅ 캘리브레이션 파일 기록")

    bus = FeetechMotorsBus(port=PORTS[args.robot],
        motors={"gripper": Motor(6, "sts3215", MotorNormMode.RANGE_M100_100)})
    bus.connect()
    try:
        bus.write("Lock", "gripper", 0)
        bus.write("Min_Position_Limit", "gripper", lo)
        bus.write("Max_Position_Limit", "gripper", hi)
        bus.write("Lock", "gripper", 1)
        print(f"✅ 펌웨어 위치 리밋 {lo}~{hi} 기록 (소프트웨어가 틀려도 모터가 차단)")
        for r in ("Min_Position_Limit", "Max_Position_Limit", "Present_Position"):
            print(f"   확인 {r:22} = {bus.read(r, 'gripper', normalize=False)}")
    finally:
        try:
            bus.disconnect()
        except Exception:
            pass


if __name__ == "__main__":
    main()
