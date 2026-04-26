using System;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 실제 SO-ARM101 로봇 컨트롤러.
    /// SOArmSocketClient로 라즈베리파이 LeRobot 서버에 명령 전송.
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

        [Header("전송 속도 제한")]
        [Range(1f, 60f)]
        public float sendRateHz = 10f;

        [Tooltip("이 변화량 이하면 전송 안 함 (degree)")]
        public float minChangeToSend = 0.5f;

        // 내부 상태
        private float[] targetAngles;
        private float[] lastSentAngles;
        private float[] homePose;
        private float gripperPercent = 50f;
        private float lastSendTime = 0f;

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

            // 소켓 자동 탐색
            if (socketClient == null)
                socketClient = GetComponent<SOArmSocketClient>();
            if (socketClient == null)
                socketClient = FindObjectOfType<SOArmSocketClient>();
        }

        void Start()
        {
            if (socketClient != null)
                socketClient.OnStatusChanged += (msg) => OnStatusChanged?.Invoke(msg);
        }

        void Update()
        {
            if (!IsConnected) return;

            // 전송 속도 제한
            if (Time.time - lastSendTime < 1f / sendRateHz) return;
            lastSendTime = Time.time;

            // 바뀐 관절만 전송
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

        // ── ISOArmController ────────────────────────────────────
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
            // 그리퍼 모터에 직접 명령 전송 (관절 변환과 별개)
            if (IsConnected)
            {
                float value = SOArmMotorMapper.PercentToGripperValue(gripperPercent);
                socketClient.SendMotorCommand(robotServerMode, "gripper", value);
            }
        }

        public void StopMotion()
        {
            // 현재 마지막 명령 위치 유지
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
