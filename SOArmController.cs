using UnityEngine;

namespace RobotControl
{
    /// <summary>
    /// SO-ARM 관절 하나에 대한 설정.
    /// 유니티 관절 Transform + 실제 로봇 모터 이름을 매핑.
    /// </summary>
    [System.Serializable]
    public class SOArmJoint
    {
        [Tooltip("표시 이름 (예: Shoulder Pan)")]
        public string displayName = "Joint";

        [Tooltip("라즈베리파이 서버에 보낼 모터 이름")]
        public string motorName = "shoulder_pan";

        [Tooltip("유니티에서 회전시킬 Articulation Body")]
        public ArticulationBody articulationBody;

        [Tooltip("최소 각도 (degree)")]
        public float minAngle = -100f;

        [Tooltip("최대 각도 (degree)")]
        public float maxAngle = 100f;

        [Tooltip("현재 각도 값 (-100 ~ 100)")]
        [Range(-100f, 100f)]
        public float currentValue = 0f;

        [Tooltip("유니티 회전 반대 방향이면 체크")]
        public bool invertSign = false;
    }

    /// <summary>
    /// SO-ARM 로봇 컨트롤러.
    /// 유니티 시뮬 + 실제 로봇 동기화 제어.
    /// </summary>
    public class SOArmController : MonoBehaviour
    {
        [Header("■ 소켓 연결")]
        public RobotSocketClient socketClient;

        [Header("■ 로봇 ID")]
        [Tooltip("Robot1 / Robot2 / Mirror 중 선택")]
        public RobotSocketClient.RobotMode robotMode = RobotSocketClient.RobotMode.Robot1;

        [Header("■ 관절 설정 (6개)")]
        public SOArmJoint[] joints = new SOArmJoint[6];

        [Header("■ 동기화 설정")]
        [Tooltip("체크시 슬라이더 값이 바뀔 때마다 실제 로봇에도 명령 전송")]
        public bool syncRealRobot = true;

        [Tooltip("체크시 실제 로봇과 유니티 시뮬이 모두 움직임")]
        public bool updateSimulation = true;

        private float[] lastSentValues;

        void Start()
        {
            lastSentValues = new float[joints.Length];
            for (int i = 0; i < joints.Length; i++)
                lastSentValues[i] = float.NaN;
        }
        [Header("■ 전송 속도 제한")]
        [Tooltip("초당 몇 번 명령 보낼지 (낮을수록 안정적)")]
        public float sendRateHz = 10f;

        private float lastSendTime = 0f;

        void Update()
        {
            // 유니티 시뮬은 매 프레임 업데이트 (부드럽게)
            for (int i = 0; i < joints.Length; i++)
            {
                SOArmJoint joint = joints[i];
                if (joint == null) continue;

                if (updateSimulation && joint.articulationBody != null)
                {
                    var drive = joint.articulationBody.xDrive;
                    float targetAngle = joint.currentValue * (joint.invertSign ? -1 : 1);
                    drive.target = Mathf.Lerp(joint.minAngle, joint.maxAngle,
                                              (targetAngle + 100f) / 200f);
                    joint.articulationBody.xDrive = drive;
                }
            }

            // 실제 로봇 명령은 제한된 속도로 (1초에 10번 정도)
            if (Time.time - lastSendTime < 1f / sendRateHz) return;
            lastSendTime = Time.time;

            for (int i = 0; i < joints.Length; i++)
            {
                SOArmJoint joint = joints[i];
                if (joint == null) continue;

                if (syncRealRobot && socketClient != null && socketClient.IsConnected)
                {
                    if (Mathf.Abs(joint.currentValue - lastSentValues[i]) > 0.5f)
                    {
                        socketClient.SetMode(robotMode);
                        socketClient.SendMotorCommand(joint.motorName, joint.currentValue);
                        lastSentValues[i] = joint.currentValue;
                    }
                }
            }
        }


        /// <summary>관절 값 설정 (외부에서 호출용).</summary>
        public void SetJointValue(int index, float value)
        {
            if (index < 0 || index >= joints.Length) return;
            joints[index].currentValue = Mathf.Clamp(value, -100f, 100f);
        }

        /// <summary>모든 관절 0으로 리셋.</summary>
        public void ResetAllJoints()
        {
            foreach (var joint in joints)
            {
                if (joint != null) joint.currentValue = 0f;
            }
        }
    }
}
