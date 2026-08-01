using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// 팔꿈치 안전 리밋을 **소프트 리밋**으로 옮긴다.
    ///
    /// 【이전 시도가 왜 틀렸나】
    ///   min/maxAngle 을 좁혀서 안전 한계를 걸었는데, 이 두 값은 사실
    ///   서버 정규화값(-100~100)과 각도를 잇는 '자' 역할을 겸한다.
    ///
    ///       angle = Lerp(minAngle, maxAngle, (norm + 100) / 200)
    ///
    ///   maxAngle 을 96.8 → 70 으로 줄이자 norm=0 이 0° 가 아니라
    ///   (-96.8 + 70)/2 = -13.4° 로 환산되기 시작했다.
    ///   즉 **모든 각도가 통째로 밀려서** 애써 맞춘 캘리브레이션이 도로 어긋났다.
    ///
    /// 【정정】
    ///   min/maxAngle 은 원래대로(-96.8 ~ 96.8) 되돌려 환산을 정상화하고,
    ///   명령 제한은 별도 필드(softMin/softMaxAngle)로 건다.
    ///   ClampMin/ClampMax 만 이 값을 보고, 환산 공식은 건드리지 않는다.
    /// </summary>
    public static class JointLimitFixer
    {
        const string ScenePath = "Assets/Scenes/LeRobot.unity";
        const int ElbowIndex = 2;   // J3

        // 정규화 기준 (URDF 공식값) — 반드시 원래대로
        const float ElbowMin = -96.8f;
        const float ElbowMax = 96.8f;

        // 기계적으로 실제 도달 가능한 상한 (라파 캘리브레이션 실측에서 계산)
        //   robot1: 기계한계 Present 522~2815 → norm +76.2 → 73.8° → 여유 두고 70
        //   robot2: 기계한계 Present 519~2755 → norm +70.3 → 68.1° → 여유 두고 64
        const float Robot1SoftMax = 70f;
        const float Robot2SoftMax = 64f;

        [MenuItem("Tools/SO-ARM/팔꿈치 리밋 → 소프트 리밋으로 이전", false, 130)]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var managers = Object.FindObjectsByType<SOArmManager>(FindObjectsInactive.Include);
            if (managers.Length == 0) { Debug.LogError("[Limit] SOArmManager 없음"); return; }

            int fixedCount = 0;
            foreach (var mgr in managers)
            {
                string mode = mgr.real != null ? mgr.real.robotServerMode : null;
                if (string.IsNullOrEmpty(mode))
                {
                    Debug.LogWarning($"[Limit] {mgr.name}: robotServerMode 없음 — 건너뜀");
                    continue;
                }

                float softMax = mode == "robot1" ? Robot1SoftMax
                              : mode == "robot2" ? Robot2SoftMax
                              : float.NaN;
                if (float.IsNaN(softMax))
                {
                    Debug.LogWarning($"[Limit] 알 수 없는 mode '{mode}' — 건너뜀");
                    continue;
                }

                fixedCount += Fix(mgr.sim != null ? mgr.sim.joints : null, softMax, mode, "Sim");
                fixedCount += Fix(mgr.real != null ? mgr.real.joints : null, softMax, mode, "Real");

                if (mgr.sim != null) EditorUtility.SetDirty(mgr.sim);
                if (mgr.real != null) EditorUtility.SetDirty(mgr.real);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Limit] 완료 — {fixedCount}개 슬롯 정정 후 저장");
        }

        static int Fix(SOArmJointConfig[] joints, float softMax, string mode, string kind)
        {
            if (joints == null || joints.Length <= ElbowIndex) return 0;
            var j = joints[ElbowIndex];

            float oldMin = j.minAngle, oldMax = j.maxAngle;

            // 1) 환산 기준 원복
            j.minAngle = ElbowMin;
            j.maxAngle = ElbowMax;

            // 2) 안전 제한은 소프트 리밋으로
            j.useSoftLimit = true;
            j.softMinAngle = ElbowMin;
            j.softMaxAngle = softMax;

            Debug.Log($"[Limit] {mode} {kind} J3: 환산범위 {oldMin:F1}~{oldMax:F1} → {ElbowMin:F1}~{ElbowMax:F1} (원복), " +
                      $"소프트리밋 {ElbowMin:F1}~{softMax:F1} 적용");
            return 1;
        }
    }
}
