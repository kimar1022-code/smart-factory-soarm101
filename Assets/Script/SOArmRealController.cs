using System;
using System.Collections.Generic;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 실제 SO-ARM101 로봇 컨트롤러. (v3 양방향)
    ///
    /// 기존: 슬라이더 → 서버에 -100~100 정규화 값 전송 (그대로 유지)
    /// 신규:
    ///   - 30Hz로 실로봇 각도 폴링
    ///   - 받은 정규화 값을 각도로 변환해 OnAnglesReceived 이벤트로 발행
    ///   - SaveHomePose() : 현재 자세를 새 0점으로
    ///   - SetServoTorque(): 토크 ON/OFF (필요 시)
    /// </summary>
    public class SOArmRealController : MonoBehaviour, ISOArmController
    {
        [Header("소켓 클라이언트")]
        [Tooltip("비워두면 자동 탐색")]
        public SOArmSocketClient socketClient;

        [Header("서버 모드")]
        [Tooltip("robot1 / robot2 / mirror")]
        public string robotServerMode = "robot1";

        [Header("관절 설정 (6개)")]
        public SOArmJointConfig[] joints;

        [Header("쓰기 (슬라이더 → 실로봇)")]
        [Range(1f, 60f)]
        public float sendRateHz = 10f;
        [Tooltip("이 변화량 이하면 전송 안 함 (degree)")]
        public float minChangeToSend = 0.5f;

        [Header("부드러운 이동")]
        [Tooltip("목표를 한 번에 보내지 않고 초당 이 각도씩만 따라간다.\n" +
                 "슬라이더를 확 움직여도 실물이 급가속하지 않아 덜덜거림이 줄어든다.\n" +
                 "0 이면 제한 없음(예전 동작).")]
        [Range(0f, 180f)]
        public float maxDegPerSecond = 40f;

        [Tooltip("그리퍼 전용 속도 제한. 그리퍼는 행정이 짧아 더 느리게 가는 게 안전하다.")]
        [Range(0f, 200f)]
        public float gripperMaxPercentPerSecond = 60f;

        /// <summary>서버로 실제로 내보내는 중간 목표. targetAngles 를 향해 천천히 따라간다.</summary>
        private float[] smoothedAngles;

        [Header("읽기 (실로봇 → 유니티) v3 신규")]
        [Tooltip("실로봇 각도를 몇 Hz로 폴링할지")]
        [Range(1, 60)]
        public int pollHz = 30;
        [Tooltip("폴링 활성화 (끄면 단방향)")]
        public bool pollEnabled = true;
        [Tooltip("각도 응답을 이 시간(초) 안에 못 받으면 포기하고 다시 요청한다.\n" +
                 "0 이면 무한 대기 — 응답 하나만 유실돼도 폴링이 영구 정지하므로 0 으로 두지 말 것.")]
        [Range(0.1f, 5f)]
        public float getResponseTimeout = 1.0f;

        [Header("Teach 모드 (손으로 밀어서 가르치기)")]
        [Tooltip("켜면 목표를 실물의 현재 위치로 계속 따라가게 한다.\n" +
                 "이걸 안 하면 모터가 Goal_Position 을 지키려 해서\n" +
                 "손을 떼는 순간 원래 자리로 튕겨 돌아간다.\n\n" +
                 "⚠️ 토크를 낮추는 것은 별도 작업이다 (라파에서 Torque_Limit 조정).\n" +
                 "   12V 팔은 토크를 완전히 끄면 중력으로 주저앉는다.")]
        public bool teachMode = false;

        [Tooltip("수동모드에서 서버가 토크를 실제로 푸는 관절.\n" +
                 "이 목록에 있는 것만 실물 위치를 목표로 채택한다.\n" +
                 "서버의 TEACH_FREE 와 같게 유지할 것.")]
        public string[] teachFreeJoints = { "shoulder_pan", "wrist_flex", "wrist_roll" };

        bool IsTeachFree(string motorName)
        {
            if (teachFreeJoints == null) return false;
            foreach (var n in teachFreeJoints)
                if (n == motorName) return true;
            return false;
        }

        [Header("시작 시 보호 (실물 우선)")]
        [Tooltip("실물 각도를 한 번 읽어 반영하기 전에는 쓰기를 막는다.\n" +
                 "끄면 Play 를 누르는 순간 시뮬의 홈 자세(0°)가 실물로 나가서 팔이 끌려간다.")]
        public bool holdWritesUntilSynced = true;

        /// <summary>실물 자세를 한 번 받아들였는지. 이 전에는 명령을 내보내지 않는다.</summary>
        public bool WritesEnabled { get; private set; } = false;

        /// <summary>실로봇에서 새 각도가 도착하면 발행 (key=motorName, value=각도deg)</summary>
        public event Action<Dictionary<string, float>> OnAnglesReceived;

        /// <summary>가장 최근에 받은 각도(도). UI 표시/디버그용.</summary>
        public Dictionary<string, float> LastReadAngles { get; private set; }
            = new Dictionary<string, float>();

        // 내부 상태
        private float[] targetAngles;
        private float[] lastSentAngles;
        private float[] homePose;
        private float gripperPercent = 50f;
        private float lastSendTime = 0f;

        // 폴링 상태
        private float pollTimer;
        private bool waitingForGetResponse;
        private bool holdWarned = false;
        private float getWaitTimer = 0f;
        private int getTimeoutCount = 0;

        public bool IsConnected => socketClient != null && socketClient.IsConnected;
        public string StatusMessage => socketClient != null ? socketClient.StatusMessage : "No socket";
        public event Action<string> OnStatusChanged;

        public int JointCount => joints?.Length ?? 0;

        void Awake()
        {
            if (joints == null || joints.Length == 0)
                joints = SOArmPresets.GetDefault6Axis();

            targetAngles = new float[joints.Length];
            lastSentAngles = new float[joints.Length];
            homePose = new float[joints.Length];

            for (int i = 0; i < joints.Length; i++)
            {
                targetAngles[i] = joints[i].homeAngle;
                lastSentAngles[i] = float.NaN;
                homePose[i] = joints[i].homeAngle;
            }

            if (socketClient == null)
                socketClient = GetComponent<SOArmSocketClient>();
            if (socketClient == null)
                socketClient = FindAnyObjectByType<SOArmSocketClient>();
        }

        void Start()
        {
            if (socketClient != null)
                socketClient.OnStatusChanged += (msg) => OnStatusChanged?.Invoke(msg);
        }

        void Update()
        {
            if (!IsConnected) return;

            // 1) 쓰기
            // ⚠️ 실물 자세를 아직 못 읽었으면 내보내지 않는다.
            //    Play 직후 시뮬은 홈(0°)에서 시작하므로, 그대로 보내면 실물이 홈으로 끌려간다.
            //    "실물이 진실" 이 디지털 트윈의 올바른 방향이다.
            if (EmergencyStopped)
            {
                // 🛑 비상정지 중에는 어떤 명령도 내보내지 않는다.
                //    읽기(폴링)는 계속해야 UI 가 현재 상태를 보여줄 수 있으므로 살려둔다.
            }
            else if (holdWritesUntilSynced && !WritesEnabled)
            {
                if (!holdWarned)
                {
                    Debug.Log($"[SOArmReal-{robotServerMode}] 실물 자세 수신 대기 중 — 쓰기 보류");
                    holdWarned = true;
                }
            }
            else if (Time.time - lastSendTime >= 1f / sendRateHz)
            {
                float dt = Time.time - lastSendTime;
                lastSendTime = Time.time;

                // 목표를 한 번에 던지지 않고 속도 제한을 걸어 따라간다.
                // 슬라이더를 확 움직였을 때 모터가 급가속하며 덜덜거리는 것을 막는다.
                EnsureSmoothed();

                for (int i = 0; i < joints.Length; i++)
                {
                    float limit = joints[i].motorName == "gripper"
                        ? gripperMaxPercentPerSecond * 0.01f * Mathf.Abs(joints[i].maxAngle - joints[i].minAngle)
                        : maxDegPerSecond;

                    if (limit > 0f)
                        smoothedAngles[i] = Mathf.MoveTowards(smoothedAngles[i], targetAngles[i], limit * dt);
                    else
                        smoothedAngles[i] = targetAngles[i];

                    bool isFirstSend = float.IsNaN(lastSentAngles[i]);
                    float diff = isFirstSend ? float.MaxValue : Mathf.Abs(smoothedAngles[i] - lastSentAngles[i]);

                    if (diff > minChangeToSend)
                    {
                        float serverValue;

                        // ⚠️ 그리퍼는 각도 슬라이더로도 조작될 수 있다(UI 가 J6 도 그린다).
                        //    SetGripperTarget() 에만 게이트를 걸면 이 경로로 새어나가므로
                        //    송신 직전에 여기서도 반드시 검사한다.
                        if (joints[i].motorName == "gripper")
                        {
                            if (!PincOpenSafety.TryApprove(0f, out _))
                            {
                                if (!gripperBlockWarned)
                                {
                                    Debug.LogWarning($"[SOArmReal-{robotServerMode}] 그리퍼 명령 차단(J{i} 각도 경로) — "
                                                     + PincOpenSafety.LastBlockReason);
                                    gripperBlockWarned = true;
                                }
                                continue;   // 보내지 않는다
                            }

                            // 각도 → 퍼센트 → 여유분 적용된 정규화값
                            // 반전은 관절별 invertSign 을 쓴다 (두 로봇의 그리퍼 방향이 반대)
                            float pct = Mathf.InverseLerp(
                                joints[i].minAngle, joints[i].maxAngle, smoothedAngles[i]) * 100f;
                            serverValue = PincOpenSafety.PercentToServerValue(pct, joints[i].invertSign);
                        }
                        else
                        {
                            serverValue = SOArmMotorMapper.AngleToServerValue(smoothedAngles[i], joints[i]);
                        }

                        bool ok = socketClient.SendMotorCommand(
                            robotServerMode,
                            joints[i].motorName,
                            serverValue);
                        if (ok) lastSentAngles[i] = smoothedAngles[i];
                    }
                }
            }

            // 2) 읽기 (30Hz 폴링)
            if (pollEnabled)
            {
                // ⚠️ 응답이 한 번이라도 유실되면 waitingForGetResponse 가 true 에 갇혀
                //    폴링이 영구 정지한다(타임아웃이 없었음). 그러면 시뮬이 실물을
                //    영영 못 따라가고, 첫 수신을 기다리는 쓰기 보류도 안 풀린다.
                //    그래서 응답 대기에도 마감 시간을 둔다.
                if (waitingForGetResponse)
                {
                    getWaitTimer += Time.deltaTime;
                    if (getWaitTimer >= getResponseTimeout)
                    {
                        waitingForGetResponse = false;
                        getWaitTimer = 0f;
                        getTimeoutCount++;
                        if (getTimeoutCount <= 3 || getTimeoutCount % 100 == 0)
                        {
                            Debug.LogWarning($"[SOArmReal-{robotServerMode}] 각도 응답 타임아웃 " +
                                             $"({getResponseTimeout:F1}s, 누적 {getTimeoutCount}회) — 폴링 재개");
                        }
                    }
                }
                else
                {
                    pollTimer += Time.deltaTime;
                    float interval = 1f / Mathf.Max(1, pollHz);
                    if (pollTimer >= interval)
                    {
                        pollTimer = 0f;
                        getWaitTimer = 0f;
                        RequestAnglesOnce();
                    }
                }
            }
        }

        /// <summary>
        /// 실물의 현재 자세를 목표값으로 채택하고 쓰기를 연다.
        ///
        /// targetAngles 뿐 아니라 lastSentAngles 까지 같은 값으로 채우는 것이 핵심이다.
        /// 그러지 않으면 다음 송신 주기에서 "변화량이 크다"고 판단해
        /// 명령이 한 번 나가고 실물이 움찔한다.
        /// </summary>
        /// <summary>smoothedAngles 배열이 준비돼 있는지 확인하고, 없으면 현재 목표로 초기화한다.</summary>
        void EnsureSmoothed()
        {
            if (smoothedAngles != null && smoothedAngles.Length == joints.Length) return;
            smoothedAngles = new float[joints.Length];
            for (int i = 0; i < joints.Length; i++) smoothedAngles[i] = targetAngles[i];
        }

        /// <summary>
        /// 실물의 현재 자세를 목표로 채택한다.
        ///
        /// <paramref name="onlyTeachFree"/> 가 true 면 **실제로 토크가 풀린 관절만** 채택한다.
        /// 수동모드에서 필요하다. 그리퍼는 1:345 감속이라 손으로 벌릴 수 없어
        /// **슬라이더가 유일한 조작 수단**인데, 여기서 목표를 실물값으로 덮어쓰면
        /// 슬라이더로 넣은 목표가 다음 폴링(30Hz)에 즉시 지워진다.
        /// 그래서 교시 중에는 그리퍼를 채택 대상에서 뺀다.
        /// 팔은 반대로 계속 채택해야 손으로 민 자리에 머문다.
        /// </summary>
        void AdoptRealPose(Dictionary<string, float> angles, bool onlyTeachFree = false)
        {
            if (joints == null) return;
            EnsureSmoothed();

            bool firstAdoption = !WritesEnabled;

            for (int i = 0; i < joints.Length; i++)
            {
                if (onlyTeachFree && !IsTeachFree(joints[i].motorName)) continue;

                if (angles.TryGetValue(joints[i].motorName, out float deg))
                {
                    targetAngles[i] = deg;
                    lastSentAngles[i] = deg;
                    smoothedAngles[i] = deg;   // 부드러움 시작점도 현재 자세로
                }
            }

            WritesEnabled = true;

            // ⚠️ 교시 중에는 이 함수가 폴링마다(30Hz) 불린다. 무조건 찍으면
            //    README 에 적힌 "Debug.Log 초당 360회 → Editor 프리징" 을 다시 만든다.
            //    첫 채택에서만 남긴다.
            if (firstAdoption)
                Debug.Log($"[SOArmReal-{robotServerMode}] 실물 자세 채택 완료 — 쓰기 허용. " +
                          string.Join(", ", System.Linq.Enumerable.Select(
                              joints, j => $"{j.motorName}={(angles.ContainsKey(j.motorName) ? angles[j.motorName].ToString("F1") : "?")}")));
        }

        // ====== v3 신규: 각도 읽기 ======
        /// <summary>한 번만 실로봇 각도를 읽고, 받으면 OnAnglesReceived 이벤트 발행.</summary>
        public void RequestAnglesOnce(Action<Dictionary<string, float>> oneShot = null)
        {
            if (socketClient == null || !socketClient.IsConnected) return;
            waitingForGetResponse = true;
            socketClient.RequestAngles(robotServerMode, (resp) =>
            {
                waitingForGetResponse = false;
                var angles = ParseAndConvertAngles(resp, robotServerMode, joints);
                if (angles != null && angles.Count > 0)
                {
                    LastReadAngles = angles;

                    // Teach 모드: 손으로 민 자리에 머물도록 목표를 실물 위치로 끌고 간다.
                    //
                    // ⚠️ 단, **실제로 토크가 풀린 관절만** 그렇게 한다.
                    //    shoulder_lift / elbow_flex 는 중력을 버텨야 해서 토크를 유지하는데,
                    //    이 관절까지 채택하면 슬라이더로 넣은 목표가 33ms 만에 지워져
                    //    손으로도 안 밀리고 슬라이더로도 안 움직이는 상태가 된다.
                    //    그리퍼도 같은 이유로 제외 대상이다.
                    if (teachMode && WritesEnabled) AdoptRealPose(angles, onlyTeachFree: true);

                    // 첫 수신이면 실물 자세를 그대로 목표로 삼는다.
                    // 이렇게 해야 쓰기가 풀린 직후에도 실물이 제자리에 머문다.
                    if (!WritesEnabled) AdoptRealPose(angles);

                    OnAnglesReceived?.Invoke(angles);
                    oneShot?.Invoke(angles);
                }
            });
        }

        // ====== v3 신규: 홈포즈 저장 ======
        /// <summary>
        /// 현재 자세(슬라이더로 만든 자세)를 새 0점으로 서버에 저장.
        /// </summary>
        public void SaveHomePose(Action<bool> done = null)
        {
            if (socketClient == null || !socketClient.IsConnected) { done?.Invoke(false); return; }
            socketClient.RequestSetHome(robotServerMode, (resp) =>
            {
                bool ok = resp != null && resp.Contains("\"ok\":true");
                Debug.Log($"[{robotServerMode}] 홈포즈 저장 응답: {resp}");
                if (ok)
                {
                    // 저장 후엔 슬라이더 캐시 리셋 (새 0점이므로)
                    for (int i = 0; i < targetAngles.Length; i++)
                    {
                        targetAngles[i] = 0f;
                        lastSentAngles[i] = float.NaN; // 다음 폴링에서 새 값 받아 적용되게
                        homePose[i] = 0f;
                    }
                }
                done?.Invoke(ok);
            });
        }

        // ====== 수동(Teach) 모드 ======
        /// <summary>
        /// 손으로 밀어서 자세를 가르치는 모드.
        ///
        /// 두 가지가 **같이** 일어나야 동작한다:
        ///   1) 실물   — 서버가 중력 부하 없는 관절의 토크를 끈다 (안 끄면 손으로 안 밀림)
        ///   2) 유니티 — teachMode 로 목표를 실물 위치에 계속 맞춘다
        ///              (안 하면 폴링으로 읽은 자세와 별개로 옛 목표가 계속 나가
        ///               토크가 살아있는 관절이 원래 자리로 되돌아간다)
        ///
        /// 어느 한쪽만 켜면 "안 밀린다" 또는 "손 떼면 튕겨 돌아간다" 가 된다.
        /// 그래서 한 함수로 묶어 둔다.
        /// </summary>
        public void SetTeachMode(bool enable, Action<bool> done = null)
        {
            teachMode = enable;

            if (socketClient == null || !socketClient.IsConnected)
            {
                Debug.LogWarning($"[SOArmReal-{robotServerMode}] 수동모드 {(enable ? "ON" : "OFF")} — " +
                                 "서버 미연결이라 유니티 쪽만 바뀜. 실물 토크는 그대로다.");
                done?.Invoke(false);
                return;
            }

            socketClient.RequestTeach(robotServerMode, enable, (resp) =>
            {
                bool ok = resp != null && resp.Contains("\"ok\":true");
                Debug.Log($"[SOArmReal-{robotServerMode}] 🔓 수동모드 {(enable ? "ON" : "OFF")} " +
                          $"— 서버 응답 {(ok ? "정상" : "실패: " + resp)}");
                done?.Invoke(ok);
            });
        }

        // ====== v3 신규: 토크 ======
        public void SetServoTorque(bool enable, Action<bool> done = null)
        {
            if (socketClient == null || !socketClient.IsConnected) { done?.Invoke(false); return; }
            socketClient.RequestTorque(robotServerMode, enable, (resp) =>
            {
                bool ok = resp != null && resp.Contains("\"ok\":true");
                done?.Invoke(ok);
            });
        }

        // ────────────────────────────────────────────────────────
        // 파서: 서버 응답(JSON) → {motorName: 각도deg}
        // ────────────────────────────────────────────────────────
        /// <summary>
        /// 서버 응답 예: {"ok":true,"robot1":{"shoulder_pan":12.3,...}}
        /// 값은 -100~100 정규화 값 → ServerValueToAngle 로 각도(deg)로 변환.
        /// </summary>
        public static Dictionary<string, float> ParseAndConvertAngles(
            string json, string mode, SOArmJointConfig[] joints)
        {
            if (string.IsNullOrEmpty(json)) return null;

            // mode에 해당하는 sub-object 찾기
            string key = $"\"{mode}\"";
            int kIdx = json.IndexOf(key, StringComparison.Ordinal);
            if (kIdx < 0) return null;

            int braceStart = json.IndexOf('{', kIdx);
            if (braceStart < 0) return null;

            int depth = 0;
            int braceEnd = -1;
            for (int i = braceStart; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) { braceEnd = i; break; }
                }
            }
            if (braceEnd < 0) return null;

            string inner = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            // 모터별 정규화 값 파싱
            var serverValues = new Dictionary<string, float>();
            foreach (var part in inner.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon < 0) continue;
                string k = part.Substring(0, colon).Trim().Trim('"');
                string v = part.Substring(colon + 1).Trim();
                if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out float f))
                {
                    serverValues[k] = f;
                }
            }

            // 정규화 값 → 각도 변환 (관절별 매핑 사용)
            var result = new Dictionary<string, float>();
            if (joints != null)
            {
                foreach (var joint in joints)
                {
                    if (joint == null || string.IsNullOrEmpty(joint.motorName)) continue;
                    if (serverValues.TryGetValue(joint.motorName, out float sv))
                    {
                        // 반전은 ServerValueToAngle 안에서 joint.invertSign 으로 처리된다.
                        // (전역 InvertDirection 을 쓰면 두 로봇의 그리퍼 방향이 반대라 한쪽이 틀어진다)
                        float deg = SOArmMotorMapper.ServerValueToAngle(sv, joint);
                        result[joint.motorName] = deg;
                    }
                }
            }
            else
            {
                // joints 없으면 정규화 값 그대로 (디버그용)
                foreach (var kv in serverValues) result[kv.Key] = kv.Value;
            }
            return result;
        }

        // ────────────────────────────────────────────────────────
        // ISOArmController 구현 (기존 그대로)
        // ────────────────────────────────────────────────────────
        public void Connect() => socketClient?.Connect();
        public void Disconnect() => socketClient?.Disconnect();

        public string GetJointName(int i) => joints[i].displayName;
        public float GetJointMinAngle(int i) => joints[i].minAngle;
        public float GetJointMaxAngle(int i) => joints[i].maxAngle;
        public float GetJointAngle(int i) => targetAngles[i];

        public void SetJointTarget(int i, float angleDeg)
        {
            // 🛑 정지 중에는 목표 변경 자체를 막는다 (SOArmSimController 와 같은 규칙).
            //    Update() 의 송신 루프가 이미 막혀 있어 팔은 안 움직이지만, 여기를 열어두면
            //    정지 중에 들어온 목표가 targetAngles 에 쌓인다. 재생이 SOArmManager 를
            //    직접 부르는 경로가 정확히 그렇다 — SOArmDualManager 의 라우팅 게이트를
            //    지나가지 않는다. 해제 시 AdoptRealPose 가 덮어써서 사고로 이어지진 않지만,
            //    "정지 중에 받은 목표" 라는 상태를 남기지 않는 편이 추적하기 쉽다.
            if (EmergencyStopped) return;
            if (i < 0 || i >= joints.Length) return;
            targetAngles[i] = Mathf.Clamp(angleDeg, joints[i].ClampMin, joints[i].ClampMax);
        }

        public void SetAllJointTargets(float[] angles)
        {
            if (angles == null) return;
            int n = Mathf.Min(angles.Length, joints.Length);
            for (int i = 0; i < n; i++) SetJointTarget(i, angles[i]);
        }

        public float GetGripperPercent() => gripperPercent;

        /// <summary>
        /// 그리퍼 목표를 퍼센트로 지정한다. (0 = 닫힘, 100 = 열림)
        ///
        /// ⚠️ 여기서 직접 서버로 보내지 않는다. **targetAngles 만 갱신**하고
        ///    실제 전송은 Update() 의 송신 루프에 맡긴다.
        ///
        ///    예전에는 이 함수가 곧바로 명령을 쐈는데, targetAngles 는 그대로라
        ///    다음 송신 주기에 루프가 옛 값을 다시 보내 **닫혔다가 도로 열리는**
        ///    현상이 생겼다. 한 모터에 두 곳에서 다른 값을 쓰던 셈이다.
        ///    송신 주체를 루프 하나로 합쳐서 해결했다. (속도 제한·안전 게이트도 그쪽에 있다)
        /// </summary>
        public void SetGripperTarget(float percent)
        {
            if (EmergencyStopped) return;   // 🛑 SetJointTarget 과 같은 이유
            gripperPercent = Mathf.Clamp(percent, 0f, 100f);
            if (joints == null || joints.Length == 0) return;

            int last = joints.Length - 1;
            if (joints[last].motorName != "gripper") return;

            // 퍼센트 → 각도. 0% = 닫힘(minAngle), 100% = 열림(maxAngle)
            targetAngles[last] = Mathf.Lerp(
                joints[last].ClampMin, joints[last].ClampMax, gripperPercent * 0.01f);
        }

        bool gripperBlockWarned = false;

        /// <summary>
        /// 비상정지. 걸린 동안은 어떤 명령도 서버로 나가지 않는다.
        /// ReleaseStop() 을 부르기 전까지 유지된다 (자동 해제 없음).
        /// </summary>
        public bool EmergencyStopped { get; private set; }

        /// <summary>
        /// 🛑 비상정지.
        ///
        /// ⚠️ 토크를 끄지 않는다. 12V STS3215 는 토크를 끄면 팔이 중력으로 떨어져
        ///    오히려 더 위험하다. 대신 **현재 측정 위치를 목표로 다시 보내서**
        ///    그 자리에 멈춰 세운다.
        ///
        /// 진행 중이던 목표를 그대로 두면 정지 버튼을 눌러도 팔이 계속 가던 곳까지
        /// 간다. 그래서 목표를 "지금 있는 자리"로 덮어쓰는 것이 핵심이다.
        /// </summary>
        public void StopMotion()
        {
            if (EmergencyStopped) return;
            EmergencyStopped = true;

            if (!IsConnected)
            {
                Debug.LogWarning($"[SOArmReal-{robotServerMode}] 🛑 비상정지 — 단, 서버 미연결이라 정지 명령은 못 보냄");
                return;
            }

            // 가장 최근에 읽은 실제 위치(30Hz 폴링이므로 보통 33ms 이내)를 정지 지점으로 쓴다.
            int sent = 0;
            for (int i = 0; i < joints.Length; i++)
            {
                string mname = joints[i].motorName;
                if (LastReadAngles == null || !LastReadAngles.TryGetValue(mname, out float here))
                    continue;   // 못 읽은 관절은 건드리지 않는다 (엉뚱한 값을 보내는 것보다 낫다)

                EnsureSmoothed();
                targetAngles[i] = here;
                lastSentAngles[i] = here;   // 해제 후 튀지 않도록 함께 갱신
                smoothedAngles[i] = here;

                float serverValue = mname == "gripper"
                    ? PincOpenSafety.PercentToServerValue(
                        Mathf.InverseLerp(joints[i].minAngle, joints[i].maxAngle, here) * 100f)
                    : SOArmMotorMapper.AngleToServerValue(here, joints[i]);

                if (mname == "gripper" && !PincOpenSafety.TryApprove(0f, out _))
                    continue;   // 그리퍼가 잠겨 있으면 그대로 둔다

                if (socketClient.SendMotorCommand(robotServerMode, mname, serverValue)) sent++;
            }

            Debug.LogWarning($"[SOArmReal-{robotServerMode}] 🛑 비상정지 — 현재 위치로 {sent}개 관절 고정. " +
                             "해제하려면 ReleaseStop() 또는 UI의 '정지 해제'.");
        }

        /// <summary>비상정지 해제. 현재 실물 자세를 다시 목표로 삼아 튐 없이 재개한다.</summary>
        public void ReleaseStop()
        {
            if (!EmergencyStopped) return;
            EmergencyStopped = false;

            if (LastReadAngles != null && LastReadAngles.Count > 0)
                AdoptRealPose(LastReadAngles);

            Debug.Log($"[SOArmReal-{robotServerMode}] ▶ 비상정지 해제 — 현재 자세부터 재개");
        }

        public void GoToHome() => SetAllJointTargets(homePose);

        public void SetHomeFromCurrent()
        {
            for (int i = 0; i < joints.Length; i++) homePose[i] = targetAngles[i];
        }

        public float[] GetHomePose() => (float[])homePose.Clone();

        public void SetHomePose(float[] angles)
        {
            if (angles == null) return;
            int n = Mathf.Min(angles.Length, homePose.Length);
            for (int i = 0; i < n; i++) homePose[i] = angles[i];
        }
    }
}
