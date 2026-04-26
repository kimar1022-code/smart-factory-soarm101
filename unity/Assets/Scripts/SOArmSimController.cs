using System;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 유니티 시뮬레이션 SO-ARM 컨트롤러.
    /// URDF 임포트한 ArticulationBody를 직접 제어.
    /// 
    /// 실로봇 없이도 작동 (시뮬레이션 단독 사용 가능).
    /// </summary>
    public class SOArmSimController : MonoBehaviour, ISOArmController
    {
        [Header("관절 설정 (6개)")]
        public SOArmJointConfig[] joints;

        [Header("ArticulationBody 드라이브 파라미터")]
        public float stiffness = 10000f;
        public float damping = 1000f;
        public float forceLimit = 1000f;

        // 내부 상태
        private float[] targetAngles;
        private float[] homePose;
        private float gripperPercent = 50f;
        private bool isReady = false;

        public bool IsConnected => isReady;
        public string StatusMessage => isReady ? "Sim Ready" : "Sim Initializing";
        public event Action<string> OnStatusChanged;

        public int JointCount => joints?.Length ?? 0;

        void Awake()
        {
            // 프리셋 자동 적용 (관절 정보가 비어있으면)
            if (joints == null || joints.Length == 0)
                joints = SOArmPresets.GetDefault6Axis();

            targetAngles = new float[joints.Length];
            homePose = new float[joints.Length];

            for (int i = 0; i < joints.Length; i++)
            {
                targetAngles[i] = joints[i].homeAngle;
                homePose[i] = joints[i].homeAngle;
            }
        }

        void Start()
        {
            ConfigureArticulationBodies();
            isReady = true;
            OnStatusChanged?.Invoke(StatusMessage);
        }

        void Update()
        {
            ApplyToArticulationBodies();
        }

        void ConfigureArticulationBodies()
        {
            for (int i = 0; i < joints.Length; i++)
            {
                var ab = joints[i].articulationBody;
                if (ab == null) continue;

                var drive = ab.xDrive;
                drive.stiffness = stiffness;
                drive.damping = damping;
                drive.forceLimit = forceLimit;
                drive.lowerLimit = joints[i].minAngle;
                drive.upperLimit = joints[i].maxAngle;
                ab.xDrive = drive;
            }
        }

        void ApplyToArticulationBodies()
        {
            for (int i = 0; i < joints.Length; i++)
            {
                var j = joints[i];
                if (j.articulationBody == null) continue;

                float angle = targetAngles[i] + j.angleOffset;
                if (j.invertSign) angle = -angle;

                var drive = j.articulationBody.xDrive;
                drive.target = Mathf.Clamp(angle, j.minAngle, j.maxAngle);
                j.articulationBody.xDrive = drive;
            }
        }

        // ── ISOArmController ────────────────────────────────────
        public void Connect() { isReady = true; }
        public void Disconnect() { isReady = false; }

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
            // 그리퍼는 마지막 관절(J6)으로 처리
            int gripperIdx = joints.Length - 1;
            if (gripperIdx >= 0)
            {
                float min = joints[gripperIdx].minAngle;
                float max = joints[gripperIdx].maxAngle;
                SetJointTarget(gripperIdx, Mathf.Lerp(min, max, gripperPercent / 100f));
            }
        }

        public void StopMotion()
        {
            // 현재 위치 유지
        }

        public void GoToHome()
        {
            SetAllJointTargets(homePose);
        }

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
