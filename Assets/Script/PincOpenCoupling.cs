using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// PincOpen 그리퍼 커플링 + 장착 오프셋 조정기.
    ///
    /// PincOpen은 평행 4절 링크라 실제 구동축이 1개(모터 ID 6)뿐이고
    /// 나머지 3축은 기계적으로 따라 움직인다. URDF는 이런 폐루프 구속을
    /// 표현할 수 없으므로 여기서 스크립트로 미러링한다.
    ///
    /// 펌웨어로 치면, 하드웨어가 알아서 해주던 종동 관계를
    /// 소프트웨어가 대신 계산해주는 것.
    ///
    /// ✅ 배율 확정: ×1.0 (MJCF_Full), 부호 (-1, -1, +1). 2026-08-01
    ///
    /// ROS2 ×0.5 와 MuJoCo ×1.0 은 모순이 아니라 **기준 관절이 다른 것**이었다.
    ///   · ROS2 xacro 는 네 관절 전부를 "모터축" 기준으로 ×±0.5 로 적는다.
    ///   · 우리 URDF 의 구동축은 모터축이 아니라 왼쪽 proximal
    ///     (= ROS2 의 base_link_to_left_arm)이고, 이 관절 자체가 이미 모터의 -0.5 배다.
    ///   · 모터 M = -2θ 로 환산하면
    ///        left_distal    = +0.5M = -θ
    ///        right_proximal = +0.5M = -θ
    ///        right_distal   = -0.5M = +θ     → 즉 ×1.0 의 (-1, -1, +1)
    ///
    /// 렌더링으로도 확인: ×1.0 에서만 손가락 패드가 서로 **평행**하게 맞물린다.
    /// PincOpen 은 평행 4절 링크이므로 이게 정답. ×0.5 는 끝만 뾰족하게 모인다.
    /// </summary>
    [ExecuteAlways]
    public class PincOpenCoupling : MonoBehaviour
    {
        public enum CouplingPreset
        {
            MJCF_Full,   // ×1.0 — ✅ 확정값 (2026-08-01). 아래 설명 참조
            ROS2_Half,   // ×0.5 — 모터 기준 배율. 우리 구조에는 틀림
            Custom,      // 아래 배율을 직접 입력
        }

        // ══════════════════════════════════════════════════════════════
        //  가동범위 — 출처 표기된 값만 사용. 임의로 바꾸지 말 것.
        // ══════════════════════════════════════════════════════════════

        /// <summary>모터 펌웨어 하드 리밋. 공식 노트북 set_min_angle_limit(-147).</summary>
        public const float MotorHardLimitDeg = -147f;
        /// <summary>모터 열림 위치. 공식 노트북 set_goal_position(-140).</summary>
        public const float MotorOpenDeg = -140f;
        /// <summary>모터 닫힘 위치. 공식 노트북 set_goal_position(0).</summary>
        public const float MotorClosedDeg = 0f;

        /// <summary>
        /// 손가락(구동축) 닫힘 각. **메시 실측으로 확정** (2026-08-01).
        ///
        /// 메시 정점을 base 로컬좌표로 변환해 좌우 손가락 최단거리를 잰 결과,
        /// -48.9° 에서 두 패드가 정확히 맞닿는다. 여기에 0.4° 여유를 둔 값.
        ///
        /// ⚠️ ROS2 xacro 의 1.22 rad(69.9°) 를 그대로 쓰면 손가락이 22mm 파고든다.
        ///    그 값은 캠을 포함한 실제 4절 링크를 모터축 기준으로 잰 것이라
        ///    MuJoCo 메시의 0점(= 벌어진 자세)과 기준이 다르다.
        ///    MJCF 의 0.77 rad(44.1°) 가 이 실측치(48.9°)에 가깝다.
        /// </summary>
        public const float FingerClosedDeg = -48.5f;

        /// <summary>
        /// 손가락 열림 각. MuJoCo 메시의 기본 자세(rest pose)이며 이때 개구부 약 66mm.
        /// 양수로 더 벌릴 수는 있으나 실물 기계 한계를 몰라 0 을 상한으로 둔다.
        /// </summary>
        public const float FingerOpenDeg = 0f;

        [Header("── 구동축 (URDF 'gripper' 조인트 = 모터 ID 6) ──")]
        [Tooltip("pincopen_left_proximal_link 의 ArticulationBody")]
        public ArticulationBody driveJoint;

        [Header("── 종동축 3개 ──")]
        [Tooltip("pincopen_left_distal_link")]
        public ArticulationBody leftDistal;
        [Tooltip("pincopen_right_proximal_link")]
        public ArticulationBody rightProximal;
        [Tooltip("pincopen_right_distal_link")]
        public ArticulationBody rightDistal;

        [Header("── 커플링 배율 ──")]
        public CouplingPreset preset = CouplingPreset.MJCF_Full;

        [Tooltip("preset 이 Custom 일 때만 사용")]
        public float leftDistalRatio = -0.5f;
        public float rightProximalRatio = -0.5f;
        public float rightDistalRatio = 0.5f;

        [Header("── 장착 오프셋 (URDF pincopen_mount origin 대용) ──")]
        [Tooltip("조정 대상 = Interface_ARM100 어댑터 링크. " +
                 "어댑터가 움직이면 PincOpen 본체는 자식이라 같이 따라온다.")]
        public ArticulationBody mountTarget;

        [Tooltip("PincOpen 본체 링크 (참조용, 오프셋 조정 대상 아님)")]
        public ArticulationBody pincopenBase;

        /// <summary>
        /// ⚠️ 기본값 false. 켜면 아래 값이 매 프레임 URDF 의 조인트 원점을 덮어쓴다.
        ///
        /// 2026-08-01 에 장착 위치를 URDF(pincopen_mount)에 확정해 넣었으므로
        /// 평소에는 꺼둔 채로 URDF 를 단일 진실 공급원으로 쓴다.
        /// 위치를 다시 손으로 맞춰볼 때만 잠깐 켤 것.
        /// (켠 채 값이 0 이면 그리퍼가 손목 원점으로 튄다)
        /// </summary>
        [Tooltip("⚠️ 켜면 URDF 의 pincopen_mount origin 을 무시하고 아래 값으로 덮어쓴다. " +
                 "위치를 다시 조정할 때만 켤 것.")]
        public bool applyMountOffset = false;

        [Tooltip("Unity 좌표계 기준 (m). applyMountOffset 이 켜져 있을 때만 의미 있음")]
        public Vector3 mountPosition = Vector3.zero;
        public Vector3 mountRotationEuler = Vector3.zero;

        // ── 현재 적용 중인 배율 ──────────────────────────────
        float LeftDistalRatio => preset switch
        {
            CouplingPreset.ROS2_Half => -0.5f,
            CouplingPreset.MJCF_Full => -1.0f,
            _ => leftDistalRatio,
        };

        float RightProximalRatio => preset switch
        {
            CouplingPreset.ROS2_Half => -0.5f,
            CouplingPreset.MJCF_Full => -1.0f,
            _ => rightProximalRatio,
        };

        float RightDistalRatio => preset switch
        {
            CouplingPreset.ROS2_Half => 0.5f,
            CouplingPreset.MJCF_Full => 1.0f,
            _ => rightDistalRatio,
        };

        [Header("── 관절 구동 파라미터 ──")]
        [Tooltip("URDF 임포트 직후에는 stiffness 가 0 이라 관절이 전혀 안 움직인다. " +
                 "켜두면 시작할 때 자동으로 채워준다.")]
        public bool autoConfigureDrives = true;
        public float stiffness = 10000f;
        public float damping = 1000f;
        public float forceLimit = 1000f;

        void Start()
        {
            if (autoConfigureDrives) ConfigureDrives();
        }

        void LateUpdate()
        {
            if (applyMountOffset) ApplyMountOffset();
            ApplyCoupling();
        }

        /// <summary>
        /// 5개 관절의 xDrive 를 실제로 움직일 수 있는 상태로 만든다.
        ///
        /// URDF 임포터는 limit 만 채우고 stiffness/damping 을 0 으로 두는 경우가 있는데,
        /// 그러면 target 을 아무리 바꿔도 관절이 미동도 하지 않는다.
        /// (펌웨어로 치면 레지스터 값만 쓰고 토크를 안 켠 상태)
        /// </summary>
        [ContextMenu("관절 구동 파라미터 설정")]
        public void ConfigureDrives()
        {
            int n = 0;
            n += Configure(driveJoint, FingerClosedDeg, FingerOpenDeg);
            n += Configure(leftDistal, -Mathf.Abs(FingerClosedDeg), Mathf.Abs(FingerClosedDeg));
            n += Configure(rightProximal, -Mathf.Abs(FingerClosedDeg), Mathf.Abs(FingerClosedDeg));
            n += Configure(rightDistal, -Mathf.Abs(FingerClosedDeg), Mathf.Abs(FingerClosedDeg));
            Debug.Log($"[PincOpen] 관절 {n}개 구동 설정 완료 " +
                      $"(stiffness={stiffness}, 구동축 범위 {FingerClosedDeg:F1}°~{FingerOpenDeg:F1}°)");
        }

        int Configure(ArticulationBody ab, float lower, float upper)
        {
            if (ab == null) return 0;
            var d = ab.xDrive;
            d.stiffness = stiffness;
            d.damping = damping;
            d.forceLimit = forceLimit;
            d.lowerLimit = Mathf.Min(lower, upper);
            d.upperLimit = Mathf.Max(lower, upper);
            d.target = Mathf.Clamp(d.target, d.lowerLimit, d.upperLimit);
            ab.xDrive = d;

#if UNITY_EDITOR
            // 에디터에서 호출됐다면 씬에 저장되도록 변경 표시를 남긴다.
            // 이걸 빼면 Play 를 눌러야만 값이 채워져서, 에디트 모드에서는
            // stiffness=0 / limit 0~0 인 채로 관절이 잠겨 보인다.
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(ab);
#endif
            return 1;
        }

        // ── 외부에서 그리퍼를 여닫는 정식 통로 ──────────────────────

        /// <summary>
        /// 0 = 완전히 닫힘, 100 = 완전히 열림.
        /// 검증된 손가락 범위 밖으로는 절대 나가지 않는다.
        /// </summary>
        public void SetGripperPercent(float percent)
        {
            percent = Mathf.Clamp(percent, 0f, 100f);
            float angle = Mathf.Lerp(FingerClosedDeg, FingerOpenDeg, percent * 0.01f);
            SetDriveAngle(angle);
        }

        public float GetGripperPercent()
        {
            if (driveJoint == null) return 0f;
            float t = Mathf.InverseLerp(FingerClosedDeg, FingerOpenDeg, driveJoint.xDrive.target);
            return t * 100f;
        }

        /// <summary>구동축을 각도로 직접 지정. 범위를 벗어나면 잘라낸다.</summary>
        public void SetDriveAngle(float deg)
        {
            if (driveJoint == null) return;

            float lo = Mathf.Min(FingerClosedDeg, FingerOpenDeg);
            float hi = Mathf.Max(FingerClosedDeg, FingerOpenDeg);
            float safe = Mathf.Clamp(deg, lo, hi);

            if (!Mathf.Approximately(safe, deg))
            {
                Debug.LogWarning($"[PincOpen] 구동각 {deg:F1}° 는 허용 범위 " +
                                 $"({lo:F1}° ~ {hi:F1}°) 밖이라 {safe:F1}° 로 제한했습니다.");
            }

            var d = driveJoint.xDrive;
            d.target = safe;
            driveJoint.xDrive = d;

            ApplyCoupling();
        }

        /// <summary>
        /// 커플링을 지금 한 번 적용한다.
        /// 에디터 배치 모드처럼 LateUpdate 가 돌지 않는 환경에서 테스트할 때 쓴다.
        /// </summary>
        public void ApplyCouplingNow() => ApplyCoupling();

        /// <summary>구동축 목표각을 배율대로 종동축 3개에 복사.</summary>
        void ApplyCoupling()
        {
            if (driveJoint == null) return;

            float drive = driveJoint.xDrive.target;   // degree

            SetDriveTarget(leftDistal, drive * LeftDistalRatio);
            SetDriveTarget(rightProximal, drive * RightProximalRatio);
            SetDriveTarget(rightDistal, drive * RightDistalRatio);
        }

        static void SetDriveTarget(ArticulationBody ab, float targetDeg)
        {
            if (ab == null) return;
            var d = ab.xDrive;
            d.target = Mathf.Clamp(targetDeg, d.lowerLimit, d.upperLimit);
            ab.xDrive = d;
        }

        /// <summary>
        /// 고정 조인트의 앵커를 움직여서 그리퍼 장착 위치를 조정한다.
        /// Transform 을 직접 건드리면 물리엔진이 되돌리므로 앵커를 쓴다.
        /// </summary>
        void ApplyMountOffset()
        {
            var t = mountTarget != null ? mountTarget : pincopenBase;
            if (t == null) return;
            t.parentAnchorPosition = mountPosition;
            t.parentAnchorRotation = Quaternion.Euler(mountRotationEuler);
        }

        /// <summary>
        /// 눈으로 맞춘 오프셋을 URDF 에 굽기 위해 좌표 변환해서 출력.
        /// Unity(왼손·Y-up) → URDF(오른손·Z-up):  x_urdf = z_unity, y_urdf = -x_unity, z_urdf = y_unity
        /// ⚠️ 회전(rpy)은 변환이 까다로워 참고값이다. 반영 후 반드시 눈으로 재확인할 것.
        /// </summary>
        [ContextMenu("현재 오프셋을 URDF 형식으로 출력")]
        public void LogUrdfOrigin()
        {
            Vector3 p = mountPosition;
            Vector3 r = mountRotationEuler * Mathf.Deg2Rad;

            string xyz = $"{p.z:F6} {-p.x:F6} {p.y:F6}";
            string rpy = $"{r.z:F6} {-r.x:F6} {r.y:F6}";

            Debug.Log(
                "[PincOpen] so101.urdf 의 pincopen_mount 에 아래 줄을 넣으세요:\n" +
                $"    <origin xyz=\"{xyz}\" rpy=\"{rpy}\"/>\n" +
                "    ※ rpy 는 참고값입니다. 반영 후 씬에서 방향을 다시 확인하세요.\n" +
                "    ※ URDF 에 넣은 뒤에는 applyMountOffset 을 반드시 다시 끄세요. " +
                "켜둔 채로 두면 URDF 값을 계속 덮어씁니다.");
        }

        /// <summary>인스펙터에서 참조를 일일이 드래그하기 번거로울 때 이름으로 자동 연결.</summary>
        [ContextMenu("자식에서 링크 자동 연결")]
        public void AutoBind()
        {
            foreach (var ab in GetComponentsInChildren<ArticulationBody>(true))
            {
                switch (ab.name)
                {
                    case "pincopen_adapter_link": mountTarget = ab; break;
                    case "pincopen_base_link": pincopenBase = ab; break;
                    case "pincopen_left_proximal_link": driveJoint = ab; break;
                    case "pincopen_left_distal_link": leftDistal = ab; break;
                    case "pincopen_right_proximal_link": rightProximal = ab; break;
                    case "pincopen_right_distal_link": rightDistal = ab; break;
                }
            }

            Debug.Log($"[PincOpen] 자동 연결 결과 — adapter:{(mountTarget != null)} " +
                      $"base:{(pincopenBase != null)} " +
                      $"drive:{(driveJoint != null)} lDistal:{(leftDistal != null)} " +
                      $"rProx:{(rightProximal != null)} rDistal:{(rightDistal != null)}");
        }
    }
}
