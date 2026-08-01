namespace SOArmControl
{
    /// <summary>
    /// SO-ARM101 6축 표준 프리셋.
    /// </summary>
    public static class SOArmPresets
    {
        /// <summary>
        /// SO-ARM101 6축 기본 설정.
        /// 모터 이름은 LeRobot 서버 프로토콜과 일치.
        /// </summary>
        public static SOArmJointConfig[] GetDefault6Axis()
        {
            return new SOArmJointConfig[]
            {
                new SOArmJointConfig {
                    displayName = "J1 (Shoulder Pan)",
                    motorName = "shoulder_pan",
                    minAngle = -110f, maxAngle = 110f, homeAngle = 0f
                },
                new SOArmJointConfig {
                    displayName = "J2 (Shoulder Lift)",
                    motorName = "shoulder_lift",
                    minAngle = -110f, maxAngle = 110f, homeAngle = 0f
                },
                new SOArmJointConfig {
                    displayName = "J3 (Elbow Flex)",
                    motorName = "elbow_flex",
                    minAngle = -110f, maxAngle = 110f, homeAngle = 0f
                },
                new SOArmJointConfig {
                    displayName = "J4 (Wrist Flex)",
                    motorName = "wrist_flex",
                    minAngle = -110f, maxAngle = 110f, homeAngle = 0f
                },
                new SOArmJointConfig {
                    displayName = "J5 (Wrist Roll)",
                    motorName = "wrist_roll",
                    minAngle = -110f, maxAngle = 110f, homeAngle = 0f
                },
                // ⚠️ PincOpen 그리퍼 (순정 moving_jaw 아님). 2026-08-01 확정.
                // 이 값은 시뮬 관절 = **손가락 각도**다. 모터 각도가 아니다.
                //   0° = 열림, -69.9° = 닫힘  (ROS2 xacro 1.22 rad, 부호는 렌더링 검증)
                // 순정 범위 -10°~100° 는 더 이상 유효하지 않다.
                new SOArmJointConfig {
                    displayName = "J6 (Gripper / PincOpen)",
                    motorName = "gripper",
                    minAngle = PincOpenCoupling.FingerClosedDeg,
                    maxAngle = PincOpenCoupling.FingerOpenDeg,
                    homeAngle = PincOpenCoupling.FingerOpenDeg
                },
            };
        }
    }
}
