#!/usr/bin/env python3
"""
PincOpen 그리퍼 기계적 한계 탐색 (모터 자력, 저토크)

【왜 이 방식인가】
  STS3215 는 1:345 감속 + PincOpen 캠(모터140°≈손가락44°) 이라
  **손으로는 역구동이 불가능**하다. 실측: 토크 OFF 상태에서 15초간 변화 0틱.
  그래서 모터를 아주 낮은 토크로 조금씩 밀어 "더 이상 안 가는 지점"을 찾는다.

【안전 설계】
  · Torque_Limit 을 평상시의 1/5 수준으로 낮춘 뒤 탐색한다.
    스토퍼에 닿아도 플라스틱이 버틸 수준의 힘만 쓴다.
  · 매 스텝마다 Present_Load 를 확인해 임계를 넘으면 즉시 후퇴·중단.
  · 위치 변화가 없으면(스톨) 그 지점을 한계로 판정하고 바로 물러난다.
  · 한 방향 최대 이동량을 제한해 폭주를 막는다.
  · 끝나면 Torque_Limit 을 원래대로 복구한다.

【실행】
  서버를 먼저 정지할 것 (시리얼 포트 충돌 방지):
      pkill -9 -f "[r]obot_server_dual"     # -9 여야 토크가 유지돼 팔이 안 떨어짐

      python pincopen_find_limits.py --robot robot1              # 탐색만
      python pincopen_find_limits.py --robot robot1 --write      # 결과를 캘리브에 기록
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

# ── 탐색 파라미터 ──────────────────────────────────────────
# 1차 시도(토크 200) 는 캠 마찰을 못 이겨 44틱 만에 멈췄다.
# 정지 시 부하가 116 으로 상한(200)에 못 미쳤던 것이 근거 — 스토퍼가 아니라 힘 부족이었다.
# 평상 운전값(Torque_Limit=500)보다는 낮게 유지한다.
PROBE_TORQUE = 450      # 탐색 중 토크 상한
STEP = 15               # 한 번에 밀 틱 수
SETTLE = 0.5            # 스텝 후 대기(초)
STALL_TICKS = 4         # 이만큼도 안 움직이면 정지로 간주
STALL_COUNT = 3         # 연속 몇 번 스톨이면 한계로 확정
LOAD_LIMIT = 600        # Present_Load 절대값 임계
LOAD_STRIKES = 2        # 연속 이 횟수 초과해야 중단 (순간 스파이크 무시)
MAX_TRAVEL = 1600       # 한 방향 최대 이동 틱 (폭주 방지)
BACKOFF = 25            # 한계 확정 후 물러날 틱
TRANSITION_STEP = 20    # 방향 전환 시 부드럽게 옮길 때의 스텝

from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode


def rd(bus, reg, default=None):
    try:
        return bus.read(reg, "gripper", normalize=False)
    except Exception:
        try:
            return bus.read(reg, "gripper")
        except Exception:
            return default


def probe(bus, direction, start_raw):
    """direction: +1 또는 -1. 스톨 지점의 raw 를 돌려준다."""
    name = "닫힘" if direction < 0 else "열림"
    print(f"\n── {name} 방향 탐색 (step {STEP}틱) ──")

    # 탐색 시작 전, 목표를 현재 위치에 맞춰둔다.
    # 이걸 빼면 직전 방향의 목표가 남아 있어 방향 전환 순간 큰 토크가 튄다.
    try:
        bus.write("Goal_Position", "gripper", int(start_raw), normalize=False)
        time.sleep(0.4)
    except Exception:
        pass

    goal = start_raw
    last = start_raw
    stalls = 0
    strikes = 0
    travelled = 0

    while travelled < MAX_TRAVEL:
        goal += direction * STEP
        goal = max(0, min(4095, goal))
        try:
            bus.write("Goal_Position", "gripper", int(goal), normalize=False)
        except Exception as e:
            print(f"  목표 기록 실패: {e}")
            break

        time.sleep(SETTLE)
        pos = int(rd(bus, "Present_Position", last))
        load = rd(bus, "Present_Load", 0)
        try:
            load_abs = abs(int(load) & 0x3FF)   # 하위 10비트가 크기, 상위는 방향
        except Exception:
            load_abs = 0

        moved = abs(pos - last)
        travelled += moved
        print(f"  goal={goal:4d}  pos={pos:4d}  이동={moved:3d}  부하={load_abs:4d}")

        # 방향 전환·기동 순간의 스파이크로 중단되지 않도록 연속 초과일 때만 멈춘다
        if load_abs > LOAD_LIMIT:
            strikes += 1
            print(f"     부하 초과 {strikes}/{LOAD_STRIKES}")
            if strikes >= LOAD_STRIKES:
                print(f"  ⚠️ 부하가 연속 {LOAD_STRIKES}회 {LOAD_LIMIT} 초과 — 중단")
                break
        else:
            strikes = 0

        if moved < STALL_TICKS:
            stalls += 1
            if stalls >= STALL_COUNT:
                print(f"  ✅ {name} 한계 도달: raw={pos}")
                break
        else:
            stalls = 0

        last = pos

    # 스토퍼에서 물러나 힘을 뺀다
    limit_raw = int(rd(bus, "Present_Position", last))
    relief = limit_raw - direction * BACKOFF
    try:
        bus.write("Goal_Position", "gripper", int(max(0, min(4095, relief))), normalize=False)
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
    print(f"PincOpen 그리퍼 한계 탐색 — {args.robot}")
    print("=" * 60)
    print(f"  기존 range : {old['range_min']} ~ {old['range_max']} (폭 {old['range_max']-old['range_min']})")
    print(f"  탐색 토크  : {PROBE_TORQUE} / 1000   부하 임계 {LOAD_LIMIT}")
    if not args.write:
        print("  🔍 탐색만 하고 파일은 안 건드립니다 (--write 로 기록)")

    bus = FeetechMotorsBus(
        port=PORTS[args.robot],
        motors={"gripper": Motor(GRIPPER_ID, "sts3215", MotorNormMode.RANGE_M100_100)},
    )
    bus.connect()

    saved_torque = rd(bus, "Torque_Limit", 1000)
    print(f"\n  현재 Torque_Limit = {saved_torque}")

    try:
        bus.write("Lock", "gripper", 0)
        bus.write("Torque_Limit", "gripper", PROBE_TORQUE)
        bus.write("Torque_Enable", "gripper", 1)
        print(f"  탐색용 토크 {PROBE_TORQUE} 적용, 토크 ON")
    except Exception as e:
        bus.disconnect()
        sys.exit(f"❌ 준비 실패: {e}")

    start = int(rd(bus, "Present_Position", 2048))
    print(f"  시작 위치 raw = {start}")

    try:
        closed_raw = probe(bus, -1, start)

        # 반대 방향으로 넘어가기 전에 여유 지점까지 부드럽게 이동한다.
        # 급하게 목표를 반대편으로 던지면 순간 토크가 크게 튄다 (1차 시도에서 부하 952 관측).
        cur = int(rd(bus, "Present_Position", closed_raw))
        print("\n── 방향 전환 (부드럽게 후퇴) ──")
        for _ in range(6):
            cur += TRANSITION_STEP
            bus.write("Goal_Position", "gripper", int(min(4095, cur)), normalize=False)
            time.sleep(0.3)
        mid = int(rd(bus, "Present_Position", cur))
        print(f"  전환 완료, 현재 raw={mid}")

        open_raw = probe(bus, +1, mid)
    finally:
        # 토크 상한 복구
        try:
            bus.write("Torque_Limit", "gripper", int(saved_torque))
            print(f"\n  Torque_Limit 복구 → {saved_torque}")
        except Exception as e:
            print(f"  ⚠️ Torque_Limit 복구 실패: {e}")

    lo, hi = min(closed_raw, open_raw), max(closed_raw, open_raw)
    span = hi - lo

    print("\n" + "=" * 60)
    print("탐색 결과")
    print("=" * 60)
    print(f"  닫힘 한계 raw : {closed_raw}")
    print(f"  열림 한계 raw : {open_raw}")
    print(f"  행정 폭       : {span} 틱 = {span*360/4096:.1f}° (모터축)")
    print(f"  기존 폭       : {old['range_max']-old['range_min']} 틱")

    if span < 200:
        print("\n❌ 행정이 너무 좁다 — 탐색 실패로 보인다. 기록하지 않는다.")
        bus.disconnect()
        sys.exit(1)

    inverted = closed_raw > open_raw
    print(f"\n  range_min={lo}, range_max={hi} 기록 시")
    if inverted:
        print("    -100 = 열림, +100 = 닫힘  → Unity 의 PincOpenSafety.InvertDirection = true")
    else:
        print("    -100 = 닫힘, +100 = 열림  → InvertDirection 은 false 유지")

    if args.write:
        backup = cal_path + f".before_limitprobe_{int(time.time())}"
        shutil.copy2(cal_path, backup)
        print(f"\n백업: {backup}")
        cal["gripper"]["range_min"] = lo
        cal["gripper"]["range_max"] = hi
        with open(cal_path, "w") as f:
            json.dump(cal, f, indent=4)
        print("✅ 캘리브레이션 기록 완료. 서버 재시작 필요.")
    else:
        print("\n🔍 값이 타당하면 --write 로 다시 실행하세요.")

    bus.disconnect()


if __name__ == "__main__":
    main()
