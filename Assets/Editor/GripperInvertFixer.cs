using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// 그리퍼 방향 반전을 **로봇별로** 설정한다.
    ///
    /// 【왜 로봇별인가】
    ///   실측 결과 두 그리퍼의 장착 방향이 서로 반대다.
    ///     robot1 : 모터 raw 가 높을수록 **닫힘**  (raw 807=열림, 3364=닫힘)
    ///     robot2 : 모터 raw 가 높을수록 **열림**  (raw 431=닫힘, 3322=열림)
    ///   RANGE_M100_100 은 range_min 이 항상 -100 이므로
    ///     robot1: -100=열림 → 우리 약속(percent 0=닫힘 → -100)과 반대 → 반전 필요
    ///     robot2: -100=닫힘 → 그대로 맞음 → 반전 불필요
    ///
    ///   전역 static 플래그 하나로는 둘 다 맞출 수 없어서
    ///   관절별 invertSign 으로 옮겼다.
    /// </summary>
    public static class GripperInvertFixer
    {
        const string ScenePath = "Assets/Scenes/LeRobot.unity";

        [MenuItem("Tools/SO-ARM/그리퍼 반전 로봇별 설정", false, 131)]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            int n = 0;
            foreach (var mgr in Object.FindObjectsByType<SOArmManager>(FindObjectsInactive.Include))
            {
                string mode = mgr.real != null ? mgr.real.robotServerMode : null;
                if (string.IsNullOrEmpty(mode)) continue;

                bool invert = mode == "robot1";   // robot1 만 반전
                n += Set(mgr.real != null ? mgr.real.joints : null, invert, mode, "Real");

                // Sim 쪽은 각도를 그대로 그리므로 반전하면 안 된다
                n += Set(mgr.sim != null ? mgr.sim.joints : null, false, mode, "Sim");

                if (mgr.real != null) EditorUtility.SetDirty(mgr.real);
                if (mgr.sim != null) EditorUtility.SetDirty(mgr.sim);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GripInvert] 완료 — {n}개 슬롯 설정 후 저장");
        }

        static int Set(SOArmJointConfig[] joints, bool invert, string mode, string kind)
        {
            if (joints == null || joints.Length == 0) return 0;
            int last = joints.Length - 1;      // J6 = 그리퍼
            if (joints[last].motorName != "gripper") return 0;

            joints[last].invertSign = invert;
            Debug.Log($"[GripInvert] {mode} {kind}: 그리퍼 invertSign = {invert}");
            return 1;
        }
    }
}
