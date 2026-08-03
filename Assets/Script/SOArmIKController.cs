using System;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 카티시안(XYZ) 제어. 역기구학은 라즈베리파이의 placo 가 푼다.
    ///
    /// ■ 왜 서버에서 푸는가
    ///   LeRobot 본체(`lerobot/model/kinematics.py`)에 이 로봇용 IK 가 이미 있다.
    ///   URDF 를 그대로 읽고, 5축이라 임의 자세를 못 만드는 문제를 자세 가중치
    ///   0.01 로 처리한다. 라파4에서 1회 0.11ms, TCP 왕복 포함 1.9ms 라
    ///   슬라이더를 끌어도 충분히 따라온다.
    ///
    /// ■ 왜 서버가 모터를 직접 안 돌리는가
    ///   서버의 ik 명령은 **계산만** 한다. 나온 각도를 실제로 적용하는 건 이 클래스가
    ///   SOArmManager 를 거쳐서 한다. 그래야 속도 제한 · 소프트 리밋 · 비상정지 ·
    ///   그리퍼 안전 게이트가 전부 그대로 걸린다. 서버가 직접 돌리면 그 방어선을
    ///   통째로 우회한다. (SOArmMotionRecorder 가 재생에서 쓰는 것과 같은 원칙)
    ///
    /// ■ 단위
    ///   서버와 주고받는 값은 **degree / meter** 다. 정규화값(-100~100)이 아니다.
    ///   정규화 변환은 SOArmMotorMapper 가 이미 하므로 두 번 하지 않는다.
    ///
    /// ■ 축이 5개인 것의 의미
    ///   팔 관절은 J1~J5 뿐이고 J6 은 그리퍼다. J2·J3·J4 가 서로 평행한 pitch 축이라
    ///   공구의 yaw 는 J1(팔이 놓인 평면)에 묶인다. 그래서 "위치는 맞추되 자세는
    ///   근사" 가 이 로봇에서 가능한 최선이다. 서버가 그렇게 풀어서 돌려준다.
    /// </summary>
    public class SOArmIKController : MonoBehaviour
    {
        [Header("연결")]
        public SOArmDualManager dualManager;

        [Tooltip("비우면 씬에서 찾는다. 두 로봇이 소켓 하나를 공유하는 구성이면 그대로 둘 것.")]
        public SOArmSocketClient socketClient;

        [Header("대상 로봇")]
        [Tooltip("false = 로봇1, true = 로봇2. UI 의 전환 버튼이 이 값을 바꾼다.")]
        public bool useRobot2 = false;

        [Header("자세 가중치")]
        [Tooltip("0 이면 자세를 아예 안 본다(위치만). 0.01 이 LeRobot 기본값.\n" +
                 "5축이라 위치와 자세를 동시에 다 맞출 수는 없다.")]
        [Range(0f, 1f)]
        public float orientationWeight = 0.01f;

        [Header("상태 (읽기 전용)")]
        [SerializeField] private Vector3 currentTcp;      // 현재 TCP 위치 (m)
        [SerializeField] private Vector3 targetTcp;       // 목표 TCP 위치 (m)
        [SerializeField] private string statusMessage = "대기";
        [SerializeField] private bool lastConverged = true;
        [SerializeField] private float lastErrorMm = 0f;

        public Vector3 CurrentTcp => currentTcp;
        public Vector3 TargetTcp => targetTcp;
        public string StatusMessage => statusMessage;
        public bool LastConverged => lastConverged;
        public float LastErrorMm => lastErrorMm;
        public bool HasTarget { get; private set; }

        /// <summary>지금 조작 대상인 로봇.</summary>
        public SOArmManager ActiveRobot =>
            dualManager == null ? null : (useRobot2 ? dualManager.robot2 : dualManager.robot1);

        public string ActiveRobotLabel => useRobot2 ? "R2" : "R1";

        /// <summary>비상정지 중에는 어떤 IK 조작도 받지 않는다.</summary>
        public bool Blocked => dualManager == null || dualManager.EmergencyStopped;

        // 요청이 겹치지 않게 한 번에 하나만. 슬라이더를 빠르게 끌면 요청이 쌓인다.
        bool inFlight = false;

        // ── 서버 응답 ─────────────────────────────────────────
        // JsonUtility 는 필드명이 정확히 같아야 채운다.
        //
        // CS0649("할당된 적 없음")를 끈다 — 이 필드들은 C# 코드가 아니라
        // JsonUtility 가 리플렉션으로 채운다. 컴파일러는 그걸 못 본다.
#pragma warning disable 0649
        [Serializable]
        class IkResponse
        {
            public bool ok;
            public string type;
            public string error;
            public float[] joints;
            public float[] reached;
            public float error_mm;
            public int iters;
            public bool converged;
        }

        [Serializable]
        class FkResponse
        {
            public bool ok;
            public string type;
            public string error;
            public float[] position;
        }
#pragma warning restore 0649

        void Start()
        {
            if (dualManager == null) dualManager = FindAnyObjectByType<SOArmDualManager>();
            if (socketClient == null) socketClient = FindAnyObjectByType<SOArmSocketClient>();
        }

        // ════════════════════════════════════════════════════
        //  현재 자세 읽기
        // ════════════════════════════════════════════════════

        /// <summary>현재 팔 5축 각도(deg). 그리퍼는 IK 대상이 아니라 뺀다.</summary>
        public float[] GetArmAngles()
        {
            var robot = ActiveRobot;
            var q = new float[5];
            if (robot == null) return q;

            for (int i = 0; i < 5; i++)
            {
                try { q[i] = robot.GetJointAngle(i); }
                catch { q[i] = 0f; }
            }
            return q;
        }

        /// <summary>현재 관절 각도로 TCP 위치를 물어본다(FK). 목표를 처음 잡을 때 쓴다.</summary>
        public void RefreshCurrentTcp(Action<bool> done = null)
        {
            if (socketClient == null || !socketClient.IsConnected)
            {
                statusMessage = "서버 미연결";
                done?.Invoke(false);
                return;
            }

            float[] q = GetArmAngles();
            string json = "{\"type\":\"fk\",\"joints\":[" + JoinF(q) + "]}";

            socketClient.SendRaw(json, resp =>
            {
                var r = SafeParse<FkResponse>(resp);
                if (r == null || !r.ok || r.position == null || r.position.Length < 3)
                {
                    statusMessage = "FK 실패: " + (r?.error ?? "응답 없음");
                    done?.Invoke(false);
                    return;
                }

                currentTcp = new Vector3(r.position[0], r.position[1], r.position[2]);
                if (!HasTarget)
                {
                    targetTcp = currentTcp;   // 목표를 현재 위치에서 시작해야 팔이 안 튄다
                    HasTarget = true;
                }
                statusMessage = "준비됨";
                done?.Invoke(true);
            });
        }

        // ════════════════════════════════════════════════════
        //  목표 조작
        // ════════════════════════════════════════════════════

        /// <summary>목표를 절대 좌표로 지정.</summary>
        public void SetTarget(Vector3 posMeters)
        {
            targetTcp = posMeters;
            HasTarget = true;
        }

        /// <summary>목표를 상대로 이동. UI 의 +/- 버튼이 이걸 부른다. (단위 m)</summary>
        public void NudgeTarget(int axis, float deltaMeters)
        {
            if (!HasTarget) return;
            switch (axis)
            {
                case 0: targetTcp.x += deltaMeters; break;
                case 1: targetTcp.y += deltaMeters; break;
                case 2: targetTcp.z += deltaMeters; break;
            }
        }

        // ════════════════════════════════════════════════════
        //  IK 요청 → 적용
        // ════════════════════════════════════════════════════

        /// <summary>
        /// 현재 목표로 IK 를 풀어 관절에 적용한다.
        ///
        /// 적용은 반드시 SOArmManager 를 거친다. 컨트롤러를 직접 부르면
        /// 속도 제한·소프트 리밋·비상정지를 건너뛴다.
        /// </summary>
        public void SolveAndApply()
        {
            if (Blocked)
            {
                statusMessage = "🛑 비상정지 중";
                return;
            }
            if (!HasTarget)
            {
                statusMessage = "목표 없음 — 먼저 현재 위치를 읽으세요";
                return;
            }
            if (socketClient == null || !socketClient.IsConnected)
            {
                statusMessage = "서버 미연결";
                return;
            }
            // 응답을 기다리는 중이면 새 요청을 안 보낸다.
            // 슬라이더를 끌면 프레임마다 불리는데, 그대로 두면 요청이 쌓여
            // 콜백 순서가 어긋난다 (SOArmSocketClient 는 FIFO 매칭이다).
            if (inFlight) return;

            float[] q = GetArmAngles();
            string json =
                "{\"type\":\"ik\",\"current\":[" + JoinF(q) + "]," +
                "\"target\":[" + F(targetTcp.x) + "," + F(targetTcp.y) + "," + F(targetTcp.z) + "]," +
                "\"orientation_weight\":" + F(orientationWeight) + "}";

            inFlight = true;
            socketClient.SendRaw(json, resp =>
            {
                inFlight = false;

                var r = SafeParse<IkResponse>(resp);
                if (r == null || !r.ok)
                {
                    statusMessage = "IK 실패: " + (r?.error ?? "응답 없음");
                    return;
                }
                if (r.joints == null || r.joints.Length < 5)
                {
                    statusMessage = "IK 응답이 이상함";
                    return;
                }

                // 응답이 오는 사이에 비상정지가 걸렸을 수 있다. 다시 본다.
                if (Blocked)
                {
                    statusMessage = "🛑 비상정지 중 — 결과 버림";
                    return;
                }

                lastConverged = r.converged;
                lastErrorMm = r.error_mm;
                if (r.reached != null && r.reached.Length >= 3)
                    currentTcp = new Vector3(r.reached[0], r.reached[1], r.reached[2]);

                var robot = ActiveRobot;
                if (robot == null)
                {
                    statusMessage = ActiveRobotLabel + " 매니저 없음";
                    return;
                }

                // 팔 5축만 건드린다. 그리퍼(인덱스 5)는 손대지 않는다 —
                // IK 대상이 아니고, 안전 게이트가 따로 있다.
                for (int i = 0; i < 5; i++)
                    robot.SetJointTarget(i, r.joints[i]);

                statusMessage = r.converged
                    ? $"{ActiveRobotLabel} 도달 (오차 {r.error_mm:F2}mm, {r.iters}회)"
                    : $"⚠ {ActiveRobotLabel} 도달 불가 — {r.error_mm:F0}mm 벗어남";
            });
        }

        /// <summary>목표를 현재 실제 위치로 되돌린다. 도달 불가로 목표가 튀어나갔을 때.</summary>
        public void SnapTargetToCurrent()
        {
            targetTcp = currentTcp;
            HasTarget = true;
            statusMessage = "목표를 현재 위치로 맞춤";
        }

        // ── 도우미 ────────────────────────────────────────────

        // ⚠️ 반드시 InvariantCulture. 한국어 로케일에서 소수점이 쉼표로 나가면
        //    서버의 JSON 파서가 통째로 실패한다. 예전에 value:F2 로 겪은 것과 같은 부류.
        static string F(float v) => v.ToString("F5", System.Globalization.CultureInfo.InvariantCulture);

        static string JoinF(float[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(F(a[i]));
            }
            return sb.ToString();
        }

        static T SafeParse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }
    }
}
