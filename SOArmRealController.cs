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

        [Header("읽기 (실로봇 → 유니티) v3 신규")]
        [Tooltip("실로봇 각도를 몇 Hz로 폴링할지")]
        [Range(1, 60)]
        public int pollHz = 30;
        [Tooltip("폴링 활성화 (끄면 단방향)")]
        public bool pollEnabled = true;

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

            // 1) 쓰기 (기존 로직 그대로)
            if (Time.time - lastSendTime >= 1f / sendRateHz)
            {
                lastSendTime = Time.time;
                for (int i = 0; i < joints.Length; i++)
                {
                    bool isFirstSend = float.IsNaN(lastSentAngles[i]);
                    float diff = isFirstSend ? float.MaxValue : Mathf.Abs(targetAngles[i] - lastSentAngles[i]);

                    if (diff > minChangeToSend)
                    {
                        float serverValue = SOArmMotorMapper.AngleToServerValue(targetAngles[i], joints[i]);
                        bool ok = socketClient.SendMotorCommand(
                            robotServerMode,
                            joints[i].motorName,
                            serverValue);
                        if (ok) lastSentAngles[i] = targetAngles[i];
                    }
                }
            }

            // 2) 읽기 (v3 신규: 30Hz 폴링)
            if (pollEnabled && !waitingForGetResponse)
            {
                pollTimer += Time.deltaTime;
                float interval = 1f / Mathf.Max(1, pollHz);
                if (pollTimer >= interval)
                {
                    pollTimer = 0f;
                    RequestAnglesOnce();
                }
            }
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
            if (i < 0 || i >= joints.Length) return;
            targetAngles[i] = Mathf.Clamp(angleDeg, joints[i].minAngle, joints[i].maxAngle);
        }

        public void SetAllJointTargets(float[] angles)
        {
            if (angles == null) return;
            int n = Mathf.Min(angles.Length, joints.Length);
            for (int i = 0; i < n; i++) SetJointTarget(i, angles[i]);
        }

        public float GetGripperPercent() => gripperPercent;

        public void SetGripperTarget(float percent)
        {
            gripperPercent = Mathf.Clamp(percent, 0f, 100f);
            if (IsConnected)
            {
                float value = SOArmMotorMapper.PercentToGripperValue(gripperPercent);
                socketClient.SendMotorCommand(robotServerMode, "gripper", value);
            }
        }

        public void StopMotion()
        {
            Debug.Log($"[SOArmReal-{robotServerMode}] StopMotion");
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
