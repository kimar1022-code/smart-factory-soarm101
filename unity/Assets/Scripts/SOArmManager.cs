using System;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 한 대의 SO-ARM 로봇 통합 관리.
    /// Sim 단독 / Real 단독 / Mirror (둘 다) 모드.
    /// 
    /// 두 대 동시 관리는 SOArmDualManager 사용.
    /// </summary>
    public class SOArmManager : MonoBehaviour, ISOArmController
    {
        public enum Mode { SimOnly, RealOnly, Mirror }

        [Header("동작 모드")]
        public Mode mode = Mode.SimOnly;

        [Header("자동 연결 (Real)")]
        [Tooltip("체크하면 시작 시 실로봇도 자동 연결")]
        public bool autoConnectReal = false;

        [Header("컨트롤러 참조")]
        public SOArmSimController sim;
        public SOArmRealController real;

        public event Action<string> OnStatusChanged;

        // ── 어느 컨트롤러를 읽을지 ────────────────────────────
        ISOArmController PrimaryReader
        {
            get
            {
                if (mode == Mode.RealOnly) return real;
                if (mode == Mode.Mirror && real != null && real.IsConnected) return real;
                return sim;
            }
        }

        bool SimActive => mode != Mode.RealOnly && sim != null;
        bool RealActive => mode != Mode.SimOnly && real != null;

        void Start()
        {
            if (SimActive) sim.Connect();
            if (autoConnectReal && RealActive) real.Connect();
        }

        public void ChangeMode(Mode newMode)
        {
            mode = newMode;
            SyncTargets();
            OnStatusChanged?.Invoke($"Mode: {newMode}");
        }

        /// <summary>현재 PrimaryReader의 각도를 다른 컨트롤러에도 적용.</summary>
        public void SyncTargets()
        {
            if (PrimaryReader == null) return;
            float[] cur = new float[PrimaryReader.JointCount];
            for (int i = 0; i < cur.Length; i++) cur[i] = PrimaryReader.GetJointAngle(i);
            if (SimActive) sim.SetAllJointTargets(cur);
            if (RealActive) real.SetAllJointTargets(cur);
        }

        // ── ISOArmController ────────────────────────────────────
        public bool IsConnected
        {
            get
            {
                if (mode == Mode.SimOnly) return sim?.IsConnected ?? false;
                if (mode == Mode.RealOnly) return real?.IsConnected ?? false;
                return (sim?.IsConnected ?? false) && (real?.IsConnected ?? false);
            }
        }

        public string StatusMessage
        {
            get
            {
                string s = sim != null ? $"Sim:{sim.StatusMessage}" : "Sim:null";
                string r = real != null ? $"Real:{real.StatusMessage}" : "Real:null";
                return mode switch
                {
                    Mode.SimOnly => s,
                    Mode.RealOnly => r,
                    _ => $"{s} | {r}"
                };
            }
        }

        public void Connect()
        {
            if (SimActive) sim.Connect();
            if (RealActive) real.Connect();
        }

        public void Disconnect()
        {
            sim?.Disconnect();
            real?.Disconnect();
        }

        public int JointCount => PrimaryReader?.JointCount ?? 6;
        public string GetJointName(int i) => PrimaryReader.GetJointName(i);
        public float GetJointMinAngle(int i) => PrimaryReader.GetJointMinAngle(i);
        public float GetJointMaxAngle(int i) => PrimaryReader.GetJointMaxAngle(i);
        public float GetJointAngle(int i) => PrimaryReader.GetJointAngle(i);

        public void SetJointTarget(int i, float angleDeg)
        {
            if (SimActive) sim.SetJointTarget(i, angleDeg);
            if (RealActive) real.SetJointTarget(i, angleDeg);
        }

        public void SetAllJointTargets(float[] angles)
        {
            if (SimActive) sim.SetAllJointTargets(angles);
            if (RealActive) real.SetAllJointTargets(angles);
        }

        public float GetGripperPercent() => PrimaryReader?.GetGripperPercent() ?? 50f;

        public void SetGripperTarget(float percent)
        {
            if (SimActive) sim.SetGripperTarget(percent);
            if (RealActive) real.SetGripperTarget(percent);
        }

        public void StopMotion()
        {
            sim?.StopMotion();
            real?.StopMotion();
        }

        public void GoToHome()
        {
            if (SimActive) sim.GoToHome();
            if (RealActive) real.GoToHome();
        }

        public void SetHomeFromCurrent()
        {
            if (SimActive) sim.SetHomeFromCurrent();
            if (RealActive) real.SetHomeFromCurrent();
        }

        public float[] GetHomePose() => PrimaryReader?.GetHomePose() ?? new float[6];

        public void SetHomePose(float[] angles)
        {
            if (SimActive) sim.SetHomePose(angles);
            if (RealActive) real.SetHomePose(angles);
        }
    }
}
