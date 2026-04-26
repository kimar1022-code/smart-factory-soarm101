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
                new SOArmJointConfig {
                    displayName = "J6 (Gripper)",
                    motorName = "gripper",
                    minAngle = -110f, maxAngle = 110f, homeAngle = 0f
                },
            };
        }
    }
}
