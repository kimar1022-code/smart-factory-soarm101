#!/usr/bin/env python3
"""
PincOpen 그리퍼 기계적 한계 탐색 v2 — 펌웨어 위치 리밋을 일시 개방하고 탐색

【v1 이 실패한 이유】
  모터 펌웨어의 Min_Position_Limit=1972 가 닫힘 방향을 막고 있었다.
  이 값은 순정 moving_jaw 로 캘리브레이션할 때 기록된 것이라
  PincOpen 의 실제 행정과 맞지 않는다.
  goal 을 1956 으로 보내도 펌웨어가 1972 로 잘라서 1998 부근에서 멈췄다.

【이 스크립트가 하는 일】
  1. 현재 펌웨어 값 전부 백업 (JSON 파일로 남김)
  2. 위치 리밋 개방 (0 ~ 4095)
  3. 저토크로 양방향 기계적 끝 탐색
  4. 찾은 범위를 캘리브레이션 파일 + 펌웨어 리밋에 기록 (--write 일 때만)
  5. 실패하거나 중단되면 백업값으로 즉시 복구

【안전】
  · Overload_Torque 는 건드리지 않는다. 낮을수록(25) 보호가 빨리 걸려 안전하다.
  · 탐색 토크 450 — 평상 운전값 500 보다 낮다.
  · 부하 임계 초과가 연속 2회면 중단.
  · 어떤 예외가 나도 finally 에서 펌웨어를 원복한다.

【실행】
      pkill -9 -f "[r]obot_server_dual"      # -9 여야 팔 토크가 유지된다
      python pincopen_probe_v2.py --robot robot1
      python pincopen_probe_v2.py --robot robot1 --write
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

PROBE_TORQUE = 450
STEP = 15
SETTLE = 0.5
STALL_TICKS = 4
STALL_COUNT = 3
LOAD_LIMIT = 250        # 보호(Overload_Torque=25) 가 걸리기 전에 스스로 멈추도록 낮게
LOAD_STRIKES = 2
MAX_TRAVEL = 2900       # robot1 실측 행정이 2607틱이었으므로 넉넉히
BACKOFF = 30
SAFETY_MARGIN = 20      # 찾은 한계에서 이만큼 안쪽을 실사용 범위로 삼는다

# 백업/복구 대상 레지스터
FW_REGS = ["Min_Position_Limit", "Max_Position_Limit", "Torque_Limit",
           "Overload_Torque", "Protective_Torque"]

from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode


def rd(bus, reg, default=None):
    try:
        return int(bus.read(reg, "gripper", normalize=False))
    except Exception:
        try:
            return int(bus.read(reg, "gripper"))
        except Exception:
            return default


def load_abs(bus):
    """
    Present_Load 를 부호 있는 값으로 해석해 절대값을 돌려준다.

    ⚠️ 하위 10비트를 그냥 크기로 쓰면 안 된다. **10비트 2의 보수**다.
       실측: 한 방향에서 +60~84, 반대 방향에서 944~968 이 나왔는데
       964 = 1024 - 60 이다. 마스킹만 하면 -60 을 964 로 오해해
       멀쩡한 상태를 과부하로 판정하고 탐색이 즉시 중단된다.
    """
    v = (rd(bus, "Present_Load", 0) or 0) & 0x3FF
    if v > 511:
        v -= 1024
    return abs(v)


def probe(bus, direction, start_raw):
    name = "닫힘" if direction < 0 else "열림"
    print(f"\n── {name} 방향 탐색 ──")
    try:
        bus.write("Goal_Position", "gripper", int(start_raw), normalize=False)
        time.sleep(0.4)
    except Exception:
        pass

    goal, last = start_raw, start_raw
    stalls = strikes = travelled = 0

    while travelled < MAX_TRAVEL:
        goal = max(0, min(4095, goal + direction * STEP))
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
                print(f"  ⚠️ 부하 연속 초과 — 중단")
                break
        else:
            strikes = 0

        if moved < STALL_TICKS:
            stalls += 1
            if stalls >= STALL_COUNT:
                print(f"  ✅ {name} 한계: raw={pos}")
                break
        else:
            stalls = 0
        last = pos

    limit_raw = rd(bus, "Present_Position", last)
    try:    # 스토퍼에서 물러나 힘을 뺀다
        bus.write("Goal_Position", "gripper",
                  int(max(0, min(4095, limit_raw - direction * BACKOFF))), normalize=False)
        time.sleep(0.5)
    except Exception:
        pass
    return limit_raw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--robot", choices=list(PORTS), required=True)
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    cal_path = f"{CAL_DIR}/{args.robot}.json"
    with open(cal_path) as f:
        cal = json.load(f)
    old = dict(cal["gripper"])

    print("=" * 60)
    print(f"PincOpen 그리퍼 한계 탐색 v2 — {args.robot}")
    print("=" * 60)
    print(f"  캘리브 range : {old['range_min']} ~ {old['range_max']}")

    bus = FeetechMotorsBus(port=PORTS[args.robot],
        motors={"gripper": Motor(GRIPPER_ID, "sts3215", MotorNormMode.RANGE_M100_100)})
    bus.connect()

    # ── 1. 펌웨어 값 백업 ──
    fw_backup = {r: rd(bus, r) for r in FW_REGS}
    bpath = f"/home/sw/gripper_fw_backup_{args.robot}_{int(time.time())}.json"
    with open(bpath, "w") as f:
        json.dump(fw_backup, f, indent=2)
    print(f"\n  펌웨어 백업 → {bpath}")
    for k, v in fw_backup.items():
        print(f"    {k:22} = {v}")

    closed_raw = open_raw = None
    try:
        # ── 2. 위치 리밋 개방 ──
        bus.write("Lock", "gripper", 0)
        bus.write("Min_Position_Limit", "gripper", 0)
        bus.write("Max_Position_Limit", "gripper", 4095)
        bus.write("Torque_Limit", "gripper", PROBE_TORQUE)
        bus.write("Torque_Enable", "gripper", 1)
        print(f"\n  위치 리밋 개방 (0~4095), 탐색 토크 {PROBE_TORQUE}, 토크 ON")

        start = rd(bus, "Present_Position", 2048)
        print(f"  시작 raw = {start}")

        closed_raw = probe(bus, -1, start)

        cur = rd(bus, "Present_Position", closed_raw)
        print("\n── 방향 전환 (부드럽게) ──")
        for _ in range(8):
            cur = min(4095, cur + 20)
            bus.write("Goal_Position", "gripper", int(cur), normalize=False)
            time.sleep(0.3)
        mid = rd(bus, "Present_Position", cur)
        print(f"  전환 완료 raw={mid}")

        open_raw = probe(bus, +1, mid)

    except Exception as e:
        print(f"\n❌ 탐색 중 오류: {e}")
    finally:
        # ── 5. 펌웨어 원복 (실패해도 반드시) ──
        print("\n── 펌웨어 원복 ──")
        for r, v in fw_backup.items():
            if v is None:
                continue
            try:
                bus.write(r, "gripper", int(v))
                print(f"    {r:22} ← {v}")
            except Exception as e:
                print(f"    ⚠️ {r} 복구 실패: {e}")

    if closed_raw is None or open_raw is None:
        bus.disconnect()
        sys.exit("\n탐색이 완료되지 않았습니다. 펌웨어는 원복했습니다.")

    lo, hi = min(closed_raw, open_raw), max(closed_raw, open_raw)
    span = hi - lo
    print("\n" + "=" * 60)
    print(f"  닫힘 한계 raw : {closed_raw}")
    print(f"  열림 한계 raw : {open_raw}")
    print(f"  행정 폭       : {span} 틱 = {span*360/4096:.1f}° (모터축)")
    print(f"  기존 폭       : {old['range_max']-old['range_min']} 틱")
    print("=" * 60)

    if span < 200:
        print("\n❌ 행정이 너무 좁다 — 기록하지 않는다.")
        bus.disconnect()
        sys.exit(1)

    use_lo, use_hi = lo + SAFETY_MARGIN, hi - SAFETY_MARGIN
    print(f"\n  실사용 범위(양끝 {SAFETY_MARGIN}틱 여유) : {use_lo} ~ {use_hi}")
    if closed_raw > open_raw:
        print("  -100 = 열림, +100 = 닫힘 → Unity 의 InvertDirection = true 필요")
    else:
        print("  -100 = 닫힘, +100 = 열림 → InvertDirection = false 유지")

    if not args.write:
        print("\n🔍 미리보기 종료. 값이 타당하면 --write 로 다시 실행하세요.")
        bus.disconnect()
        return

    # ── 3~4. 캘리브 파일 + 펌웨어 리밋 기록 ──
    backup = cal_path + f".before_probe2_{int(time.time())}"
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
        bus.write("Lock", "gripper", 1)
        print(f"✅ 펌웨어 위치 리밋도 {use_lo}~{use_hi} 로 기록 (하드웨어 보호)")
    except Exception as e:
        print(f"⚠️ 펌웨어 리밋 기록 실패: {e}")

    print("\n다음: 서버 재시작 → bash /home/sw/start_server.sh")
    bus.disconnect()


if __name__ == "__main__":
    main()
