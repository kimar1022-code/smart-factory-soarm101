using System;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 두 대의 SO-ARM을 통합 관리.
    /// 스마트팩토리용 협동 제어 모드 포함.
    /// </summary>
    public class SOArmDualManager : MonoBehaviour
    {
        public enum ControlMode
        {
            Robot1Only,    // 1번만 제어
            Robot2Only,    // 2번만 제어
            Independent,   // 두 로봇 독립 (UI에서 각각)
            Mirror,        // 두 로봇 동시 같은 동작
            Cooperative    // 협동 작업 (스마트팩토리)
        }

        [Header("제어 모드")]
        public ControlMode controlMode = ControlMode.Robot1Only;

        [Header("로봇 매니저 (각 로봇의 Sim+Real 통합)")]
        public SOArmManager robot1;
        public SOArmManager robot2;

        public event Action<string> OnModeChanged;

        // ── 모드 별 활성화 여부 ──────────────────────────────
        bool Robot1Active =>
            controlMode != ControlMode.Robot2Only;

        bool Robot2Active =>
            controlMode == ControlMode.Robot2Only ||
            controlMode == ControlMode.Independent ||
            controlMode == ControlMode.Mirror ||
            controlMode == ControlMode.Cooperative;

        public void ChangeMode(ControlMode newMode)
        {
            controlMode = newMode;
            OnModeChanged?.Invoke(newMode.ToString());
            Debug.Log($"[DualManager] Mode: {newMode}");
        }

        // ── 한 번에 두 로봇 제어 (Mirror/Cooperative용) ────────
        public void SetJointBoth(int jointIndex, float angleDeg)
        {
            robot1?.SetJointTarget(jointIndex, angleDeg);
            robot2?.SetJointTarget(jointIndex, angleDeg);
        }

        public void SetGripperBoth(float percent)
        {
            robot1?.SetGripperTarget(percent);
            robot2?.SetGripperTarget(percent);
        }

        public void GoToHomeAll()
        {
            if (Robot1Active) robot1?.GoToHome();
            if (Robot2Active) robot2?.GoToHome();
        }

        public void StopAll()
        {
            robot1?.StopMotion();
            robot2?.StopMotion();
        }

        public void ConnectAll()
        {
            if (Robot1Active) robot1?.Connect();
            if (Robot2Active) robot2?.Connect();
        }

        // ── 모드별 라우팅 ────────────────────────────────────
        /// <summary>UI에서 호출. 현재 모드에 따라 적절한 로봇으로 라우팅.</summary>
        public void RouteJointCommand(bool fromRobot1UI, int jointIndex, float angleDeg)
        {
            switch (controlMode)
            {
                case ControlMode.Robot1Only:
                    if (fromRobot1UI) robot1?.SetJointTarget(jointIndex, angleDeg);
                    break;
                case ControlMode.Robot2Only:
                    if (!fromRobot1UI) robot2?.SetJointTarget(jointIndex, angleDeg);
                    break;
                case ControlMode.Independent:
                    if (fromRobot1UI) robot1?.SetJointTarget(jointIndex, angleDeg);
                    else robot2?.SetJointTarget(jointIndex, angleDeg);
                    break;
                case ControlMode.Mirror:
                case ControlMode.Cooperative:
                    SetJointBoth(jointIndex, angleDeg);
                    break;
            }
        }

        public void RouteGripperCommand(bool fromRobot1UI, float percent)
        {
            switch (controlMode)
            {
                case ControlMode.Robot1Only:
                    if (fromRobot1UI) robot1?.SetGripperTarget(percent);
                    break;
                case ControlMode.Robot2Only:
                    if (!fromRobot1UI) robot2?.SetGripperTarget(percent);
                    break;
                case ControlMode.Independent:
                    if (fromRobot1UI) robot1?.SetGripperTarget(percent);
                    else robot2?.SetGripperTarget(percent);
                    break;
                case ControlMode.Mirror:
                case ControlMode.Cooperative:
                    SetGripperBoth(percent);
                    break;
            }
        }
    }
}
