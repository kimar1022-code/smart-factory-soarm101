using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Robotics.UrdfImporter;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// 메인 씬(LeRobot.unity)의 로봇에서 순정 그리퍼를 떼고 PincOpen 을 이식한다.
    ///
    /// 로봇을 통째로 재임포트하면 SOArmManager·SocketClient·UI 의 인스펙터 연결이
    /// 전부 끊어진다. 그래서 **그리퍼 subtree 만 교체**하는 방식을 쓴다.
    ///   1) 임시로 새 URDF 로봇을 하나 임포트해서 PincOpen 부품을 확보
    ///   2) 기존 로봇의 moving_jaw 링크를 제거
    ///   3) PincOpen subtree 를 기존 gripper_link 밑으로 이식 (로컬 변환 그대로)
    ///   4) J6 슬롯 재연결 + 범위 갱신 + 커플링 부착
    ///   5) 임시 로봇 삭제
    /// </summary>
    public static class PincOpenMainSceneMigrator
    {
        const string MainScenePath = "Assets/Scenes/LeRobot.unity";
        const string UrdfAssetPath = "Assets/SO101_unity/so101.urdf";

        [MenuItem("Tools/SO-ARM/메인 씬에 PincOpen 이식", false, 120)]
        public static void Migrate()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            // ── 1. 기준이 될 PincOpen 부품을 임시 로봇에서 확보 ──────────
            GameObject temp = ImportTempRobot();
            if (temp == null) return;

            var srcAdapter = FindDeep(temp.transform, "pincopen_adapter_link");
            if (srcAdapter == null)
            {
                Debug.LogError("[Migrate] 임시 로봇에 pincopen_adapter_link 가 없습니다. URDF 확인 필요.");
                Object.DestroyImmediate(temp);
                return;
            }

            // ── 2. 씬의 로봇들을 찾는다 ──────────────────────────────────
            var sims = Object.FindObjectsByType<SOArmSimController>(FindObjectsInactive.Include);
            Debug.Log($"[Migrate] 씬에서 SOArmSimController {sims.Length}개 발견");

            int done = 0;
            foreach (var sim in sims)
            {
                if (MigrateOne(sim, srcAdapter)) done++;
            }

            Object.DestroyImmediate(temp);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Migrate] 완료 — 로봇 {done}대에 PincOpen 이식 및 저장");
        }

        static bool MigrateOne(SOArmSimController sim, Transform srcAdapter)
        {
            string robotName = sim.gameObject.name;

            var gripperLink = FindDeep(sim.transform, "gripper_link");
            if (gripperLink == null)
            {
                Debug.LogWarning($"[Migrate] {robotName}: gripper_link 를 못 찾아 건너뜁니다.");
                return false;
            }

            // 이미 이식돼 있으면 중복 생성하지 않는다
            var existing = FindDeep(sim.transform, "pincopen_adapter_link");
            if (existing != null)
            {
                Debug.Log($"[Migrate] {robotName}: 이미 PincOpen 이 있어 이식은 건너뜁니다. 배선만 갱신합니다.");
            }
            else
            {
                // ── 순정 그리퍼 제거 ──
                var jaw = FindDeep(sim.transform, "moving_jaw_so101_v1_link");
                if (jaw != null)
                {
                    Debug.Log($"[Migrate] {robotName}: 순정 '{jaw.name}' 제거");
                    Object.DestroyImmediate(jaw.gameObject);
                }

                // ── 순정 그리퍼 모터 시각 메시 제거 (PincOpen base 에 중복 포함) ──
                RemoveVisualChild(gripperLink, "sts3215_03a_v1");
                RemoveVisualChild(gripperLink, "wrist_roll_follower_so101_v1");

                // ── PincOpen 이식 (로컬 변환을 원본 그대로 복사) ──
                var copy = Object.Instantiate(srcAdapter.gameObject, gripperLink);
                copy.name = srcAdapter.name;
                copy.transform.localPosition = srcAdapter.localPosition;
                copy.transform.localRotation = srcAdapter.localRotation;
                copy.transform.localScale = srcAdapter.localScale;
                StripCloneSuffix(copy.transform);

                Debug.Log($"[Migrate] {robotName}: PincOpen 이식 완료 " +
                          $"(localPos={copy.transform.localPosition}, localRot={copy.transform.localEulerAngles})");
            }

            // ── J6 슬롯 재연결 + 범위 갱신 ──
            var drive = FindDeep(sim.transform, "pincopen_left_proximal_link");
            if (drive == null)
            {
                Debug.LogError($"[Migrate] {robotName}: 구동축 링크를 못 찾았습니다.");
                return false;
            }

            var driveAb = drive.GetComponent<ArticulationBody>();
            RewireGripperSlot(sim.joints, driveAb, robotName, "Sim");

            // 같은 로봇의 Real 컨트롤러도 범위를 맞춘다 (Sim↔Real 매핑 일치 필수)
            var manager = FindManagerFor(sim);
            if (manager != null && manager.real != null)
                RewireGripperSlot(manager.real.joints, null, robotName, "Real");

            // ── 커플링 부착 ──
            var coupling = sim.GetComponent<PincOpenCoupling>();
            if (coupling == null) coupling = sim.gameObject.AddComponent<PincOpenCoupling>();
            coupling.AutoBind();
            // ⚠️ 이미 씬에 저장된 컴포넌트는 옛 값(ROS2_Half)을 들고 있을 수 있다.
            //    ×0.5 면 종동축이 절반만 움직여 좌우가 비대칭으로 닫힌다.
            coupling.preset = PincOpenCoupling.CouplingPreset.MJCF_Full;
            coupling.ConfigureDrives();
            coupling.applyMountOffset = false;   // URDF 가 단일 진실 공급원
            EditorUtility.SetDirty(coupling);
            EditorUtility.SetDirty(sim);

            // ArticulationBody.xDrive 는 코드로 넣어도 씬 파일에 안 남는다.
            // (네이티브 쪽만 바뀌고 직렬화 필드는 그대로) → SerializedObject 로 직접 쓴다.
            PersistDrive(coupling.driveJoint,
                         PincOpenCoupling.FingerClosedDeg, PincOpenCoupling.FingerOpenDeg, coupling);
            float f = Mathf.Abs(PincOpenCoupling.FingerClosedDeg);
            PersistDrive(coupling.leftDistal, -f, f, coupling);
            PersistDrive(coupling.rightProximal, -f, f, coupling);
            PersistDrive(coupling.rightDistal, -f, f, coupling);

            return true;
        }

        /// <summary>xDrive 설정을 씬 파일에 실제로 남긴다.</summary>
        static void PersistDrive(ArticulationBody ab, float lower, float upper, PincOpenCoupling c)
        {
            if (ab == null) return;

            var so = new SerializedObject(ab);
            Set(so, "m_XDrive.stiffness", c.stiffness);
            Set(so, "m_XDrive.damping", c.damping);
            Set(so, "m_XDrive.forceLimit", c.forceLimit);
            Set(so, "m_XDrive.lowerLimit", Mathf.Min(lower, upper));
            Set(so, "m_XDrive.upperLimit", Mathf.Max(lower, upper));
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[Migrate]   {ab.name} 드라이브 저장 " +
                      $"(stiffness={c.stiffness}, limit {Mathf.Min(lower, upper):F1}~{Mathf.Max(lower, upper):F1})");
        }

        static void Set(SerializedObject so, string path, float v)
        {
            var p = so.FindProperty(path);
            if (p != null) p.floatValue = v;
            else Debug.LogWarning($"[Migrate]   직렬화 경로 없음: {path}");
        }

        static void RewireGripperSlot(SOArmJointConfig[] joints, ArticulationBody drive,
                                      string robotName, string kind)
        {
            if (joints == null || joints.Length == 0) return;
            int last = joints.Length - 1;

            joints[last].displayName = "J6 (Gripper / PincOpen)";
            joints[last].motorName = "gripper";
            joints[last].minAngle = PincOpenCoupling.FingerClosedDeg;
            joints[last].maxAngle = PincOpenCoupling.FingerOpenDeg;
            joints[last].homeAngle = PincOpenCoupling.FingerOpenDeg;
            if (drive != null) joints[last].articulationBody = drive;

            Debug.Log($"[Migrate] {robotName} {kind}: J{last} → " +
                      $"{(drive != null ? drive.name : "(범위만 갱신)")}, " +
                      $"범위 {joints[last].minAngle:F1}° ~ {joints[last].maxAngle:F1}°");
        }

        static SOArmManager FindManagerFor(SOArmSimController sim)
        {
            foreach (var m in Object.FindObjectsByType<SOArmManager>(FindObjectsInactive.Include))
                if (m.sim == sim) return m;
            return null;
        }

        static void RemoveVisualChild(Transform parent, string namePart)
        {
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t == parent) continue;
                if (t.GetComponent<ArticulationBody>() != null) continue;  // 다른 링크는 건드리지 않음
                if (t.name.Contains(namePart))
                {
                    Debug.Log($"[Migrate]   중복 메시 '{t.name}' 제거");
                    Object.DestroyImmediate(t.gameObject);
                    return;
                }
            }
        }

        static void StripCloneSuffix(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("(Clone)"))
                    t.name = t.name.Substring(0, t.name.Length - "(Clone)".Length);
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static GameObject ImportTempRobot()
        {
            string full = Path.GetFullPath(UrdfAssetPath);
            if (!File.Exists(full)) { Debug.LogError($"[Migrate] URDF 없음: {full}"); return null; }

            var settings = new ImportSettings
            {
                chosenAxis = ImportSettings.axisType.yAxis,
                convexMethod = ImportSettings.convexDecomposer.unity,
            };

            GameObject robot = null;
            var pipe = UrdfRobotExtensions.Create(full, settings, loadStatus: false);
            while (pipe.MoveNext()) if (pipe.Current != null) robot = pipe.Current;
            if (robot == null) robot = pipe.Current;

            if (robot == null) { Debug.LogError("[Migrate] 임시 로봇 임포트 실패"); return null; }
            robot.name = "__temp_pincopen_source";
            return robot;
        }
    }
}
