#!/usr/bin/env python3
"""placo 가 우리 URDF 를 읽고 FK/IK 를 실제로 푸는지 확인만 한다. 로봇은 안 건드린다."""
import sys
import numpy as np

URDF = "/home/sw/ik/so101_kin.urdf"
ARM_JOINTS = ["shoulder_pan", "shoulder_lift", "elbow_flex", "wrist_flex", "wrist_roll"]
TIP = "gripper_frame_link"

print("=" * 60)
print("1) placo 임포트")
import placo
print("   OK")

print("2) URDF 적재 (메시 없이 되는지)")
try:
    robot = placo.RobotWrapper(URDF)
    print("   OK — 메시 없이 적재됨")
except Exception as e:
    print(f"   실패: {type(e).__name__}: {e}")
    sys.exit(1)

print("3) URDF 가 아는 관절 전체")
names = list(robot.joint_names())
print("  ", names)

missing = [j for j in ARM_JOINTS if j not in names]
if missing:
    print(f"   ⚠ 팔 관절이 URDF 에 없다: {missing}")
    sys.exit(1)
print("   팔 5축 전부 존재")

print(f"4) TCP 프레임 '{TIP}' 존재 확인")
try:
    robot.update_kinematics()
    T = robot.get_T_world_frame(TIP)
    print("   OK")
except Exception as e:
    print(f"   실패: {type(e).__name__}: {e}")
    sys.exit(1)

print("5) FK — 홈자세(전부 0도) 의 TCP 위치")
from lerobot.model.kinematics import RobotKinematics
kin = RobotKinematics(urdf_path=URDF, target_frame_name=TIP, joint_names=ARM_JOINTS)
home = np.zeros(5)
T_home = kin.forward_kinematics(home)
p_home = T_home[:3, 3]
print(f"   위치(m): x={p_home[0]:+.4f}  y={p_home[1]:+.4f}  z={p_home[2]:+.4f}")

print("6) FK — 몇 가지 자세에서 TCP 가 실제로 움직이는가")
for label, q in [
    ("shoulder_pan +30", [30, 0, 0, 0, 0]),
    ("shoulder_lift -40", [0, -40, 0, 0, 0]),
    ("elbow_flex +50", [0, 0, 50, 0, 0]),
]:
    p = kin.forward_kinematics(np.array(q, dtype=float))[:3, 3]
    d = np.linalg.norm(p - p_home)
    print(f"   {label:22s} -> x={p[0]:+.4f} y={p[1]:+.4f} z={p[2]:+.4f}  (홈에서 {d*1000:6.1f}mm)")

print("7) IK — 왕복 검증 (알려진 자세로 FK -> 그 위치를 IK 로 되풀기)")
truth = np.array([20.0, -35.0, 45.0, 15.0, 0.0])
T_target = kin.forward_kinematics(truth)
start = np.zeros(5)

for ow in (0.01, 0.0):
    q = start.copy()
    # placo 는 QP 1스텝이라 반복해서 수렴시킨다
    for _ in range(60):
        q = kin.inverse_kinematics(q, T_target, position_weight=1.0, orientation_weight=ow)
    p_got = kin.forward_kinematics(q)[:3, 3]
    p_want = T_target[:3, 3]
    err_mm = np.linalg.norm(p_got - p_want) * 1000
    print(f"   orientation_weight={ow:<5} 위치오차 {err_mm:7.3f} mm")
    print(f"      정답 관절 {np.round(truth,1)}")
    print(f"      IK  관절 {np.round(q,1)}")

print("8) IK — 5축의 한계 확인 (자세까지 강제하면 어떻게 되는가)")
q = start.copy()
for _ in range(60):
    q = kin.inverse_kinematics(q, T_target, position_weight=1.0, orientation_weight=1.0)
p_got = kin.forward_kinematics(q)[:3, 3]
err_mm = np.linalg.norm(p_got - T_target[:3, 3]) * 1000
print(f"   orientation_weight=1.0  위치오차 {err_mm:7.3f} mm  <- 커지면 자세와 위치가 경합한 것")

print("=" * 60)
print("끝. 로봇은 건드리지 않았다.")
