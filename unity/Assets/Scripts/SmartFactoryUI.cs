using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 스마트팩토리 제어 UI.
    /// 두 로봇을 한 화면에서 독립/동시 제어.
    /// </summary>
    public class SmartFactoryUI : MonoBehaviour
    {
        [Header("매니저 연결")]
        public SOArmDualManager dualManager;

        [Header("UI 크기")]
        public int panelWidth = 320;
        public int panelHeight = 560;

        // 슬라이더 값 (UI 측 캐싱)
        private float[] r1Sliders = new float[6];
        private float[] r2Sliders = new float[6];
        private float r1Gripper = 50f;
        private float r2Gripper = 50f;

        void Start()
        {
            if (dualManager == null) dualManager = FindObjectOfType<SOArmDualManager>();

            // 홈 포즈로 슬라이더 초기화
            InitSlidersFromRobot(r1Sliders, dualManager?.robot1);
            InitSlidersFromRobot(r2Sliders, dualManager?.robot2);
        }

        void InitSlidersFromRobot(float[] sliders, SOArmManager robot)
        {
            if (robot == null) return;
            var home = robot.GetHomePose();
            for (int i = 0; i < Mathf.Min(home.Length, sliders.Length); i++)
                sliders[i] = home[i];
        }

        void OnGUI()
        {
            if (dualManager == null) return;

            DrawModeBar();
            DrawRobotPanel("🤖 Robot 1", 10, 60, dualManager.robot1, r1Sliders, ref r1Gripper, true);
            DrawRobotPanel("🤖 Robot 2", panelWidth + 30, 60, dualManager.robot2, r2Sliders, ref r2Gripper, false);
            DrawCommonButtons();
        }

        void DrawModeBar()
        {
            GUI.Box(new Rect(10, 10, panelWidth * 2 + 30, 45),
                $"제어 모드: {dualManager.controlMode}");

            int x = 20, y = 28, w = 110, h = 22;
            if (GUI.Button(new Rect(x, y, w, h), "Robot1만"))
                dualManager.ChangeMode(SOArmDualManager.ControlMode.Robot1Only);
            if (GUI.Button(new Rect(x + 115, y, w, h), "Robot2만"))
                dualManager.ChangeMode(SOArmDualManager.ControlMode.Robot2Only);
            if (GUI.Button(new Rect(x + 230, y, w, h), "독립"))
                dualManager.ChangeMode(SOArmDualManager.ControlMode.Independent);
            if (GUI.Button(new Rect(x + 345, y, w, h), "미러"))
                dualManager.ChangeMode(SOArmDualManager.ControlMode.Mirror);
            if (GUI.Button(new Rect(x + 460, y, w, h), "협동"))
                dualManager.ChangeMode(SOArmDualManager.ControlMode.Cooperative);
        }

        void DrawRobotPanel(string title, int x, int y, SOArmManager robot,
            float[] sliders, ref float gripper, bool isRobot1)
        {
            GUI.Box(new Rect(x, y, panelWidth, panelHeight), title);

            string status = robot != null ? robot.StatusMessage : "Not assigned";
            GUI.Label(new Rect(x + 10, y + 25, panelWidth - 20, 40), $"상태: {status}");

            if (robot == null) return;

            int innerY = y + 70;

            for (int i = 0; i < Mathf.Min(sliders.Length, robot.JointCount); i++)
            {
                GUI.Label(new Rect(x + 10, innerY, panelWidth - 20, 20),
                    $"{robot.GetJointName(i)}: {sliders[i]:F1}°");
                innerY += 20;

                float min = robot.GetJointMinAngle(i);
                float max = robot.GetJointMaxAngle(i);
                float newVal = GUI.HorizontalSlider(
                    new Rect(x + 10, innerY, panelWidth - 20, 20),
                    sliders[i], min, max);

                if (Mathf.Abs(newVal - sliders[i]) > 0.01f)
                {
                    sliders[i] = newVal;
                    dualManager.RouteJointCommand(isRobot1, i, newVal);

                    // Mirror/Cooperative는 반대편 슬라이더도 동기화
                    if (dualManager.controlMode == SOArmDualManager.ControlMode.Mirror ||
                        dualManager.controlMode == SOArmDualManager.ControlMode.Cooperative)
                    {
                        if (isRobot1) r2Sliders[i] = newVal;
                        else r1Sliders[i] = newVal;
                    }
                }
                innerY += 30;
            }

            // 그리퍼
            GUI.Label(new Rect(x + 10, innerY, panelWidth - 20, 20),
                $"Gripper: {gripper:F0}%");
            innerY += 20;
            float newGrip = GUI.HorizontalSlider(
                new Rect(x + 10, innerY, panelWidth - 20, 20),
                gripper, 0f, 100f);
            if (Mathf.Abs(newGrip - gripper) > 0.5f)
            {
                gripper = newGrip;
                dualManager.RouteGripperCommand(isRobot1, gripper);

                if (dualManager.controlMode == SOArmDualManager.ControlMode.Mirror ||
                    dualManager.controlMode == SOArmDualManager.ControlMode.Cooperative)
                {
                    if (isRobot1) r2Gripper = gripper;
                    else r1Gripper = gripper;
                }
            }
            innerY += 30;

            // 홈 / 정지 버튼
            int btnW = (panelWidth - 30) / 2;
            if (GUI.Button(new Rect(x + 10, innerY, btnW, 25), "🏠 홈으로"))
            {
                robot.GoToHome();
                InitSlidersFromRobot(sliders, robot);
            }
            if (GUI.Button(new Rect(x + 20 + btnW, innerY, btnW, 25), "⏸ 정지"))
            {
                robot.StopMotion();
            }
        }

        void DrawCommonButtons()
        {
            int y = 60 + panelHeight + 10;
            if (GUI.Button(new Rect(10, y, 200, 35), "🏠 전체 홈으로"))
            {
                dualManager.GoToHomeAll();
                InitSlidersFromRobot(r1Sliders, dualManager.robot1);
                InitSlidersFromRobot(r2Sliders, dualManager.robot2);
            }
            if (GUI.Button(new Rect(220, y, 200, 35), "⏸ 전체 정지"))
                dualManager.StopAll();
            if (GUI.Button(new Rect(430, y, 230, 35), "🔌 재연결"))
                dualManager.ConnectAll();
        }
    }
}
