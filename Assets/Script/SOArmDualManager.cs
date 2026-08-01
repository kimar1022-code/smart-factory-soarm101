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
        /// <summary>제어 방식. "어느 팔을 쓰나"와는 직교 — 그건 robot1/2Enabled 담당.</summary>
        public enum ControlMode
        {
            Independent,   // 두 로봇 독립 (UI 슬라이더가 각자 제어)
            Mirror,        // 두 로봇 동시 같은 동작
        }

        [Header("제어 모드")]
        public ControlMode controlMode = ControlMode.Independent;

        [Header("녹화 모드 (제어 모드와 직교 — 어느 모드에서든 켤 수 있음)")]
        public bool isRecordModeActive = false;

        [Header("채널 활성화 (연결/명령 대상. 한 대만 켜고 쓰려면 여기서 끄기)")]
        public bool robot1Enabled = true;
        public bool robot2Enabled = true;

        [Header("로봇 매니저 (각 로봇의 Sim+Real 통합)")]
        public SOArmManager robot1;
        public SOArmManager robot2;

        public event Action<string> OnModeChanged;

        // ── 채널 활성화 여부 ────────────────────────────────
        // 예전엔 ControlMode(Robot1Only/Robot2Only)가 연결까지 겸했는데,
        // "제어 방식"과 "채널 on/off"는 다른 축이라 분리함.
        bool Robot1Active => robot1Enabled;
        bool Robot2Active => robot2Enabled;

        public void ChangeMode(ControlMode newMode)
        {
            controlMode = newMode;
            OnModeChanged?.Invoke(newMode.ToString());
            Debug.Log($"[DualManager] Mode: {newMode}");
        }

        public void SetRecordMode(bool active)
        {
            isRecordModeActive = active;
            OnModeChanged?.Invoke($"{controlMode} / Record:{(active ? "ON" : "OFF")}");
            Debug.Log($"[DualManager] Record: {active}");
        }

        // ── 수동(Teach) 모드 ─────────────────────────────────────
        // 손으로 팔을 밀어 자세를 잡는 모드. 로봇별로 따로 켠다.
        //
        // ⚠️ 미러 모드에서도 "양쪽 다 수동" 은 만들지 않는다.
        //    직접교시는 한 팔을 손으로 잡고 가르치는 작업이고,
        //    양쪽을 동시에 손으로 밀 수는 없다.
        //    미러 수동은 "로봇1을 손으로 가르치면 로봇2가 따라 움직이는" 것이 맞다
        //    → TeachMirror. 이때 로봇2는 토크를 유지해야 명령을 따를 수 있다.
        public bool robot1Teach { get; private set; }
        public bool robot2Teach { get; private set; }

        /// <summary>미러 수동: 로봇1을 손으로 가르치면 로봇2가 실시간으로 따라 한다.</summary>
        public bool teachMirror { get; private set; }

        public void SetTeach(bool forRobot1, bool enable)
        {
            if (forRobot1)
            {
                robot1Teach = enable;
                robot1?.SetTeachMode(enable);
            }
            else
            {
                robot2Teach = enable;
                robot2?.SetTeachMode(enable);
            }

            // 개별 수동을 끄면 미러 수동도 성립하지 않는다.
            if (!enable && teachMirror && forRobot1) SetTeachMirror(false);

            OnModeChanged?.Invoke(DescribeMode());
            Debug.Log($"[DualManager] 수동모드 {(forRobot1 ? "로봇1" : "로봇2")}: {enable}");
        }

        /// <summary>
        /// 미러 수동 — 로봇1만 토크를 풀고, 로봇1의 실물 각도를 로봇2로 흘려보낸다.
        /// 로봇2는 토크를 유지해야 따라올 수 있으므로 수동으로 만들지 않는다.
        /// </summary>
        public void SetTeachMirror(bool enable)
        {
            teachMirror = enable;

            if (enable)
            {
                // 로봇1은 손으로 미는 쪽 → 수동
                if (!robot1Teach) SetTeach(true, true);
                // 로봇2는 따라오는 쪽 → 수동이면 안 된다
                if (robot2Teach) SetTeach(false, false);

                if (robot1?.real != null) robot1.real.OnAnglesReceived += MirrorToRobot2;
            }
            else
            {
                if (robot1?.real != null) robot1.real.OnAnglesReceived -= MirrorToRobot2;
            }

            OnModeChanged?.Invoke(DescribeMode());
            Debug.Log($"[DualManager] 미러 수동: {enable}");
        }

        void MirrorToRobot2(System.Collections.Generic.Dictionary<string, float> angles)
        {
            if (!teachMirror || EmergencyStopped) return;
            if (!Robot2Active || robot2 == null) return;

            int n = robot2.JointCount;
            float[] target = new float[n];
            for (int i = 0; i < n; i++)
            {
                string mname = robot2.GetJointName(i);
                target[i] = angles.TryGetValue(mname, out float d) ? d : robot2.GetJointAngle(i);
            }
            robot2.SetAllJointTargets(target);
        }

        void OnDisable()
        {
            // 이벤트 구독을 남겨두면 씬 전환/재컴파일 후 죽은 참조로 호출된다.
            if (teachMirror && robot1?.real != null)
                robot1.real.OnAnglesReceived -= MirrorToRobot2;
        }

        string DescribeMode()
        {
            string t = teachMirror ? " / 미러수동"
                     : (robot1Teach || robot2Teach)
                       ? $" / 수동:{(robot1Teach ? "R1" : "")}{(robot2Teach ? "R2" : "")}"
                       : "";
            return controlMode + t + (isRecordModeActive ? " / REC" : "");
        }

        // ── 한 번에 두 로봇 제어 (Mirror용) ────────────────────
        // 꺼진 채널에는 명령을 보내지 않음.
        public void SetJointBoth(int jointIndex, float angleDeg)
        {
            if (Robot1Active) robot1?.SetJointTarget(jointIndex, angleDeg);
            if (Robot2Active) robot2?.SetJointTarget(jointIndex, angleDeg);
        }

        public void SetGripperBoth(float percent)
        {
            if (Robot1Active) robot1?.SetGripperTarget(percent);
            if (Robot2Active) robot2?.SetGripperTarget(percent);
        }

        public void GoToHomeAll()
        {
            if (Robot1Active) robot1?.GoToHome();
            if (Robot2Active) robot2?.GoToHome();
        }

        /// <summary>비상정지 상태. 걸린 동안 UI 슬라이더도 무시된다.</summary>
        public bool EmergencyStopped { get; private set; }

        /// <summary>
        /// 🛑 전체 비상정지.
        ///
        /// 채널 활성화(robot1/2Enabled)와 무관하게 **항상 두 로봇 모두** 정지시킨다.
        /// 꺼둔 채널이라고 안 세우면, 그 로봇이 이미 움직이는 중일 때 못 멈춘다.
        ///
        /// ⚠️ 토크는 끄지 않는다 — 12V 팔은 토크를 끄면 중력으로 떨어진다.
        ///    "그 자리에 세우기" 가 이 시스템에서의 안전한 정지다.
        /// </summary>
        public void StopAll()
        {
            EmergencyStopped = true;
            robot1?.StopMotion();
            robot2?.StopMotion();
            OnModeChanged?.Invoke("EMERGENCY STOP");
            Debug.LogWarning("[DualManager] 🛑 비상정지 — 두 로봇 모두 현재 자세로 고정");
        }

        /// <summary>▶ 비상정지 해제. 현재 자세부터 재개하므로 튀지 않는다.</summary>
        public void ReleaseStopAll()
        {
            if (!EmergencyStopped) return;
            EmergencyStopped = false;
            robot1?.ReleaseStop();
            robot2?.ReleaseStop();
            OnModeChanged?.Invoke(controlMode.ToString());
            Debug.Log("[DualManager] ▶ 비상정지 해제");
        }

        void Update()
        {
            // ESC = 비상정지. 마우스로 버튼을 찾는 것보다 빠르다.
            // (안전 규칙에 명시된 요구사항)
            if (Input.GetKeyDown(KeyCode.Escape) && !EmergencyStopped)
                StopAll();
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
            if (EmergencyStopped) return;   // 🛑 정지 중에는 어떤 조작도 받지 않는다

            switch (controlMode)
            {
                case ControlMode.Independent:
                    if (fromRobot1UI) { if (Robot1Active) robot1?.SetJointTarget(jointIndex, angleDeg); }
                    else              { if (Robot2Active) robot2?.SetJointTarget(jointIndex, angleDeg); }
                    break;
                case ControlMode.Mirror:
                    SetJointBoth(jointIndex, angleDeg);
                    break;
            }
        }

        public void RouteGripperCommand(bool fromRobot1UI, float percent)
        {
            if (EmergencyStopped) return;   // 🛑 정지 중에는 어떤 조작도 받지 않는다

            switch (controlMode)
            {
                case ControlMode.Independent:
                    if (fromRobot1UI) { if (Robot1Active) robot1?.SetGripperTarget(percent); }
                    else              { if (Robot2Active) robot2?.SetGripperTarget(percent); }
                    break;
                case ControlMode.Mirror:
                    SetGripperBoth(percent);
                    break;
            }
        }
    }
}
