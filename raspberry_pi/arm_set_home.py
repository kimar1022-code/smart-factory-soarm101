#!/usr/bin/env python3
"""
팔 관절 0점 재정의 (set_home) — 지난번 실패 원인을 모두 반영한 버전

【지난번 왜 실패했나】
  1. Unity Play 중이라 10Hz 로 옛 목표를 계속 보냈고, 0점이 바뀐 순간
     그 숫자가 다른 물리 위치를 뜻하게 되어 팔이 끌려갔다.
  2. Homing_Offset 을 바꾸면 모터 안의 Goal_Position 도 새 좌표계로
     재해석된다. 그래서 오프셋을 쓴 직후 **Goal 을 현재 위치로 다시 맞춰주지
     않으면 모터가 스스로 움직인다.** 서버의 handle_set_home_safe 에 이 단계가 없다.

【이 스크립트】
  · 서버를 거치지 않고 직접 버스에 붙는다 (Unity 간섭 차단)
  · 각 모터의 raw 위치를 읽어 "지금 자세 = 0" 이 되도록 Homing_Offset 계산
  · 쓰기 직후 Goal_Position 을 현재 위치로 재설정 → 튐 방지
  · 캘리브레이션 파일도 같은 값으로 갱신
  · 실패 시 원래 오프셋으로 복구

【전제】
  실행 전에 팔을 **URDF 0° 자세**로 만들어 두어야 한다.
  (전완 수평 / 상완 약 75° — docs 의 유니티_0도기준 이미지 참고)
  그리퍼는 건드리지 않는다. 오늘 실측으로 맞춰놨다.

【실행】
  pkill -9 -f "[r]obot_server_dual"      # -9 여야 토크 유지
  python arm_set_home.py --robot robot1
  python arm_set_home.py --robot robot1 --write
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

# 팔 관절만. 그리퍼(ID 6)는 오늘 실측으로 맞춰놨으므로 제외한다.
ARM = [("shoulder_pan", 1), ("shoulder_lift", 2), ("elbow_flex", 3),
       ("wrist_flex", 4), ("wrist_roll", 5)]

from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--robot", choices=list(PORTS), required=True)
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    path = CAL.format(args.robot)
    with open(path) as f:
        cal = json.load(f)

    motors = {n: Motor(i, "sts3215", MotorNormMode.RANGE_M100_100) for n, i in ARM}
    bus = FeetechMotorsBus(port=PORTS[args.robot], motors=motors)
    bus.connect()

    def r(reg, m):
        return int(bus.read(reg, m, normalize=False))

    print("=" * 64)
    print(f"{args.robot} 팔 관절 0점 재정의 (그리퍼 제외)")
    print("=" * 64)

    plan = []
    for name, _ in ARM:
        g = cal[name]
        pos = r("Present_Position", name)          # 현재 Present (오프셋 반영됨)
        old_h = g["homing_offset"]
        mid = (g["range_min"] + g["range_max"]) / 2.0
        # norm 0 은 range 중앙이다. 지금 자세가 중앙으로 읽히게 오프셋을 옮긴다.
        #   Present = Actual - H  →  Present 를 (pos - mid) 만큼 낮추려면 H 를 그만큼 올린다
        delta = int(round(pos - mid))
        new_h = old_h + delta
        norm_now = (pos - g["range_min"]) / (g["range_max"] - g["range_min"]) * 200 - 100
        plan.append((name, old_h, new_h, delta, pos, norm_now))
        print(f"  {name:15} pos={pos:5d}  현재 norm={norm_now:+7.2f}  "
              f"H {old_h:6d} → {new_h:6d} ({delta:+d})")

    bad = [p for p in plan if not (-2047 <= p[2] <= 2047)]
    if bad:
        print("\n❌ homing_offset 이 11비트 범위(±2047) 를 벗어나는 관절이 있음:")
        for p in bad:
            print(f"   {p[0]}: {p[2]}")
        try:
            bus.port_handler.closePort()   # disconnect() 는 토크를 끈다
        except Exception:
            pass
        sys.exit(1)

    if not args.write:
        print("\n🔍 미리보기. 적용은 --write")
        try:
            bus.port_handler.closePort()   # disconnect() 는 토크를 끈다
        except Exception:
            pass
        return

    bak = path + f".before_armhome_{int(time.time())}"
    shutil.copy2(path, bak)
    print(f"\n캘리브 백업: {bak}")

    done = []
    try:
        for name, old_h, new_h, delta, pos, _ in plan:
            bus.write("Lock", name, 0)
            bus.write("Homing_Offset", name, new_h)
            time.sleep(0.15)

            # ⭐ 핵심: 오프셋을 바꾸면 Goal 도 새 좌표계로 해석된다.
            #    현재 위치를 다시 목표로 줘서 모터가 제자리에 머물게 한다.
            now = r("Present_Position", name)
            bus.write("Goal_Position", name, now, normalize=False)
            bus.write("Lock", name, 1)

            cal[name]["homing_offset"] = new_h
            done.append((name, old_h))
            print(f"  ✅ {name:15} H={new_h}  Present={now} (목표 재설정)")

        with open(path, "w") as f:
            json.dump(cal, f, indent=4)
        print("\n✅ 캘리브레이션 파일 기록 완료")

        print("\n적용 후 확인:")
        for name, _ in ARM:
            g = cal[name]
            pos = r("Present_Position", name)
            norm = (pos - g["range_min"]) / (g["range_max"] - g["range_min"]) * 200 - 100
            print(f"  {name:15} pos={pos:5d}  norm={norm:+7.2f}  (0 에 가까워야 정상)")

    except Exception as e:
        print(f"\n❌ 오류: {e}\n원래 오프셋으로 복구 시도")
        for name, old_h in done:
            try:
                bus.write("Lock", name, 0)
                bus.write("Homing_Offset", name, old_h)
                bus.write("Lock", name, 1)
                print(f"  복구 {name} → {old_h}")
            except Exception as e2:
                print(f"  ⚠️ {name} 복구 실패: {e2}")
    finally:
        # ⚠️ bus.disconnect() 를 부르면 안 된다.
        #    LeRobot 은 disconnect 안에서 disable_torque() 를 호출한다 (motors_bus.py:546).
        #    읽기만 하는 스크립트라도 종료하는 순간 팔 토크가 풀려 주저앉는다.
        #    (실제로 이 스크립트 미리보기 실행만으로 두 팔이 처졌다)
        #    포트만 닫고 토크는 그대로 둔다.
        try:
            bus.port_handler.closePort()
        except Exception:
            pass


if __name__ == "__main__":
    main()
