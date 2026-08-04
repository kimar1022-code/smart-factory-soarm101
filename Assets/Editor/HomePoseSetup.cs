using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SOArmControl;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// 씬에 직렬화된 홈 포즈를 한 번에 맞춰 주는 도구.
    ///
    /// 왜 필요한가: 컨트롤러의 <c>joints</c> 는 public 직렬화 필드라 씬에 값이 이미 박혀 있고,
    /// <c>Awake()</c> 의 <c>SOArmPresets</c> fallback 은 배열이 비어 있을 때만 탄다.
    /// 즉 프리셋 상수를 고쳐도 기존 씬에는 아무 일도 일어나지 않는다.
    /// 홈이 컨트롤러 4개(R1/R2 × 시뮬/실물)에 흩어져 있어 손으로 고치면 어긋나기 쉽다.
    /// </summary>
    public static class HomePoseSetup
    {
        // 2026-08-04 확정 홈 — 두 로봇의 리밋 안에서 가장 접힌 자세.
        // elbow 64° 는 R2 의 소프트 리밋 상한이다(R1 은 70° 까지 되지만 두 팔을 같은
        // 자세로 세우려고 낮은 쪽에 맞췄다). 그리퍼는 손대지 않는다 — 홈=열림이 맞다.
        private static readonly (string motor, float angle)[] HomePose =
        {
            ("shoulder_pan",    0f),
            ("shoulder_lift", -90f),
            ("elbow_flex",     64f),
            ("wrist_flex",    -80f),
            ("wrist_roll",      0f),
        };

        [MenuItem("SO-ARM/홈 포즈 적용 (접힌 자세)")]
        public static void Apply()
        {
            int touched = 0, warned = 0;

            foreach (var real in Object.FindObjectsByType<SOArmRealController>(
                         FindObjectsInactive.Include))
                touched += ApplyTo(real, real.joints, ref warned);

            foreach (var sim in Object.FindObjectsByType<SOArmSimController>(
                         FindObjectsInactive.Include))
                touched += ApplyTo(sim, sim.joints, ref warned);

            if (touched == 0)
            {
                Debug.LogWarning("[홈 포즈] 대상 컨트롤러를 못 찾았다. 씬이 열려 있는지 확인할 것.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[홈 포즈] 컨트롤러 {touched}개 적용 완료 — 씬을 저장해야 반영된다." +
                      (warned > 0 ? $" (리밋 경고 {warned}건, 위 로그 확인)" : ""));
        }

        /// <returns>이 컨트롤러를 건드렸으면 1, 아니면 0</returns>
        private static int ApplyTo(Object owner, SOArmJointConfig[] joints, ref int warned)
        {
            if (joints == null || joints.Length == 0) return 0;

            Undo.RecordObject(owner, "홈 포즈 적용");

            foreach (var (motor, angle) in HomePose)
            {
                foreach (var j in joints)
                {
                    if (j == null || j.motorName != motor) continue;

                    // 리밋을 넘는 값을 넣으면 홈 버튼이 클램프된 다른 자세로 가 버린다.
                    // 조용히 자르지 말고 어느 관절이 문제인지 남긴다.
                    if (angle < j.ClampMin || angle > j.ClampMax)
                    {
                        Debug.LogWarning(
                            $"[홈 포즈] {owner.name} / {motor}: {angle}° 는 명령 가능 범위 " +
                            $"({j.ClampMin}~{j.ClampMax}) 밖이다. 홈으로 가면 잘린 값이 적용된다.");
                        warned++;
                    }

                    j.homeAngle = angle;
                }
            }

            EditorUtility.SetDirty(owner);
            return 1;
        }
    }
}
