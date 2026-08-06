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

        /// <summary>
        /// joints 와 targetAngles 의 길이를 맞춘다.
        ///
        /// ⚠️ targetAngles 는 Awake 에서 joints.Length 크기로 한 번만 만든다.
        ///    인스펙터에서 joints 를 늘리거나 줄이면 두 배열 길이가 어긋나서,
        ///    joints.Length 로만 검사하는 가드를 통과한 뒤 targetAngles[i] 에서 터진다.
        ///    Awake 전에 외부(UI 등)에서 불릴 수도 있으므로 접근 지점마다 확인한다.
        /// </summary>
        void EnsureArrays()
        {
            if (joints == null) return;
            if (targetAngles != null && targetAngles.Length == joints.Length) return;

            var old = targetAngles;
            targetAngles = new float[joints.Length];
            for (int i = 0; i < joints.Length; i++)
                targetAngles[i] = (old != null && i < old.Length) ? old[i] : joints[i].homeAngle;
        }

        bool Valid(int i) { EnsureArrays(); return joints != null && i >= 0 && i < joints.Length && targetAngles != null && i < targetAngles.Length; }

        public string GetJointName(int i) => Valid(i) ? joints[i].displayName : $"J{i}";
        public float GetJointMinAngle(int i) => Valid(i) ? joints[i].minAngle : -180f;
        public float GetJointMaxAngle(int i) => Valid(i) ? joints[i].maxAngle : 180f;
        public float GetJointAngle(int i) => Valid(i) ? targetAngles[i] : 0f;

        /// <summary>
        /// 시뮬이 **지금 실제로 가 있는** 각도. ArticulationBody 에서 직접 읽는다.
        ///
        /// `GetJointAngle`(=`targetAngles`)은 던진 즉시 최종 목표라 도착 판정에 못 쓴다.
        /// 물리 각도를 `StopMotion` 과 같은 방식으로 논리 각도(부호·오프셋)로 되돌린다.
        /// 드라이브가 없으면 목표값으로 돌아간다.
        /// </summary>
        public float GetMeasuredJointAngle(int i)
        {
            if (!Valid(i)) return 0f;

            var ab = joints[i].articulationBody;
            if (ab == null || ab.jointPosition.dofCount == 0) return targetAngles[i];

            float cur = ab.jointPosition[0] * Mathf.Rad2Deg;
            float logical = joints[i].invertSign ? -cur : cur;
            return logical - joints[i].angleOffset;
        }

        [Header("디버그")]
        [Tooltip("관절 목표가 바뀔 때마다 로그를 남긴다.\n" +
                 "⚠️ 양방향 동기화(30Hz × 2대 × 6관절 = 360회/초)에서 켜면\n" +
                 "   스택트레이스 추출 비용 때문에 에디터가 사실상 멈춘다. 평소엔 끌 것.")]
        public bool logJointTargets = false;

        public void SetJointTarget(int i, float angleDeg)
        {
            if (!Valid(i)) return;          // joints / targetAngles 양쪽 길이를 모두 확인한다
            if (EmergencyStopped) return;   // 🛑 정지 중에는 목표 변경 자체를 막는다
            targetAngles[i] = Mathf.Clamp(angleDeg, joints[i].ClampMin, joints[i].ClampMax);

            if (logJointTargets)
            {
                Debug.Log($"[Sim {gameObject.name}] J{i} target={targetAngles[i]:F2}, " +
                          $"AB={(joints[i].articulationBody != null ? joints[i].articulationBody.name : "NULL")}");
            }
        }

        /// <summary>
        /// 목표만 바꾸는 게 아니라 관절을 **그 각도로 즉시 이동**시킨다.
        ///
        /// Play 직후 첫 동기화에 쓴다. SetAllJointTargets 만 쓰면 드라이브가
        /// 홈 자세(0°)에서 실물 자세까지 물리적으로 움직여 가느라
        /// 팔이 한 번 휙 쓸고 지나가는 게 보인다. 디지털 트윈이라면
        /// 처음부터 실물 자세로 서 있어야 한다.
        /// </summary>
        public void SnapAllJointTargets(float[] angles)
        {
            if (angles == null) return;
            int n = Mathf.Min(angles.Length, joints.Length);

            for (int i = 0; i < n; i++)
            {
                SetJointTarget(i, angles[i]);

                var ab = joints[i].articulationBody;
                if (ab == null || ab.jointPosition.dofCount == 0) continue;

                float a = targetAngles[i] + joints[i].angleOffset;
                if (joints[i].invertSign) a = -a;
                a = Mathf.Clamp(a, joints[i].minAngle, joints[i].maxAngle);

                // 드라이브 목표와 실제 관절 위치를 함께 맞춰 순간이동시킨다
                var d = ab.xDrive;
                d.target = a;
                ab.xDrive = d;
                ab.jointPosition = new ArticulationReducedSpace(a * Mathf.Deg2Rad);
                ab.jointVelocity = new ArticulationReducedSpace(0f);
            }

            Debug.Log($"[Sim {gameObject.name}] 실물 자세로 스냅 완료 ({n}개 관절)");
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

        /// <summary>비상정지 중에는 목표각 변경을 받지 않는다.</summary>
        public bool EmergencyStopped { get; private set; }

        /// <summary>
        /// 🛑 비상정지 — 시뮬을 현재 자세에 얼린다.
        ///
        /// ArticulationBody 의 목표를 **지금 실제로 가 있는 각도**로 덮어쓴다.
        /// 목표만 남겨두면 정지 후에도 관절이 가던 곳까지 계속 움직인다.
        /// </summary>
        public void StopMotion()
        {
            if (EmergencyStopped) return;
            EmergencyStopped = true;

            for (int i = 0; i < joints.Length; i++)
            {
                var ab = joints[i].articulationBody;
                if (ab == null) continue;

                // 물리적으로 현재 도달해 있는 각도
                float cur = ab.jointPosition.dofCount > 0
                    ? ab.jointPosition[0] * Mathf.Rad2Deg
                    : ab.xDrive.target;

                var d = ab.xDrive;
                d.target = Mathf.Clamp(cur, d.lowerLimit, d.upperLimit);
                ab.xDrive = d;

                // 내부 목표도 같이 맞춰야 Update() 가 되돌리지 않는다
                float logical = joints[i].invertSign ? -cur : cur;
                targetAngles[i] = Mathf.Clamp(logical - joints[i].angleOffset,
                                              joints[i].minAngle, joints[i].maxAngle);
            }

            Debug.LogWarning($"[Sim {gameObject.name}] 🛑 비상정지 — 현재 자세로 고정");
        }

        /// <summary>비상정지 해제.</summary>
        public void ReleaseStop()
        {
            if (!EmergencyStopped) return;
            EmergencyStopped = false;
            Debug.Log($"[Sim {gameObject.name}] ▶ 비상정지 해제");
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
