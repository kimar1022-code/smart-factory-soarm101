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

        [Header("경고 / 안전")]
        [Tooltip("한 번의 조작으로 관절이 이만큼 넘게 돌면 경고를 띄운다.\n" +
                 "홈 자세가 리치의 96% 라 뻗은 쪽에서는 5mm 에도 크게 돈다.")]
        public float bigJointStepDeg = 10f;

        [Tooltip("관절이 이만큼 넘게 돌아야 하는 해는 **적용하지 않는다**.\n\n" +
                 "작업영역 경계에서 솔버가 팔꿈치를 리밋까지 꺾는 다른 자세로\n" +
                 "뒤집히는 일이 있다. 실측: Z 를 5mm 씩 밀다가 16번째에\n" +
                 "J2 가 +21.6°, J3 가 -40.5° 한 번에 튀었다(J3 는 리밋에 박힘).\n" +
                 "그대로 넣으면 팔이 주저앉는다.")]
        public float maxJointStepDeg = 20f;

        [Header("상태 (읽기 전용)")]
        [SerializeField] private Vector3 currentTcp;      // 현재 TCP 위치 (m)
        [SerializeField] private Vector3 targetTcp;       // 목표 TCP 위치 (m)
        [SerializeField] private Vector3 currentRpy;      // 현재 TCP 자세 (roll/pitch/yaw, deg)
        [SerializeField] private string statusMessage = "대기";
        [SerializeField] private bool lastConverged = true;
        [SerializeField] private float lastErrorMm = 0f;

        public Vector3 CurrentTcp => currentTcp;
        public Vector3 TargetTcp => targetTcp;
        public Vector3 CurrentRpy => currentRpy;
        public string StatusMessage => statusMessage;
        public bool LastConverged => lastConverged;
        public float LastErrorMm => lastErrorMm;
        public bool HasTarget { get; private set; }

        /// <summary>
        /// 응답을 기다리는 중. 이때 목표를 또 밀면 안 된다.
        ///
        /// 버튼을 연타하면 NudgeTarget 은 매번 목표를 밀어 놓는데 SolveAndApply 는
        /// inFlight 라서 그냥 돌아가 버린다. 그러다 한 번 통과하는 순간 그동안
        /// 쌓인 거리를 한 번에 간다 — 5mm 씩 다섯 번 누른 게 25mm 도약이 된다.
        /// 부르는 쪽이 이걸 보고 아예 밀지 말아야 한다.
        /// </summary>
        public bool IsBusy => inFlight;

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
            public float[] reached_rpy;
            public float error_mm;
            public float rot_error_deg;
            public float max_joint_step_deg;
            public int iters;
            public bool converged;
            public string blocked_by;
        }

        [Serializable]
        class FkResponse
        {
            public bool ok;
            public string type;
            public string error;
            public float[] position;
            public float[] rpy;
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
            // ⚠️ 끝에 \n 이 반드시 있어야 한다. 서버는 buffer 에 모아두고 '\n' 로 자른다
            //    (robot_server_dual.py 의 while '\n' in buffer). 개행이 없으면 요청이
            //    서버 버퍼에 갇혀 영영 처리되지 않는다. SendRaw 는 개행을 안 붙인다 —
            //    붙이는 건 부르는 쪽 책임이다 (다른 호출부도 전부 그렇게 한다).
            string json = "{\"type\":\"fk\",\"joints\":[" + JoinF(q) + "]}\n";

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
                if (r.rpy != null && r.rpy.Length >= 3)
                    currentRpy = new Vector3(r.rpy[0], r.rpy[1], r.rpy[2]);
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
                "\"orientation_weight\":" + F(orientationWeight) + "}\n";   // \n 필수 — 위 fk 주석 참고

            SendIk(json, "이동");
        }

        /// <summary>
        /// ik 요청을 보내고 결과를 관절에 적용한다. 이동·회전이 같은 경로를 쓴다.
        ///
        /// 적용은 반드시 SOArmManager 를 거친다. 컨트롤러를 직접 부르면
        /// 속도 제한·소프트 리밋·비상정지를 건너뛴다.
        /// </summary>
        void SendIk(string json, string what, bool snapTargetToReached = false)
        {
            inFlight = true;
            socketClient.SendRaw(json, resp =>
            {
                inFlight = false;

                var r = SafeParse<IkResponse>(resp);
                if (r == null || !r.ok)
                {
                    // 서버가 거절한 경우(회전으로 팔이 밀려남 등)도 여기로 온다.
                    // 사유가 그대로 오므로 감추지 않고 보여 준다.
                    statusMessage = $"{what} 불가: " + (r?.error ?? "응답 없음");
                    lastConverged = false;
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

                // ⚠️ 관절이 통째로 뒤집히는 해는 **넣지 않는다**.
                //    작업영역 경계에서 솔버가 다른 자세로 건너뛰면, 몇 mm 를 요청한
                //    한 번의 조작에 팔이 수십 도 돈다. 실측으로 J3 가 리밋(-96.8°)까지
                //    꺾이고 J2 가 21.6° 튀었다 — 실제 팔에서는 주저앉는 것으로 보인다.
                //    목표도 되돌려서, 다음에 눌러도 같은 자리로 다시 달려가지 않게 한다.
                if (r.max_joint_step_deg > maxJointStepDeg)
                {
                    targetTcp = currentTcp;
                    lastConverged = false;
                    statusMessage = $"⛔ {ActiveRobotLabel} 여기서 막힌다 — 더 가려면 관절이 " +
                                    $"{r.max_joint_step_deg:F0}° 튄다. 적용하지 않았다";
                    return;
                }

                lastConverged = r.converged;
                lastErrorMm = r.error_mm;
                if (r.reached != null && r.reached.Length >= 3)
                {
                    currentTcp = new Vector3(r.reached[0], r.reached[1], r.reached[2]);

                    // 목표가 실제 도달점을 앞질러 가지 못하게 한다.
                    //
                    // 이게 없으면 팔이 못 닿는데도 누를 때마다 목표가 5mm 씩 계속
                    // 나간다. 목표와 실제가 벌어질수록 솔버가 무리해서 풀다가
                    // 결국 자세가 뒤집힌다(실측: 16번째 누름에서 J3 가 리밋까지).
                    // 못 닿았으면 목표를 실제 자리로 되돌려, 다음 누름이 다시
                    // "지금 자리에서 5mm" 가 되게 한다.
                    if (snapTargetToReached || !r.converged) targetTcp = currentTcp;
                }
                if (r.reached_rpy != null && r.reached_rpy.Length >= 3)
                    currentRpy = new Vector3(r.reached_rpy[0], r.reached_rpy[1], r.reached_rpy[2]);

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

                // 관절이 크게 돌면 그걸 먼저 알린다. 팔이 다 뻗은 자세에서는
                // 몇 mm 를 옮기는 데도 관절이 수십 도 돈다 — 눈에는 "확 튀는" 것으로
                // 보이는데, 값을 안 보여 주면 고장으로 오해한다.
                if (r.max_joint_step_deg >= bigJointStepDeg)
                    statusMessage = $"⚠ {ActiveRobotLabel} 관절이 {r.max_joint_step_deg:F0}° 돌았다 " +
                                    $"— 팔이 뻗은 자세라 조금만 움직여도 크게 돈다";
                else
                    statusMessage = r.converged
                        ? $"{ActiveRobotLabel} {what} 완료 (오차 {r.error_mm:F2}mm, {r.iters}회)"
                        : $"⚠ {ActiveRobotLabel} {what} 도달 불가 — {r.error_mm:F0}mm 벗어남";
            });
        }

        // ════════════════════════════════════════════════════
        //  회전 조그 (Rx / Ry / Rz)
        // ════════════════════════════════════════════════════

        /// <summary>
        /// 공구를 축 하나 기준으로 조금 돌린다. 위치는 지금 자리를 지킨다.
        ///
        /// ■ 왜 목표를 currentTcp 로 보내나
        ///   사용자가 누른 건 "돌려라" 지 "움직여라" 가 아니다. 카티시안 목표가
        ///   실제 위치와 벌어져 있을 수 있는데(도달 불가로 목표만 튀어나간 경우)
        ///   그걸 그대로 보내면 회전 명령 한 번에 팔이 그리로 달려간다.
        ///
        /// ■ 안 도는 축이 있다
        ///   5축이라 Rx(팔이 놓인 평면을 도는 축)는 J1 에 묶여 있다. 그걸 돌리려면
        ///   팔 전체가 돌아 TCP 가 수십 mm 밀려난다. 서버가 그걸 재서 한계를 넘으면
        ///   ok:false 로 **거절**한다 — 여기서는 그 사유를 그대로 보여 주기만 한다.
        ///   (실측: dRz 5° → 0.6mm, dRy 5° → 1.2mm, dRx 5° → 25mm)
        /// </summary>
        public void JogRotation(int axis, float deltaDeg)
        {
            if (Blocked)
            {
                statusMessage = "🛑 비상정지 중";
                return;
            }
            if (socketClient == null || !socketClient.IsConnected)
            {
                statusMessage = "서버 미연결";
                return;
            }
            if (!HasTarget)
            {
                statusMessage = "먼저 현재 위치를 읽으세요";
                return;
            }
            if (inFlight) return;

            float dx = axis == 0 ? deltaDeg : 0f;
            float dy = axis == 1 ? deltaDeg : 0f;
            float dz = axis == 2 ? deltaDeg : 0f;

            float[] q = GetArmAngles();
            string json =
                "{\"type\":\"ik\",\"current\":[" + JoinF(q) + "]," +
                "\"target\":[" + F(currentTcp.x) + "," + F(currentTcp.y) + "," + F(currentTcp.z) + "]," +
                "\"rot_delta\":[" + F(dx) + "," + F(dy) + "," + F(dz) + "]}\n";

            SendIk(json, "회전", snapTargetToReached: true);
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
