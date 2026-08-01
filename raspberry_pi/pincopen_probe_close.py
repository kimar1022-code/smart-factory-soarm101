#!/usr/bin/env python3
"""
PincOpen 닫힘 한계 탐색 (열림 한계는 이미 확인됨)

【지금까지 밝혀진 것】
  · raw 가 **낮을수록 열림**, 높을수록 닫힘 (방향이 예상과 반대였다)
  · 열림 기계 한계 ≈ raw 780  (부하가 완만히 상승하며 이동량이 줄다가 보호 발동)
  · 기존 캘리브 range_min=1972 는 순정 그리퍼 기준이라 105° 만큼 어긋나 있었다

【이번 탐색의 개선점】
  1차 탐색은 부하 임계를 600 으로 잡아 보호(Overload)가 먼저 걸렸고,
  그 상태에서는 레지스터 쓰기가 전부 거부돼 원복에 애를 먹었다.
  → 임계를 250 으로 낮추고 토크도 350 으로 줄여 **보호 발동 전에 스스로 멈춘다.**

【시작 위치 주의】
  현재 raw(≈782)가 펌웨어 Min_Position_Limit(1972) 보다 아래다.
  이 상태로 토크를 켜면 모터가 1972 로 튀어 오른다.
  → 리밋을 먼저 개방하고, Goal 을 현재 위치로 맞춘 뒤에 토크를 켠다.

【실행】
      python pincopen_probe_close.py --robot robot1
      python pincopen_probe_close.py --robot robot1 --write
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

PROBE_TORQUE = 350      # 1차(450)보다 낮춤
STEP = 15
SETTLE = 0.5
STALL_TICKS = 4
STALL_COUNT = 3
LOAD_LIMIT = 250        # 보호(Overload_Torque 근처) 전에 멈추도록 낮게
LOAD_STRIKES = 2
MAX_TRAVEL = 2600
BACKOFF = 30
SAFETY_MARGIN = 25

FW_REGS = ["Min_Position_Limit", "Max_Position_Limit", "Torque_Limit"]

from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode


def rd(bus, reg, default=None):
    try:
        return int(bus.read(reg, "gripper", normalize=False))
    except Exception:
        return default


def load_abs(bus):
    """
    Present_Load 를 부호 있는 값으로 해석해 절대값을 돌려준다.

    ⚠️ 하위 10비트를 그냥 크기로 쓰면 안 된다.
       실측: 열림 방향에서 +60~84, 닫힘 방향에서 964~968 이 나왔는데
       964 = 1024 - 60 이다. 즉 **10비트 2의 보수**로 인코딩돼 있다.
       마스킹만 하면 -60 을 964 로 오해해서 멀쩡한 상태를 과부하로 판정한다.
       (1차 탐색이 시작하자마자 멈춘 원인이 이것이었다)
    """
    v = rd(bus, "Present_Load", 0) or 0
    v &= 0x3FF
    if v > 511:
        v -= 1024
    return abs(v)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--robot", choices=list(PORTS), required=True)
    ap.add_argument("--open-raw", type=int, default=None,
                    help="이미 확인된 열림 한계 raw (기본: 현재 위치를 사용)")
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    cal_path = f"{CAL_DIR}/{args.robot}.json"
    with open(cal_path) as f:
        cal = json.load(f)
    old = dict(cal["gripper"])

    print("=" * 60)
    print(f"PincOpen 닫힘 한계 탐색 — {args.robot}")
    print("=" * 60)
    print(f"  기존 캘리브 range : {old['range_min']} ~ {old['range_max']}")
    print(f"  탐색 토크 {PROBE_TORQUE} / 부하 임계 {LOAD_LIMIT}")

    bus = FeetechMotorsBus(port=PORTS[args.robot],
        motors={"gripper": Motor(6, "sts3215", MotorNormMode.RANGE_M100_100)})
    bus.connect()

    fw_backup = {r: rd(bus, r) for r in FW_REGS}
    print(f"\n  펌웨어 백업: {fw_backup}")

    start = rd(bus, "Present_Position", 800)
    open_raw = args.open_raw if args.open_raw is not None else start
    print(f"  현재 raw = {start}  →  열림 한계로 사용: {open_raw}")

    closed_raw = None
    try:
        # ⚠️ 순서 중요: 리밋 개방 → Goal 을 현재 위치로 → 그 다음 토크 ON
        bus.write("Lock", "gripper", 0)
        bus.write("Min_Position_Limit", "gripper", 0)
        bus.write("Max_Position_Limit", "gripper", 4095)
        bus.write("Torque_Limit", "gripper", PROBE_TORQUE)
        bus.write("Goal_Position", "gripper", int(start), normalize=False)
        time.sleep(0.3)
        bus.write("Torque_Enable", "gripper", 1)
        print("  리밋 개방 + 현재 위치 고정 후 토크 ON")

        print("\n── 닫힘 방향(raw 증가) 탐색 ──")
        goal = last = start
        stalls = strikes = travelled = 0

        while travelled < MAX_TRAVEL:
            goal = min(4095, goal + STEP)
            bus.write("Goal_Position", "gripper", int(goal), normalize=False)
            time.sleep(SETTLE)

            pos = rd(bus, "Present_Position", last)
            load = load_abs(bus)
            moved = abs(pos - last)
            travelled += moved
            print(f"  goal={goal:4d}  pos={pos:4d}  이동={moved:3d}  부하={load:4d}")

            if load > LOAD_LIMIT:
                strikes += 1
                if strikes >= LOAD_STRIKES:
                    print(f"  ⚠️ 부하 {load} 연속 초과 — 스토퍼로 판단, 정지")
                    break
            else:
                strikes = 0

            if moved < STALL_TICKS:
                stalls += 1
                if stalls >= STALL_COUNT:
                    print(f"  ✅ 닫힘 한계: raw={pos}")
                    break
            else:
                stalls = 0
            last = pos

        closed_raw = rd(bus, "Present_Position", last)
        # 스토퍼에서 물러난다
        bus.write("Goal_Position", "gripper",
                  int(max(0, closed_raw - BACKOFF)), normalize=False)
        time.sleep(0.6)

    except Exception as e:
        print(f"\n❌ 탐색 중 오류: {e}")
    finally:
        print("\n── 정리 ──")
        for reg, val in (("Torque_Enable", 0),):
            for _ in range(4):
                try:
                    bus.write(reg, "gripper", val); print(f"  {reg} ← {val} ✅"); break
                except Exception:
                    time.sleep(0.8)

    if closed_raw is None:
        for r, v in fw_backup.items():
            try: bus.write(r, "gripper", int(v))
            except Exception: pass
        bus.disconnect(); sys.exit("탐색 실패. 펌웨어 원복 시도함.")

    lo, hi = min(open_raw, closed_raw), max(open_raw, closed_raw)
    span = hi - lo
    print("\n" + "=" * 60)
    print(f"  열림 한계 raw : {open_raw}")
    print(f"  닫힘 한계 raw : {closed_raw}")
    print(f"  행정 폭       : {span} 틱 = {span*360/4096:.1f}° (모터축)")
    print(f"  기존 폭       : {old['range_max']-old['range_min']} 틱")
    print("=" * 60)
    print("\n  ⚠️ raw 가 낮을수록 열림이므로,")
    print("     range_min(=norm -100) 은 '열림' 에 대응한다.")
    print("     → Unity 에서 PincOpenSafety.InvertDirection = true 로 켤 것")

    if span < 300:
        print("\n❌ 행정이 너무 좁다 — 기록하지 않음")
        for r, v in fw_backup.items():
            try: bus.write(r, "gripper", int(v))
            except Exception: pass
        bus.disconnect(); sys.exit(1)

    use_lo, use_hi = lo + SAFETY_MARGIN, hi - SAFETY_MARGIN
    print(f"\n  실사용 범위(양끝 {SAFETY_MARGIN}틱 여유): {use_lo} ~ {use_hi}")

    if not args.write:
        print("\n🔍 미리보기 종료. --write 로 기록하세요.")
        for r, v in fw_backup.items():
            try: bus.write(r, "gripper", int(v))
            except Exception: pass
        bus.disconnect(); return

    backup = cal_path + f".before_close_{int(time.time())}"
    shutil.copy2(cal_path, backup)
    cal["gripper"]["range_min"] = use_lo
    cal["gripper"]["range_max"] = use_hi
    with open(cal_path, "w") as f:
        json.dump(cal, f, indent=4)
    print(f"\n캘리브 백업: {backup}")
    print("✅ 캘리브레이션 range 기록 완료")

    try:
        bus.write("Lock", "gripper", 0)
        bus.write("Min_Position_Limit", "gripper", int(use_lo))
        bus.write("Max_Position_Limit", "gripper", int(use_hi))
        bus.write("Torque_Limit", "gripper", int(fw_backup["Torque_Limit"] or 500))
        bus.write("Lock", "gripper", 1)
        print(f"✅ 펌웨어 리밋 {use_lo}~{use_hi} 기록 (하드웨어 보호)")
    except Exception as e:
        print(f"⚠️ 펌웨어 리밋 기록 실패: {e}")

    bus.disconnect()


if __name__ == "__main__":
    main()
