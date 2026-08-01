using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 한 대의 SO-ARM 로봇 통합 관리. (v3)
    /// Sim 단독 / Real 단독 / Mirror.
    ///
    /// v3 변경:
    ///  - real.OnAnglesReceived 구독 → sim에 자동 반영 (실로봇 → 시뮬)
    ///  - Play 시작 시 초기 동기화 (실로봇 자세를 시뮬에 맞춤)
    /// </summary>
    public class SOArmManager : MonoBehaviour, ISOArmController
    {
        public enum Mode { SimOnly, RealOnly, Mirror }

        [Header("동작 모드")]
        public Mode mode = Mode.SimOnly;

        [Header("자동 연결 (Real)")]
        public bool autoConnectReal = false;

        [Header("컨트롤러 참조")]
        public SOArmSimController sim;
        public SOArmRealController real;

        [Header("v3 양방향 옵션")]
        [Tooltip("실로봇에서 받은 각도를 시뮬에 자동 반영")]
        public bool realToSimSync = true;

        [Tooltip("Play 시작 후 일정 시간 뒤 실로봇 각도를 1회 읽어 시뮬 초기 자세를 맞춤")]
        public bool syncOnStart = true;

        [Tooltip("연결 직후 첫 각도 요청까지의 대기(초).\n" +
                 "폴링은 어차피 바로 시작되므로 길게 둘 이유가 없다. 길수록 시뮬이 홈 자세로 보이는 시간이 늘어난다.")]
        public float startSyncDelay = 0.2f;

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

            // v3: 실로봇 각도 수신 시 시뮬에 자동 반영
            if (real != null)
            {
                real.OnAnglesReceived += HandleRealAngles;
            }

            // v3: 초기 동기화
            if (syncOnStart && RealActive)
                StartCoroutine(InitialSyncCoroutine());
        }

        void OnDestroy()
        {
            if (real != null)
                real.OnAnglesReceived -= HandleRealAngles;
        }

        private IEnumerator InitialSyncCoroutine()
        {
            // 소켓 연결 + 폴링 활성화까지 잠시 대기
            yield return new WaitForSeconds(startSyncDelay);

            if (real != null && real.IsConnected)
            {
                Debug.Log($"[Manager] 초기 동기화: 실로봇 자세 1회 읽기");
                real.RequestAnglesOnce();
            }
            else
            {
                Debug.Log($"[Manager] 실로봇 미연결, 초기 동기화 스킵");
            }
        }

        /// <summary>
        /// 실로봇이 각도를 보내올 때마다 호출.
        /// Mirror 모드에서만 시뮬에 반영 (SimOnly는 무관, RealOnly는 시뮬 없음).
        /// </summary>
        /// <summary>첫 실물 수신 여부. 첫 회만 순간이동으로 맞추기 위해 쓴다.</summary>
        private bool firstSimSyncDone = false;

        private void HandleRealAngles(Dictionary<string, float> angles)
        {
            if (!realToSimSync) return;
            if (sim == null || sim.joints == null) return;
            if (mode == Mode.SimOnly) return; // 시뮬만 쓰는 모드는 무시

            // joints의 motorName 순서대로 매핑하여 sim에 적용
            float[] target = new float[sim.JointCount];
            for (int i = 0; i < sim.joints.Length; i++)
            {
                string mname = sim.joints[i].motorName;
                if (angles.TryGetValue(mname, out float deg))
                    target[i] = deg;
                else
                    target[i] = sim.GetJointAngle(i); // 못 받은 건 유지
            }

            if (!firstSimSyncDone)
            {
                // Play 직후 첫 수신: 홈 자세에서 서서히 움직여 가면 팔이 한 번 쓸고 지나간다.
                // 디지털 트윈은 처음부터 실물 자세로 서 있어야 하므로 순간이동시킨다.
                firstSimSyncDone = true;
                sim.SnapAllJointTargets(target);
            }
            else
            {
                sim.SetAllJointTargets(target);
            }
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

        // v3 신규: 홈포즈 저장 (현재 자세를 새 0점으로)
        public void SaveHomePose(Action<bool> done = null)
        {
            if (real == null || !real.IsConnected)
            {
                Debug.LogWarning("[Manager] 실로봇 미연결 — 홈포즈 저장 스킵");
                done?.Invoke(false);
                return;
            }
            real.SaveHomePose((ok) =>
            {
                if (ok && sim != null)
                {
                    // 시뮬도 홈포즈 갱신: 현재 시뮬 위치를 0으로 만들기보다는,
                    // 다음 폴링에서 새 0점이 들어오면 sim에 자동 반영됨.
                    // 여기선 시뮬 목표만 0으로 리셋.
                    float[] zero = new float[sim.JointCount];
                    sim.SetAllJointTargets(zero);
                    sim.SetHomeFromCurrent();
                }
                done?.Invoke(ok);
            });
        }

        /// <summary>
        /// 수동(Teach) 모드 — 손으로 밀어 자세를 잡는다.
        /// 실물 토크 해제와 유니티 목표 추종을 함께 처리한다 (real 쪽에 구현).
        /// </summary>
        public void SetTeachMode(bool enable, Action<bool> done = null)
        {
            if (real == null)
            {
                Debug.LogWarning("[Manager] real 컨트롤러 없음 — 수동모드 스킵");
                done?.Invoke(false);
                return;
            }
            real.SetTeachMode(enable, done);
        }

        /// <summary>현재 수동모드인지.</summary>
        public bool IsTeachMode => real != null && real.teachMode;

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

        /// <summary>🛑 비상정지 — sim/real 모두 현재 자세에 고정.</summary>
        public void StopMotion()
        {
            sim?.StopMotion();
            real?.StopMotion();
        }

        /// <summary>▶ 비상정지 해제 — 현재 자세부터 재개.</summary>
        public void ReleaseStop()
        {
            sim?.ReleaseStop();
            real?.ReleaseStop();
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
