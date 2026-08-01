#!/usr/bin/env python3
"""
robot2 그리퍼 homing_offset 재배치 — 인코더 랩어라운드 회피

【문제】
  robot2 그리퍼는 homing_offset = -1367 이다.
      Present = Actual - Homing_Offset = Actual + 1367
  Actual 이 0~4095 이므로 Present 는 1367~5462 가 되어 **4095 를 넘어 되감긴다.**
  실제로 닫힘 방향 탐색이 두 번 모두 raw 21 에서 끝났는데,
  스톨 곡선(부하 상승 + 이동량 감소)이 전혀 없었다 → 기계 한계가 아니라 숫자 바닥이다.
  (robot1 은 +1836 이라 이 문제가 없어 정상적으로 탐색됐다)

【해결】
  homing_offset 을 양수 쪽으로 옮겨 작동 구간을 4095 중앙 근처로 보낸다.
  Actual 을 그대로 두고 offset 만 바꾸면 Present 좌표계 전체가 평행이동한다.

【부작용】
  ⚠️ Homing_Offset 은 Goal_Position 해석에도 쓰이므로,
     바꾸는 순간 모터가 새 좌표계의 목표를 향해 **움직인다.**
     그래서 오프셋을 쓴 직후 Goal 을 현재 위치로 다시 맞춰 제자리에 세운다.
     (오늘 팔 set_home 에서 겪은 문제와 같은 원리)

【실행】
      python pincopen_fix_r2_offset.py            # 미리보기
      python pincopen_fix_r2_offset.py --write
"""
import json
import shutil
import sys
import time

CAL = "/home/sw/.cache/huggingface/lerobot/calibration/robots/so_follower/robot2.json"
PORT = "/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14029636-if00"

# 실측 역산으로 구한 값
#   열림 스토퍼 Present 3012  →  Actual = 3012 - 1367 = 1645
#   닫힘 방향 행정 약 2600틱  →  Actual 1645 → 0 을 지나 3141 로 되감김
#   즉 작동 구간이 인코더 0 을 가로지른다.
#   구간 중앙 Actual ≈ 1645 - 1300 = 345 를 Present 2047(중앙) 로 보내면
#   Present = (Actual - H) mod 4096 기준으로 747~3347 에 놓여 랩이 사라진다.
#       H = Actual_mid - 2047 = 345 - 2047 = -1702
NEW_H = -1702

from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode

with open(CAL) as f:
    cal = json.load(f)
g = cal["gripper"]
old_h = g["homing_offset"]
delta = NEW_H - old_h                      # Present 는 -delta 만큼 이동

print("=" * 58)
print("robot2 그리퍼 homing_offset 재배치")
print("=" * 58)
print(f"  homing_offset : {old_h} → {NEW_H}   (Δ {delta:+d})")
print(f"  Present 좌표  : {-delta:+d} 만큼 이동")
print(f"  캘리브 range  : {g['range_min']}~{g['range_max']}"
      f" → {g['range_min']-delta}~{g['range_max']-delta}")

# range 는 여기서 확정하지 않는다. 오프셋을 옮긴 뒤 다시 탐색해서 실측으로 잡는다.
# (기존 range 2011~3511 은 순정 기준이라 어차피 무효다)
new_min, new_max = 0, 4095
if not (-2047 <= NEW_H <= 2047):
    sys.exit(f"❌ homing_offset {NEW_H} 이 11비트 범위 초과. 중단.")
print("  range 는 재탐색으로 확정 — 여기서는 리밋을 열어만 둔다")

if "--write" not in sys.argv:
    print("\n🔍 미리보기. 적용은 --write")
    sys.exit(0)

bak = CAL + f".before_offsetfix_{int(time.time())}"
shutil.copy2(CAL, bak)
g["homing_offset"] = NEW_H
# range 는 재탐색이 확정한다. 지금은 전 구간으로 열어둔다.
g["range_min"], g["range_max"] = 0, 4095
with open(CAL, "w") as f:
    json.dump(cal, f, indent=4)
print(f"\n캘리브 백업: {bak}")
print("✅ 캘리브레이션 파일 기록")

bus = FeetechMotorsBus(port=PORT,
    motors={"gripper": Motor(6, "sts3215", MotorNormMode.RANGE_M100_100)})
bus.connect()
try:
    before = int(bus.read("Present_Position", "gripper", normalize=False))
    print(f"\n  변경 전 Present = {before}")

    bus.write("Lock", "gripper", 0)
    # 리밋을 먼저 넓혀두지 않으면 새 좌표계에서 현재 위치가 리밋 밖이 될 수 있다
    bus.write("Min_Position_Limit", "gripper", 0)
    bus.write("Max_Position_Limit", "gripper", 4095)
    bus.write("Torque_Enable", "gripper", 0)      # 튀는 것을 막기 위해 토크 OFF 후 변경
    bus.write("Homing_Offset", "gripper", NEW_H)
    time.sleep(0.4)

    after = int(bus.read("Present_Position", "gripper", normalize=False))
    print(f"  변경 후 Present = {after}   (기대 {before - delta})")

    # 새 좌표계 기준으로 목표를 현재 위치에 맞춘 뒤 토크 복귀
    bus.write("Goal_Position", "gripper", after, normalize=False)
    bus.write("Min_Position_Limit", "gripper", new_min)
    bus.write("Max_Position_Limit", "gripper", new_max)
    bus.write("Lock", "gripper", 1)
    print(f"  펌웨어 리밋 → {new_min} ~ {new_max}")
    print(f"  Goal 을 현재 위치({after})로 맞춰 제자리 유지")

    for r in ("Homing_Offset", "Min_Position_Limit", "Max_Position_Limit",
              "Present_Position", "Present_Temperature", "Status"):
        print(f"    {r:22} = {bus.read(r, 'gripper', normalize=False)}")
finally:
    try:
        bus.disconnect()
    except Exception:
        pass

print("\n다음: 랩어라운드가 사라졌으니 한계 탐색을 다시 실행하세요.")
print("      python pincopen_probe_v2.py --robot robot2")
