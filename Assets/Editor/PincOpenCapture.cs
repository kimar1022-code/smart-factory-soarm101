using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// 미리보기 씬을 여러 각도에서 PNG로 렌더링한다.
    /// 배치 모드에서 호출해 그리퍼 장착 위치를 눈으로 검증하기 위한 도구.
    /// </summary>
    public static class PincOpenCapture
    {
        const int Width = 900;
        const int Height = 700;

        [MenuItem("Tools/SO-ARM/그리퍼 장착 상태 캡처", false, 110)]
        public static void CaptureAll()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PINCOPEN_SHOT_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Application.dataPath, "..", "Captures");
            Directory.CreateDirectory(outDir);

            var wrist = GameObject.Find("gripper_link");
            if (wrist == null)
            {
                Debug.LogError("[Capture] gripper_link 를 찾을 수 없음. 씬이 로드됐는지 확인.");
                return;
            }

            // 손목 결합부를 중심으로 잡는다. 여기가 정렬을 판단하는 지점.
            Vector3 focus = wrist.transform.position;

            Capture(outDir, "1_side", focus, new Vector3(0.30f, 0.02f, 0f), 0.34f);
            Capture(outDir, "2_top", focus, new Vector3(0.02f, 0.30f, 0.02f), 0.34f);
            Capture(outDir, "3_front", focus, new Vector3(0f, 0.02f, 0.30f), 0.34f);

            // 접합부만 크게 — 틈/파묻힘 판정용
            var adapter = GameObject.Find("pincopen_adapter_link");
            Vector3 joint = adapter != null
                ? (focus + adapter.transform.position) * 0.5f
                : focus;
            Capture(outDir, "4_junction", joint, new Vector3(0.25f, 0.10f, 0.12f), 0.10f);

            ReportGeometry();

            Debug.Log($"[Capture] 저장 위치: {Path.GetFullPath(outDir)}");
        }

        /// <summary>
        /// 눈으로 보는 대신 실제 메시 경계를 재서 정렬 상태를 수치로 보고한다.
        /// 손목 부품과 어댑터 사이의 간격이 핵심 지표.
        /// </summary>
        static void ReportGeometry()
        {
            LogBounds("gripper_link");
            LogBounds("pincopen_adapter_link");
            LogBounds("pincopen_base_link");

            var wrist = GameObject.Find("gripper_link");
            var adapter = GameObject.Find("pincopen_adapter_link");
            if (wrist == null || adapter == null) return;

            // 손목 링크 자신의 메시만 (자식으로 딸린 PincOpen 은 제외)
            if (TryGetOwnBounds(wrist, out Bounds wb) &&
                TryGetOwnBounds(adapter, out Bounds ab))
            {
                Vector3 d = ab.center - wb.center;
                float gapX = Mathf.Max(0f, Mathf.Abs(d.x) - (wb.extents.x + ab.extents.x));
                float gapY = Mathf.Max(0f, Mathf.Abs(d.y) - (wb.extents.y + ab.extents.y));
                float gapZ = Mathf.Max(0f, Mathf.Abs(d.z) - (wb.extents.z + ab.extents.z));

                Debug.Log($"[Diag] 손목↔어댑터 중심거리 = ({d.x:F4}, {d.y:F4}, {d.z:F4}) m");
                Debug.Log($"[Diag] 축별 빈틈 = ({gapX * 1000f:F1}, {gapY * 1000f:F1}, {gapZ * 1000f:F1}) mm  " +
                          "(0 이면 접촉 또는 겹침)");
            }
        }

        static bool TryGetOwnBounds(GameObject go, out Bounds b)
        {
            b = new Bounds();
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                // 다른 링크(자식 관절) 소속 렌더러는 제외
                var t = r.transform;
                bool ownedByOtherLink = false;
                while (t != null && t != go.transform)
                {
                    if (t.GetComponent<ArticulationBody>() != null) { ownedByOtherLink = true; break; }
                    t = t.parent;
                }
                if (ownedByOtherLink) continue;

                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return !first;
        }

        static void LogBounds(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) { Debug.LogWarning($"[Diag] {name} 없음"); return; }
            if (!TryGetOwnBounds(go, out Bounds b)) { Debug.Log($"[Diag] {name}: 메시 없음"); return; }
            Debug.Log($"[Diag] {name}: center=({b.center.x:F4},{b.center.y:F4},{b.center.z:F4}) " +
                      $"size=({b.size.x * 1000f:F1},{b.size.y * 1000f:F1},{b.size.z * 1000f:F1})mm");
        }

        static void Capture(string dir, string name, Vector3 focus, Vector3 offset, float dist)
        {
            var camGo = new GameObject("__capCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.18f, 0.20f, 0.24f);
            cam.nearClipPlane = 0.001f;
            cam.farClipPlane = 10f;
            cam.fieldOfView = 40f;

            camGo.transform.position = focus + offset.normalized * dist;
            camGo.transform.LookAt(focus);

            // 배치 모드에서도 형태가 보이도록 카메라에 라이트를 붙인다.
            var lightGo = new GameObject("__capLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);

            Debug.Log($"[Capture] {name}.png 저장");
        }

        /// <summary>씬 재생성 + 캡처를 한 번에. 배치 실행 진입점.</summary>
        public static void RebuildAndCapture()
        {
            PincOpenSetupMenu.BuildPreviewScene();
            CaptureAll();
        }

        /// <summary>
        /// 실제 코드 경로(PincOpenCoupling.SetGripperPercent)로 관절이 움직이는지 검증한다.
        /// 인스펙터로 손을 대지 않고, 게임 코드가 쓰는 통로 그대로 호출해 확인한다.
        /// </summary>
        [MenuItem("Tools/SO-ARM/그리퍼 구동 자체검증", false, 111)]
        public static void SelfTest()
        {
            var robot = GameObject.Find("Robot_PincOpen");
            var c = robot != null ? robot.GetComponent<PincOpenCoupling>() : null;
            if (c == null) { Debug.LogError("[SelfTest] PincOpenCoupling 없음"); return; }

            c.ConfigureDrives();

            bool allOk = true;
            float prevSpan = -1f;

            foreach (float pct in new[] { 100f, 75f, 50f, 25f, 0f })
            {
                c.SetGripperPercent(pct);
                StepPhysics();

                float span = MeasureFingerSpan();
                float drive = c.driveJoint != null ? c.driveJoint.xDrive.target : float.NaN;

                string trend = prevSpan < 0 ? "" :
                    (span < prevSpan - 0.0005f ? " ↓닫힘" :
                     span > prevSpan + 0.0005f ? " ↑열림" : " ⚠️변화없음");
                if (prevSpan >= 0 && Mathf.Abs(span - prevSpan) < 0.0005f) allOk = false;

                Debug.Log($"[SelfTest] {pct,5:F0}% → 구동축 {drive,6:F1}°, 손가락 간격 {span * 1000f,5:F1}mm{trend}");
                prevSpan = span;
            }

            // 범위 밖 명령이 잘리는지도 확인
            c.SetGripperPercent(150f);
            StepPhysics();
            float over = c.driveJoint != null ? c.driveJoint.xDrive.target : 0f;
            bool clamped = over <= PincOpenCoupling.FingerOpenDeg + 0.01f;
            Debug.Log($"[SelfTest] 150% 입력 → 구동축 {over:F1}° " +
                      (clamped ? "✅ 안전범위로 잘림" : "❌ 범위를 벗어남"));

            Debug.Log(allOk && clamped
                ? "[SelfTest] ✅ 통과 — 모든 관절이 코드로 구동되고 범위 제한도 동작합니다."
                : "[SelfTest] ❌ 실패 — 위 로그에서 변화없음/범위초과 항목 확인 필요.");
        }

        /// <summary>
        /// UI 슬라이더가 실제로 타는 경로 그대로 그리퍼를 여닫아 본다.
        ///   SmartFactoryUI → DualManager.RouteGripperCommand
        ///   → SOArmManager → SOArmSimController.SetGripperTarget
        ///   → PincOpenCoupling (종동축 3개)
        /// 중간을 건너뛰지 않아야 "UI 로 정말 되는가" 를 답할 수 있다.
        /// </summary>
        public static void EndToEndGripperTest()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/LeRobot.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var dual = Object.FindAnyObjectByType<SOArmDualManager>();
            if (dual == null) { Debug.LogError("[E2E] SOArmDualManager 없음"); return; }

            // 에디트 모드에서는 Awake/Start 가 돌지 않는다.
            // targetAngles 같은 내부 배열이 null 인 채라 그대로 부르면 터진다.
            // Play 모드와 같은 조건을 만들어주고 테스트한다.
            foreach (var s in Object.FindObjectsByType<SOArmSimController>(FindObjectsInactive.Include))
                s.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            foreach (var r in Object.FindObjectsByType<SOArmRealController>(FindObjectsInactive.Include))
                r.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            DumpDriveProperties();   // 씬에 저장된 값 (ConfigureDrives 전)

            foreach (var c in Object.FindObjectsByType<PincOpenCoupling>(FindObjectsInactive.Include))
                c.ConfigureDrives();

            Debug.Log($"[E2E] 제어모드={dual.controlMode}, R1={dual.robot1Enabled}, R2={dual.robot2Enabled}");

            bool ok = true;
            foreach (bool fromR1 in new[] { true, false })
            {
                var mgr = fromR1 ? dual.robot1 : dual.robot2;
                if (mgr == null || mgr.sim == null) continue;

                var coupling = mgr.sim.GetComponent<PincOpenCoupling>();
                float prev = -1f;

                foreach (float pct in new[] { 100f, 50f, 0f })
                {
                    // ★ UI 가 부르는 바로 그 함수
                    dual.RouteGripperCommand(fromR1, pct);

                    // SOArmSimController 는 Update() 에서 xDrive 에 반영한다.
                    // 에디트 모드에선 Update 가 안 도니 직접 한 번 돌려준다.
                    mgr.sim.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                    if (coupling != null) coupling.ApplyCouplingNow();
                    StepPhysics();

                    float span = MeasureSpanUnder(mgr.sim.transform);
                    float drive = coupling != null && coupling.driveJoint != null
                        ? coupling.driveJoint.xDrive.target : float.NaN;

                    string trend = prev < 0 ? "" :
                        (span < prev - 0.0005f ? " ↓" : span > prev + 0.0005f ? " ↑" : " ⚠️변화없음");
                    if (prev >= 0 && Mathf.Abs(span - prev) < 0.0005f) ok = false;

                    Debug.Log($"[E2E] {mgr.sim.gameObject.name} 슬라이더 {pct,5:F0}% → " +
                              $"구동축 {drive,6:F1}°, 손가락 {span * 1000f,5:F1}mm{trend}");
                    prev = span;
                }
            }

            Debug.Log(ok
                ? "[E2E] ✅ 통과 — UI 슬라이더 경로로 그리퍼가 여닫힙니다."
                : "[E2E] ❌ 실패 — 위에 '변화없음' 항목 확인 필요.");
        }

        /// <summary>
        /// 좌우 손가락이 실제로 대칭인지 base 링크 기준 로컬좌표로 측정한다.
        /// "왼쪽이 더 닫힌다" 같은 증상을 눈이 아니라 숫자로 확인하기 위한 것.
        /// </summary>
        public static void SymmetryTest()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/LeRobot.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var c = Object.FindAnyObjectByType<PincOpenCoupling>();
            if (c == null || c.pincopenBase == null) { Debug.LogError("[Sym] 커플링/base 없음"); return; }
            c.ConfigureDrives();

            Transform baseT = c.pincopenBase.transform;

            foreach (float pct in new[] { 100f, 50f, 0f })
            {
                c.SetGripperPercent(pct);
                StepPhysics();

                var lp = FindChildByName(c.transform, "pincopen_left_proximal_link");
                var rp = FindChildByName(c.transform, "pincopen_right_proximal_link");
                var ld = FindChildByName(c.transform, "pincopen_left_distal_link");
                var rd = FindChildByName(c.transform, "pincopen_right_distal_link");

                string angles =
                    $"L_prox={Deg(lp),7:F1}  R_prox={Deg(rp),7:F1}  " +
                    $"L_dist={Deg(ld),7:F1}  R_dist={Deg(rd),7:F1}";

                string pos = "";
                if (ld != null && rd != null &&
                    TryGetOwnBounds(ld.gameObject, out Bounds lb) &&
                    TryGetOwnBounds(rd.gameObject, out Bounds rb))
                {
                    Vector3 L = baseT.InverseTransformPoint(lb.center);
                    Vector3 R = baseT.InverseTransformPoint(rb.center);
                    // 좌우 대칭이면 두 점은 중심면 기준 거울상이어야 한다
                    float asymLat = Mathf.Abs(Mathf.Abs(L.x) - Mathf.Abs(R.x));
                    float asymFwd = Mathf.Abs(L.z - R.z);
                    float asymUp = Mathf.Abs(L.y - R.y);
                    pos = $"\n        L=({L.x * 1000:F1},{L.y * 1000:F1},{L.z * 1000:F1})mm " +
                          $"R=({R.x * 1000:F1},{R.y * 1000:F1},{R.z * 1000:F1})mm " +
                          $"→ 비대칭 측{asymLat * 1000:F1} / 전후{asymFwd * 1000:F1} / 상하{asymUp * 1000:F1} mm";
                }

                Debug.Log($"[Sym] {pct,5:F0}%  {angles}{pos}");
            }
        }

        /// <summary>
        /// 좌우 손가락이 실제로 겹치기 시작하는 각도를 찾는다.
        /// 경계상자(AABB)는 월드축 기준이라 부정확하므로 메시 정점을
        /// base 링크 로컬좌표로 변환해서 잰다.
        /// </summary>
        public static void ContactTest()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/LeRobot.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var c = Object.FindAnyObjectByType<PincOpenCoupling>();
            if (c == null || c.pincopenBase == null) { Debug.LogError("[Contact] 커플링 없음"); return; }
            c.ConfigureDrives();
            Transform baseT = c.pincopenBase.transform;

            var ld = FindChildByName(c.transform, "pincopen_left_distal_link");
            var rd = FindChildByName(c.transform, "pincopen_right_distal_link");
            if (ld == null || rd == null) { Debug.LogError("[Contact] 손가락 링크 없음"); return; }

            Debug.Log("[Contact] 구동각별 좌우 손가락 최단거리 (음수 = 겹침)");

            float firstTouch = float.NaN;
            float prevGap = float.NaN;

            for (float a = 0f; a >= -50f; a -= 2f)
            {
                SetDrive(c.driveJoint, a);          // 리밋 무시하고 전 구간 훑는다
                ApplyCouplingWithRatio(c, a, 1.0f);
                StepPhysics();

                float lInner = InnerEdge(ld, baseT, true);    // 왼쪽의 안쪽 끝 (최대 x)
                float rInner = InnerEdge(rd, baseT, false);   // 오른쪽의 안쪽 끝 (최소 x)
                float gap = rInner - lInner;

                string mark = gap < 0 ? "  ❌ 겹침" : (gap < 0.002f ? "  ← 접촉" : "");
                Debug.Log($"[Contact] {a,6:F1}° → 간격 {gap * 1000f,7:F2}mm{mark}");

                if (float.IsNaN(firstTouch) && gap <= 0f && !float.IsNaN(prevGap))
                {
                    // 직전 각도와 선형보간해서 접촉 각도를 추정 (step 은 위 for 문과 동일해야 함)
                    const float step = 2f;
                    float t = prevGap / (prevGap - gap);
                    firstTouch = Mathf.Lerp(a + step, a, t);
                }
                prevGap = gap;
            }

            if (!float.IsNaN(firstTouch))
                Debug.Log($"[Contact] ⭐ 접촉 시작 각도 ≈ {firstTouch:F1}°  " +
                          $"(현재 닫힘 설정 {PincOpenCoupling.FingerClosedDeg:F1}° 는 " +
                          $"{Mathf.Abs(PincOpenCoupling.FingerClosedDeg - firstTouch):F1}° 더 들어감)");
            else
                Debug.Log("[Contact] 겹침 없음 — 전 구간에서 손가락이 닿지 않습니다.");
        }

        /// <summary>메시 정점을 base 로컬좌표로 옮겨 안쪽 끝 x 를 구한다.</summary>
        static float InnerEdge(Transform link, Transform baseT, bool isLeft)
        {
            float best = isLeft ? float.NegativeInfinity : float.PositiveInfinity;

            foreach (var mf in link.GetComponentsInChildren<MeshFilter>())
            {
                // 다른 링크 소속이면 제외
                var t = mf.transform;
                bool other = false;
                while (t != null && t != link)
                {
                    if (t.GetComponent<ArticulationBody>() != null) { other = true; break; }
                    t = t.parent;
                }
                if (other || mf.sharedMesh == null) continue;

                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 local = baseT.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (isLeft) { if (local.x > best) best = local.x; }
                    else { if (local.x < best) best = local.x; }
                }
            }
            return best;
        }

        static float Deg(Transform t)
        {
            if (t == null) return float.NaN;
            var ab = t.GetComponent<ArticulationBody>();
            if (ab == null) return float.NaN;
            return ab.jointPosition.dofCount > 0 ? ab.jointPosition[0] * Mathf.Rad2Deg : float.NaN;
        }

        /// <summary>ArticulationBody 의 실제 직렬화 경로를 찍어본다. 저장이 안 될 때 원인 파악용.</summary>
        static void DumpDriveProperties()
        {
            var ab = Object.FindObjectsByType<ArticulationBody>(FindObjectsInactive.Include);
            foreach (var a in ab)
            {
                if (a.name != "pincopen_left_proximal_link") continue;
                var so = new SerializedObject(a);
                Debug.Log($"[Props] {a.name} 저장된 값 — " +
                          $"stiffness={so.FindProperty("m_XDrive.stiffness").floatValue}, " +
                          $"limit[{so.FindProperty("m_XDrive.lowerLimit").floatValue:F1} ~ " +
                          $"{so.FindProperty("m_XDrive.upperLimit").floatValue:F1}]  |  " +
                          $"런타임 stiffness={a.xDrive.stiffness}, " +
                          $"limit[{a.xDrive.lowerLimit:F1} ~ {a.xDrive.upperLimit:F1}]");
                break;
            }
        }

        static float MeasureSpanUnder(Transform root)
        {
            var lt = FindChildByName(root, "pincopen_left_distal_link");
            var rt = FindChildByName(root, "pincopen_right_distal_link");
            if (lt == null || rt == null) return 0f;
            if (!TryGetOwnBounds(lt.gameObject, out Bounds lb)) return 0f;
            if (!TryGetOwnBounds(rt.gameObject, out Bounds rb)) return 0f;
            return Vector3.Distance(lb.center, rb.center);
        }

        /// <summary>
        /// 물리를 실제로 돌려서 로봇이 중력에 떨어지는지 측정한다.
        /// ArticulationBody 루트가 immovable 이 아니면 팔 전체가 바닥으로 낙하한다.
        /// </summary>
        public static void GravityTest()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/LeRobot.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var sims = Object.FindObjectsByType<SOArmSimController>(FindObjectsInactive.Include);
            var roots = new List<(string name, Transform t, Vector3 start)>();

            foreach (var s in sims)
            {
                var bl = FindChildByName(s.transform, "base_link");
                if (bl == null) continue;
                var ab = bl.GetComponent<ArticulationBody>();
                roots.Add((s.gameObject.name, bl, bl.position));
                Debug.Log($"[Gravity] {s.gameObject.name}/base_link " +
                          $"immovable={(ab != null ? ab.immovable.ToString() : "?")} " +
                          $"시작 Y={bl.position.y:F4}");
            }

            // 2초분 물리를 돌린다
            try
            {
                var prev = Physics.simulationMode;
                Physics.simulationMode = SimulationMode.Script;
                for (int i = 0; i < 100; i++) Physics.Simulate(0.02f);
                Physics.simulationMode = prev;
            }
            catch (System.Exception e) { Debug.LogWarning($"[Gravity] {e.Message}"); }

            bool fell = false;
            foreach (var r in roots)
            {
                float drop = r.start.y - r.t.position.y;
                bool bad = Mathf.Abs(drop) > 0.005f;
                if (bad) fell = true;
                Debug.Log($"[Gravity] {r.name}: 2초 후 Y 변화 {(-drop) * 1000f:F1}mm " +
                          (bad ? "❌ 떨어짐" : "✅ 고정됨"));
            }

            Debug.Log(fell
                ? "[Gravity] ❌ 로봇이 중력에 낙하합니다. 루트 immovable 설정 필요."
                : "[Gravity] ✅ 낙하 없음.");
        }

        /// <summary>
        /// 실물에서 읽은 각도를 시뮬에 그대로 적용하고 여러 각도에서 촬영한다.
        /// 실물 사진과 비교해 0점 어긋남을 찾기 위한 것.
        /// 각도는 환경변수 PINCOPEN_POSE 로 "J1,J2,J3,J4,J5" (도) 형식 전달.
        /// </summary>
        public static void RenderPose()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/LeRobot.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            string outDir = System.Environment.GetEnvironmentVariable("PINCOPEN_SHOT_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Application.dataPath, "..", "Captures");
            Directory.CreateDirectory(outDir);

            string poseStr = System.Environment.GetEnvironmentVariable("PINCOPEN_POSE");
            float[] pose = { 0f, 0f, 0f, 0f, 0f };
            if (!string.IsNullOrEmpty(poseStr))
            {
                var parts = poseStr.Split(',');
                for (int i = 0; i < Mathf.Min(parts.Length, pose.Length); i++)
                    float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out pose[i]);
            }
            Debug.Log($"[Pose] 적용 각도: J1={pose[0]} J2={pose[1]} J3={pose[2]} J4={pose[3]} J5={pose[4]}");

            var sims = Object.FindObjectsByType<SOArmSimController>(FindObjectsInactive.Include);
            SOArmSimController target = null;
            foreach (var s in sims)
            {
                s.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
                if (s.gameObject.name.Contains("1")) target = s;
            }
            if (target == null && sims.Length > 0) target = sims[0];
            if (target == null) { Debug.LogError("[Pose] SimController 없음"); return; }

            // 구동 파라미터 채우고(에디트 모드에선 Start 가 안 돎) 각도 적용
            foreach (var c in Object.FindObjectsByType<PincOpenCoupling>(FindObjectsInactive.Include))
                c.ConfigureDrives();

            for (int i = 0; i < 5 && i < target.joints.Length; i++)
            {
                var ab = target.joints[i].articulationBody;
                if (ab == null) { Debug.LogWarning($"[Pose] J{i + 1} 슬롯 비어있음"); continue; }
                var d = ab.xDrive;
                d.target = Mathf.Clamp(pose[i], d.lowerLimit, d.upperLimit);
                ab.xDrive = d;
                Debug.Log($"[Pose] J{i + 1} → {ab.name} target={d.target:F1}° (limit {d.lowerLimit:F1}~{d.upperLimit:F1})");
            }

            StepPhysics();

            // 로봇 전체가 들어오도록 여러 방향에서
            Vector3 focus = target.transform.position + Vector3.up * 0.12f;
            Capture(outDir, "pose_side", focus, new Vector3(1f, 0.15f, 0f), 0.75f);
            Capture(outDir, "pose_side2", focus, new Vector3(-1f, 0.15f, 0f), 0.75f);
            Capture(outDir, "pose_front", focus, new Vector3(0f, 0.15f, 1f), 0.75f);
            Capture(outDir, "pose_iso", focus, new Vector3(0.8f, 0.5f, 0.8f), 0.8f);

            // ── 오프셋 후보 훑기 ──
            // 0점이 어긋난 관절을 찾기 위해 J2/J3 를 여러 값으로 돌려 옆모습을 찍는다.
            // 실물 사진과 같은 각도(옆에서 수평)로 찍어야 비교가 된다.
            string sweepStr = System.Environment.GetEnvironmentVariable("PINCOPEN_SWEEP");
            if (!string.IsNullOrEmpty(sweepStr))
            {
                foreach (var item in sweepStr.Split(';'))
                {
                    var kv = item.Split(':');           // 예 "J3:-45"
                    if (kv.Length != 2) continue;
                    if (!int.TryParse(kv[0].Trim().Substring(1), out int jn)) continue;
                    if (!float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out float deg)) continue;

                    int idx = jn - 1;
                    if (idx < 0 || idx >= target.joints.Length) continue;
                    var ab2 = target.joints[idx].articulationBody;
                    if (ab2 == null) continue;

                    var d2 = ab2.xDrive;
                    float prev = d2.target;
                    d2.target = Mathf.Clamp(deg, d2.lowerLimit, d2.upperLimit);
                    ab2.xDrive = d2;
                    StepPhysics();

                    Capture(outDir, $"sweep_J{jn}_{deg:F0}", focus, new Vector3(1f, 0.15f, 0f), 0.75f);
                    Debug.Log($"[Pose] 훑기 J{jn}={deg:F0}° 촬영");

                    d2.target = prev;                    // 원복
                    ab2.xDrive = d2;
                    StepPhysics();
                }
            }

            ReportLinkGeometry(target);

            Debug.Log("[Pose] 촬영 완료");
        }

        /// <summary>
        /// 각 링크가 실제로 어느 방향을 향하는지 수치로 보고한다.
        /// 렌더 이미지는 카메라 각도에 따라 착시가 생기므로, 판단은 숫자로 한다.
        /// </summary>
        static void ReportLinkGeometry(SOArmSimController sim)
        {
            string[] chain = { "base_link", "shoulder_link", "upper_arm_link",
                               "lower_arm_link", "wrist_link", "gripper_link" };

            Debug.Log("[Geom] 링크 월드 위치 (m)");
            var pos = new Dictionary<string, Vector3>();
            foreach (var n in chain)
            {
                var t = FindChildByName(sim.transform, n);
                if (t == null) { Debug.LogWarning($"[Geom] {n} 없음"); continue; }
                pos[n] = t.position;
                Debug.Log($"[Geom]   {n,-18} ({t.position.x:F3}, {t.position.y:F3}, {t.position.z:F3})");
            }

            // 각 구간이 수평면과 이루는 각 (0° = 수평, +90° = 수직 상방)
            void Seg(string a, string b, string label)
            {
                if (!pos.ContainsKey(a) || !pos.ContainsKey(b)) return;
                Vector3 v = pos[b] - pos[a];
                float horiz = new Vector2(v.x, v.z).magnitude;
                float elev = Mathf.Atan2(v.y, horiz) * Mathf.Rad2Deg;
                Debug.Log($"[Geom] {label,-24} 길이 {v.magnitude * 1000f:F0}mm, 수평면 대비 {elev:F1}°");
            }

            Seg("shoulder_link", "upper_arm_link", "어깨→상완");
            Seg("upper_arm_link", "lower_arm_link", "상완→전완(위팔뼈)");
            Seg("lower_arm_link", "wrist_link", "전완→손목");
            Seg("wrist_link", "gripper_link", "손목→그리퍼");

            var tip = FindChildByName(sim.transform, "pincopen_base_link");
            if (tip != null && pos.ContainsKey("base_link"))
            {
                Vector3 d = tip.position - pos["base_link"];
                Debug.Log($"[Geom] 베이스→그리퍼 : 수평거리 {new Vector2(d.x, d.z).magnitude * 1000f:F0}mm, " +
                          $"높이 {d.y * 1000f:F0}mm");
            }
        }

        static Transform FindChildByName(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static float MeasureFingerSpan()
        {
            var lt = GameObject.Find("pincopen_left_distal_link");
            var rt = GameObject.Find("pincopen_right_distal_link");
            if (lt == null || rt == null) return 0f;
            if (!TryGetOwnBounds(lt, out Bounds lb) || !TryGetOwnBounds(rt, out Bounds rb)) return 0f;
            return Vector3.Distance(lb.center, rb.center);
        }

        /// <summary>씬 재생성 + 자체검증. 배치 진입점.</summary>
        public static void RebuildAndSelfTest()
        {
            PincOpenSetupMenu.BuildPreviewScene();
            SelfTest();
        }

        /// <summary>메인 씬을 열어 두 로봇의 그리퍼 상태를 촬영하고 검증한다.</summary>
        public static void VerifyMainScene()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/LeRobot.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            string outDir = System.Environment.GetEnvironmentVariable("PINCOPEN_SHOT_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Application.dataPath, "..", "Captures");
            Directory.CreateDirectory(outDir);

            var couplings = Object.FindObjectsByType<PincOpenCoupling>(FindObjectsInactive.Include);
            Debug.Log($"[Main] PincOpenCoupling {couplings.Length}개 발견");

            int n = 0;
            foreach (var c in couplings)
            {
                n++;
                c.ConfigureDrives();

                foreach (float pct in new[] { 100f, 0f })
                {
                    c.SetGripperPercent(pct);
                    StepPhysics();

                    // 손가락 두 개가 다 들어오도록 메시 중심을 기준으로 잡는다
                    Vector3 focus = c.transform.position;
                    var lt = FindChildByName(c.transform, "pincopen_left_distal_link");
                    var rt = FindChildByName(c.transform, "pincopen_right_distal_link");
                    if (lt != null && rt != null &&
                        TryGetOwnBounds(lt.gameObject, out Bounds lb2) &&
                        TryGetOwnBounds(rt.gameObject, out Bounds rb2))
                    {
                        focus = (lb2.center + rb2.center) * 0.5f;
                    }

                    string tag = $"main_robot{n}_{(pct > 50 ? "open" : "closed")}";
                    Capture(outDir, tag, focus, new Vector3(0.3f, 0.7f, 0.3f), 0.40f);

                    float drive = c.driveJoint != null ? c.driveJoint.xDrive.target : float.NaN;
                    Debug.Log($"[Main] {c.gameObject.name} {pct:F0}% → 구동축 {drive:F1}°");
                }

                c.SetGripperPercent(100f);
                StepPhysics();
            }

            // 전체 모습도 한 장
            var sims = Object.FindObjectsByType<SOArmSimController>(FindObjectsInactive.Include);
            if (sims.Length > 0)
            {
                Vector3 center = Vector3.zero;
                foreach (var s in sims) center += s.transform.position;
                center /= sims.Length;
                Capture(outDir, "main_overview", center + Vector3.up * 0.2f,
                        new Vector3(0.5f, 0.4f, 0.5f), 1.1f);
            }

            Debug.Log("[Main] 검증 촬영 완료");
        }

        /// <summary>
        /// 구동축을 여러 각도로 돌려가며 캡처한다.
        /// 손가락이 어느 부호에서 "모이는지" 눈으로 확정하기 위한 실험.
        /// </summary>
        public static void SweepAndCapture()
        {
            PincOpenSetupMenu.BuildPreviewScene();

            string outDir = System.Environment.GetEnvironmentVariable("PINCOPEN_SHOT_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Application.dataPath, "..", "Captures");
            Directory.CreateDirectory(outDir);

            var robot = GameObject.Find("Robot_PincOpen");
            var coupling = robot != null ? robot.GetComponent<PincOpenCoupling>() : null;
            if (coupling == null || coupling.driveJoint == null)
            {
                Debug.LogError("[Sweep] PincOpenCoupling 또는 구동축을 찾을 수 없음.");
                return;
            }

            // 손가락 끝 두 개의 중점을 본다. 벌어짐/모임을 판정해야 하므로.
            var lTip = GameObject.Find("pincopen_left_distal_link");
            var rTip = GameObject.Find("pincopen_right_distal_link");
            Vector3 focus = (lTip != null && rTip != null)
                ? (lTip.transform.position + rTip.transform.position) * 0.5f
                : robot.transform.position;

            // 대칭 범위로 훑어서 어느 쪽이 닫히는 방향인지 본다
            float[] angles = { -69.9f, -35f, 0f, 35f, 69.9f };
            foreach (float a in angles)
            {
                SetDrive(coupling.driveJoint, a);
                ApplyCouplingManually(coupling, a);
                StepPhysics();

                string tag = a < 0 ? $"neg{Mathf.Abs(a):F0}" : $"pos{a:F0}";

                // 손가락 벌어짐은 위에서 봐야 가장 잘 보인다
                var lt = GameObject.Find("pincopen_left_distal_link");
                var rt = GameObject.Find("pincopen_right_distal_link");
                Vector3 f = (lt != null && rt != null)
                    ? (lt.transform.position + rt.transform.position) * 0.5f
                    : focus;
                // 링크 원점이 아니라 실제 메시 중심으로 재야 벌어짐이 제대로 나온다
                float span = 0f;
                if (lt != null && rt != null &&
                    TryGetOwnBounds(lt, out Bounds lb) && TryGetOwnBounds(rt, out Bounds rb))
                {
                    span = Vector3.Distance(lb.center, rb.center);
                }
                Debug.Log($"[Sweep] drive={a:F1}° → 손가락 메시 중심 간격 {span * 1000f:F1}mm");

                Capture(outDir, $"sweep_{tag}", f, new Vector3(0f, 1f, 0.25f), 0.32f);
            }

            SetDrive(coupling.driveJoint, 0f);
            ApplyCouplingManually(coupling, 0f);
            Debug.Log("[Sweep] 완료");
        }

        // 배치 모드에서는 LateUpdate 가 돌지 않으므로 커플링을 직접 계산해 적용한다.
        static void ApplyCouplingManually(PincOpenCoupling c, float driveDeg)
        {
            bool half = c.preset == PincOpenCoupling.CouplingPreset.ROS2_Half;
            ApplyCouplingWithRatio(c, driveDeg, half ? 0.5f : 1.0f);
        }

        static void ApplyCouplingWithRatio(PincOpenCoupling c, float driveDeg, float m)
        {
            SetDrive(c.leftDistal, driveDeg * -m);
            SetDrive(c.rightProximal, driveDeg * -m);
            SetDrive(c.rightDistal, driveDeg * m);
        }

        /// <summary>
        /// 커플링 배율 ×0.5(ROS2) 와 ×1.0(MJCF) 을 같은 각도에서 비교 촬영한다.
        /// 두 문헌이 서로 다른 기준 관절을 쓰고 있어 어느 쪽이 맞는지 실물 형상으로 판정해야 한다.
        /// </summary>
        public static void CompareRatios()
        {
            PincOpenSetupMenu.BuildPreviewScene();

            string outDir = System.Environment.GetEnvironmentVariable("PINCOPEN_SHOT_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Application.dataPath, "..", "Captures");
            Directory.CreateDirectory(outDir);

            var robot = GameObject.Find("Robot_PincOpen");
            var c = robot != null ? robot.GetComponent<PincOpenCoupling>() : null;
            if (c == null || c.driveJoint == null) { Debug.LogError("[Cmp] 구동축 없음"); return; }

            float[] ratios = { 0.5f, 1.0f };
            float[] drives = { 0f, -69.9f };

            foreach (float m in ratios)
            {
                foreach (float d in drives)
                {
                    SetDrive(c.driveJoint, d);
                    ApplyCouplingWithRatio(c, d, m);
                    StepPhysics();

                    var lt = GameObject.Find("pincopen_left_distal_link");
                    var rt = GameObject.Find("pincopen_right_distal_link");
                    float span = 0f;
                    Vector3 focus = robot.transform.position;
                    if (lt != null && rt != null &&
                        TryGetOwnBounds(lt, out Bounds lb) && TryGetOwnBounds(rt, out Bounds rb))
                    {
                        span = Vector3.Distance(lb.center, rb.center);
                        focus = (lb.center + rb.center) * 0.5f;
                    }

                    string tag = $"x{m:0.0}_drive{(d < 0 ? "closed" : "open")}";
                    Debug.Log($"[Cmp] 배율 ×{m:0.0}, drive={d:F1}° → 손가락 간격 {span * 1000f:F1}mm");
                    Capture(outDir, tag, focus, new Vector3(0f, 1f, 0.3f), 0.30f);
                }
            }

            SetDrive(c.driveJoint, 0f);
            ApplyCouplingWithRatio(c, 0f, 1f);
            Debug.Log("[Cmp] 완료");
        }

        /// <summary>
        /// 배치/에디트 모드에서는 물리가 자동으로 돌지 않아 관절을 움직여도
        /// Transform 이 갱신되지 않는다. 수동으로 몇 스텝 돌려준다.
        /// </summary>
        static void StepPhysics()
        {
            try
            {
                var prev = Physics.simulationMode;
                Physics.simulationMode = SimulationMode.Script;
                for (int i = 0; i < 40; i++) Physics.Simulate(0.02f);
                Physics.simulationMode = prev;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Sweep] 물리 스텝 실패: {e.Message}");
            }
        }

        static void SetDrive(ArticulationBody ab, float deg)
        {
            if (ab == null) return;
            var d = ab.xDrive;
            d.lowerLimit = Mathf.Min(d.lowerLimit, -90f);
            d.upperLimit = Mathf.Max(d.upperLimit, 90f);
            d.target = deg;
            ab.xDrive = d;
            ab.jointPosition = new ArticulationReducedSpace(deg * Mathf.Deg2Rad);
        }
    }
}
