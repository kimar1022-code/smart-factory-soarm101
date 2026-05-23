using UnityEngine;

namespace RobotControl
{
    public class RobotTester : MonoBehaviour
    {
        public RobotSocketClient socketClient;

        [Range(-100f, 100f)]
        public float testValue = 30f;

        void OnGUI()
        {
            if (socketClient == null) return;

            GUI.Box(new Rect(10, 10, 300, 450), "로봇 테스터");

            // 모드 선택
            GUI.Label(new Rect(20, 40, 280, 20), "모드 선택:");
            if (GUI.Button(new Rect(20, 60, 80, 30), "Robot1"))
                socketClient.SetMode(RobotSocketClient.RobotMode.Robot1);
            if (GUI.Button(new Rect(110, 60, 80, 30), "Robot2"))
                socketClient.SetMode(RobotSocketClient.RobotMode.Robot2);
            if (GUI.Button(new Rect(200, 60, 80, 30), "Mirror"))
                socketClient.SetMode(RobotSocketClient.RobotMode.Mirror);

            // 현재 모드 표시
            GUI.Label(new Rect(20, 95, 280, 20), $"현재: {socketClient.currentMode}");

            // 값
            GUI.Label(new Rect(20, 120, 280, 20), $"값: {testValue:F0}");
            testValue = GUI.HorizontalSlider(new Rect(20, 140, 280, 20), testValue, -100, 100);

            // 모터 버튼들
            int y = 170;
            if (GUI.Button(new Rect(20, y, 280, 30), $"Shoulder Pan → {testValue:F0}"))
                socketClient.SendMotorCommand("shoulder_pan", testValue);
            y += 35;
            if (GUI.Button(new Rect(20, y, 280, 30), $"Shoulder Lift → {testValue:F0}"))
                socketClient.SendMotorCommand("shoulder_lift", testValue);
            y += 35;
            if (GUI.Button(new Rect(20, y, 280, 30), $"Elbow Flex → {testValue:F0}"))
                socketClient.SendMotorCommand("elbow_flex", testValue);
            y += 35;
            if (GUI.Button(new Rect(20, y, 280, 30), $"Wrist Flex → {testValue:F0}"))
                socketClient.SendMotorCommand("wrist_flex", testValue);
            y += 35;
            if (GUI.Button(new Rect(20, y, 280, 30), $"Wrist Roll → {testValue:F0}"))
                socketClient.SendMotorCommand("wrist_roll", testValue);
            y += 35;
            if (GUI.Button(new Rect(20, y, 280, 30), $"Gripper → {testValue:F0}"))
                socketClient.SendMotorCommand("gripper", testValue);
            y += 40;
            if (GUI.Button(new Rect(20, y, 280, 30), "◯ 모든 모터 0으로 리셋"))
            {
                socketClient.SendMotorCommand("shoulder_pan", 0);
                socketClient.SendMotorCommand("shoulder_lift", 0);
                socketClient.SendMotorCommand("elbow_flex", 0);
                socketClient.SendMotorCommand("wrist_flex", 0);
                socketClient.SendMotorCommand("wrist_roll", 0);
                socketClient.SendMotorCommand("gripper", 0);
            }
        }
    }
}