#!/usr/bin/env python3
"""
Teach 모드용 토크 조절 — 손으로 밀 수 있되 중력에는 버티도록

【왜 완전히 끄면 안 되나】
  12V STS3215 는 토크를 끄면 팔이 중력으로 주저앉는다.
  (오늘 실제로 겪었다 — LeRobot 의 bus.disconnect() 가 토크를 꺼서 두 팔이 처졌다)
  그래서 **낮추되 끄지는 않는다.**

【관절별로 다르게 준다】
  어깨(shoulder_lift)가 팔 전체 무게를 든다 → 상대적으로 높게
  손목 쪽은 부하가 작다 → 낮게 해도 손으로 밀린다

【사용】
  python teach_torque.py --robot robot1 --on      # Teach 진입 (토크 낮춤)
  python teach_torque.py --robot robot1 --off     # 평상 복귀 (500)

  ⚠️ --on 직후에는 팔을 손으로 받치고 있을 것. 처지면 즉시 --off.
  ⚠️ Unity 쪽에서도 SOArmRealController 의 teachMode 를 켜야
     손으로 민 자리에 머문다. (안 켜면 Goal 로 되돌아감)
"""
import argparse
import time

PORTS = {
    "robot1": "/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00",
    "robot2": "/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14029636-if00",
}

# 관절별 Teach 토크 (평상값 500 기준)
TEACH = {
    "shoulder_pan":  200,   # 수평 회전 — 중력 부하 거의 없음
    "shoulder_lift": 320,   # 팔 전체를 듦 — 가장 높게
    "elbow_flex":    260,   # 전완 + 그리퍼
    "wrist_flex":    180,
    "wrist_roll":    150,
}
NORMAL = 500

# --free 에서 토크를 완전히 끄는 관절.
# STS3215 는 1:345 감속이라 Torque_Limit 을 낮춰도 기어 마찰 때문에 손으로 밀리지 않는다.
# 실제로 60% (lift 192) 까지 내려도 "안 꺾인다" 는 반응이었다.
# 중력 모멘트가 거의 없는 관절만 골라 끄면, 팔은 서 있으면서 그 관절들은 자유로워진다.
#   shoulder_pan  수직축 회전 → 중력 토크 0
#   wrist_roll    툴 축 회전  → 중력 토크 ~0
#   wrist_flex    그리퍼만 듦 → 살짝 처지지만 팔 전체는 안 무너짐
# shoulder_lift / elbow_flex 는 팔 무게를 드는 관절이라 절대 끄지 않는다.
FREE_OK = {"shoulder_pan", "wrist_roll", "wrist_flex"}

IDS ={"shoulder_pan": 1, "shoulder_lift": 2, "elbow_flex": 3,
       "wrist_flex": 4, "wrist_roll": 5}

from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--robot", choices=list(PORTS), required=True)
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--on", action="store_true")
    g.add_argument("--off", action="store_true")
    ap.add_argument("--scale", type=float, default=1.0,
                    help="Teach 토크 배율. 0.6 이면 기본값의 60%%. 낮출수록 손으로 밀기 쉽지만 처질 위험도 커진다.")
    ap.add_argument("--floor", type=int, default=80,
                    help="이 값 아래로는 내리지 않는다 (팔이 주저앉는 것을 막는 하한)")
    ap.add_argument("--free", action="store_true",
                    help="중력을 받지 않는 관절(pan/wrist_flex/wrist_roll)의 토크를 아예 끈다.\n"
                         "1:345 감속기라 토크만 낮춰서는 손으로 못 미는데, 끄면 자유롭게 움직인다.\n"
                         "무게를 드는 shoulder_lift / elbow_flex 는 토크를 유지해 팔이 주저앉지 않게 한다.")
    args = ap.parse_args()

    motors ={n: Motor(i, "sts3215", MotorNormMode.RANGE_M100_100) for n, i in IDS.items()}
    bus = FeetechMotorsBus(port=PORTS[args.robot], motors=motors)
    bus.connect()

    print(f"{args.robot} — Teach {'진입' if args.on else '해제'}")
    try:
        for name in IDS:
            free = args.on and args.free and name in FREE_OK
            val = max(args.floor, int(TEACH[name] * args.scale)) if args.on else NORMAL

            pos = int(bus.read("Present_Position", name, normalize=False))
            # 목표를 현재 위치로 먼저 맞춰 튐을 막고, 그 다음 토크 조정
            bus.write("Goal_Position", name, pos, normalize=False)
            bus.write("Torque_Limit", name, val)
            bus.write("Torque_Enable", name, 0 if free else 1)

            if free:
                print(f"  {name:15} 토크 OFF  ← 손으로 자유롭게    pos={pos}")
            else:
                print(f"  {name:15} Torque_Limit={val:4d}  (유지)      pos={pos}")
    finally:
        # ⚠️ bus.disconnect() 는 disable_torque() 를 호출해 팔을 주저앉힌다.
        try:
            bus.port_handler.closePort()
        except Exception:
            pass

    if args.on:
        print("\n⚠️ 손으로 받치면서 천천히 밀어보세요. 처지면 즉시 --off 로 복귀.")
        print("   Unity 의 SOArmRealController → teachMode 도 켜야 제자리에 머뭅니다.")
    else:
        print("\n평상 토크(500)로 복귀했습니다.")


if __name__ == "__main__":
    main()
