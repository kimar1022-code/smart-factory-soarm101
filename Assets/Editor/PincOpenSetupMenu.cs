using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Robotics.UrdfImporter;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// so101.urdf 재임포트 + PincOpenCoupling 자동 설정을 메뉴 하나로 묶은 도구.
    ///
    /// URDF 파일을 고쳐도 씬에 이미 놓인 로봇은 안 바뀐다.
    /// URDF는 설계도일 뿐이고, 씬의 GameObject는 예전에 그 설계도로 찍어낸 결과물이라서.
    /// 그래서 새로 찍어내는 과정이 필요하다.
    /// </summary>
    public static class PincOpenSetupMenu
    {
        const string UrdfAssetPath = "Assets/SO101_unity/so101.urdf";

        [MenuItem("Tools/SO-ARM/PincOpen 로봇 재임포트", false, 100)]
        public static void ReimportRobot()
        {
            string fullPath = Path.GetFullPath(UrdfAssetPath);

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[PincOpen] URDF를 못 찾음: {fullPath}");
                return;
            }

            // ⚠️ 기본값이 vHACD 인데, 이 프로젝트에서 NullReference 크래시를 냈던 방식이다.
            //    unity 디컴포저로 강제한다. (URDF의 collision 은 어차피 전부 주석 처리 상태)
            var settings = new ImportSettings
            {
                chosenAxis = ImportSettings.axisType.yAxis,
                convexMethod = ImportSettings.convexDecomposer.unity,
            };

            Debug.Log($"[PincOpen] 임포트 시작 — {UrdfAssetPath} (디컴포저: unity)");

            GameObject robot = null;
            var pipeline = UrdfRobotExtensions.Create(fullPath, settings, loadStatus: false);
            while (pipeline.MoveNext())
            {
                if (pipeline.Current != null) robot = pipeline.Current;
            }
            if (robot == null) robot = pipeline.Current;

            if (robot == null)
            {
                Debug.LogError("[PincOpen] 임포트 실패 — 로봇 오브젝트가 생성되지 않음. Console 위쪽 에러 확인 필요.");
                return;
            }

            robot.name = "Robot_PincOpen";
            SetupCoupling(robot);

            Selection.activeGameObject = robot;
            EditorGUIUtility.PingObject(robot);

            Debug.Log($"[PincOpen] 완료 — '{robot.name}' 생성됨. Hierarchy에서 선택해뒀습니다.");
        }

        /// <summary>
        /// 새 씬에 PincOpen 로봇만 임포트해서 저장한다.
        /// 작업 중인 LeRobot.unity 는 건드리지 않는 미리보기 전용.
        /// 커맨드라인(-executeMethod)에서도 호출된다.
        /// </summary>
        [MenuItem("Tools/SO-ARM/PincOpen 미리보기 씬 만들기", false, 103)]
        public static void BuildPreviewScene()
        {
            const string previewScenePath = "Assets/Scenes/PincOpen_Preview.unity";

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            ReimportRobot();

            var robot = GameObject.Find("Robot_PincOpen");
            if (robot == null)
            {
                Debug.LogError("[PincOpen] 미리보기 씬 생성 실패 — 로봇이 만들어지지 않았습니다.");
                return;
            }

            // 카메라를 그리퍼 쪽으로 향하게 (씬 열자마자 보이도록)
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0.35f, 0.35f, 0.35f);
                cam.transform.LookAt(robot.transform.position + Vector3.up * 0.15f);
                cam.nearClipPlane = 0.01f;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, previewScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[PincOpen] 미리보기 씬 저장 완료 — {previewScenePath}");
        }

        [MenuItem("Tools/SO-ARM/선택한 로봇에 PincOpenCoupling 설정", false, 101)]
        public static void SetupCouplingOnSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogError("[PincOpen] Hierarchy에서 로봇 루트를 먼저 선택하세요.");
                return;
            }
            SetupCoupling(go);
        }

        static void SetupCoupling(GameObject robot)
        {
            var coupling = robot.GetComponent<PincOpenCoupling>();
            if (coupling == null) coupling = robot.AddComponent<PincOpenCoupling>();

            coupling.AutoBind();

            bool ok = coupling.mountTarget != null
                   && coupling.pincopenBase != null
                   && coupling.driveJoint != null
                   && coupling.leftDistal != null
                   && coupling.rightProximal != null
                   && coupling.rightDistal != null;

            if (ok)
                Debug.Log("[PincOpen] 커플링 연결 성공 — 5개 링크 전부 찾음.");
            else
                Debug.LogWarning("[PincOpen] 커플링 연결 불완전. 위 자동 연결 로그에서 False인 항목을 인스펙터에서 직접 드래그하세요.");

            EditorUtility.SetDirty(coupling);
        }

        /// <summary>씬의 SOArmSimController J6이 아직 옛 moving_jaw를 보고 있는지 검사만 한다. 바꾸지는 않음.</summary>
        [MenuItem("Tools/SO-ARM/J6 슬롯 점검 (그리퍼 연결 확인)", false, 102)]
        public static void CheckGripperSlot()
        {
            var sims = Object.FindObjectsByType<SOArmSimController>(FindObjectsInactive.Include);
            if (sims.Length == 0)
            {
                Debug.LogWarning("[PincOpen] 씬에 SOArmSimController가 없습니다.");
                return;
            }

            foreach (var sim in sims)
            {
                if (sim.joints == null || sim.joints.Length == 0)
                {
                    Debug.LogWarning($"[PincOpen] {sim.name}: joints 배열이 비어있음.");
                    continue;
                }

                int last = sim.joints.Length - 1;
                var ab = sim.joints[last].articulationBody;
                string target = ab != null ? ab.name : "(비어있음)";

                if (ab == null)
                    Debug.LogWarning($"[PincOpen] {sim.name}: J{last} 슬롯이 비어있습니다.");
                else if (target.Contains("moving_jaw"))
                    Debug.LogWarning($"[PincOpen] {sim.name}: J{last}가 아직 옛 그리퍼 '{target}' 를 가리킵니다. " +
                                     "→ pincopen_left_proximal_link 로 바꿔야 그리퍼 슬라이더가 먹습니다.");
                else if (target.Contains("pincopen"))
                    Debug.Log($"[PincOpen] {sim.name}: J{last} → '{target}' ✅ 정상");
                else
                    Debug.LogWarning($"[PincOpen] {sim.name}: J{last} → '{target}' — 예상 밖의 오브젝트입니다. 확인 필요.");
            }
        }
    }
}
