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
        // 홈 자세 — 2026-08-04 에 "접힌 자세" 로 바꿨다.
        // 전관절 0° 였던 예전 홈은 팔을 앞으로 쭉 뻗은 자세라(TCP 가 베이스에서 0.45m)
        // 켤 때마다 책상 앞을 크게 쓸고, 세워 둘 때도 자리를 많이 먹었다.
        //
        // ⚠️ 손으로 접어 놓은 실물 자세를 그대로 쓸 수는 없었다.
        //    실측 R1 (1.4, -96.3, 89.5, -100.0, 1.3) / R2 (-0.3, -98.6, 94.9, -99.4, 3.3) 인데
        //    elbow_flex 는 씬의 소프트 리밋(R1 70° / R2 64°)을, wrist_flex 는 하드 리밋(-95°)을
        //    각각 넘는다. 토크가 꺼져 있으면 손으로 거기까지 밀리지만 명령은 못 보낸다.
        //    그래서 두 로봇 공통으로 가능한 범위 안에서 가장 접힌 자세로 정했다.
        //    이 자세의 TCP 는 (0.039, 0, 0.364) — 베이스 축 바로 위다.
        //
        // ⚠️ wrist_flex 의 minAngle/maxAngle 은 정규화 스케일을 겸한다. 자세를 더 접겠다고
        //    이 값을 넓히면 전 관절 각도가 어긋난다. 넓혀야 하면 softMin/Max 만 건드릴 것.
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
                    minAngle = -110f, maxAngle = 110f, homeAngle = -90f
                },
                new SOArmJointConfig {
                    displayName = "J3 (Elbow Flex)",
                    motorName = "elbow_flex",
                    minAngle = -110f, maxAngle = 110f, homeAngle = 64f
                },
                new SOArmJointConfig {
                    displayName = "J4 (Wrist Flex)",
                    motorName = "wrist_flex",
                    minAngle = -110f, maxAngle = 110f, homeAngle = -80f
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
