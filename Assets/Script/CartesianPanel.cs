using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 카티시안(XYZ) 제어 패널. F2 로 여닫는다.
    ///
    /// 관제 화면(ControlTowerCanvas)을 건드리지 않는 별도 오버레이다.
    /// 잘 돌아가는 화면을 뜯어고치는 것보다 얹는 쪽이 위험이 적고,
    /// 마음에 안 들면 컴포넌트만 빼면 원상복구된다.
    ///
    /// 맨 위 R1/R2 전환이 이 패널 전체의 대상을 바꾼다 — 슬라이더도,
    /// ± 버튼도, 관절 입력도 전부 선택된 로봇을 따라간다.
    /// </summary>
    [RequireComponent(typeof(SOArmIKController))]
    public class CartesianPanel : MonoBehaviour
    {
        [Header("표시")]
        public bool visible = true;
        public KeyCode toggleKey = KeyCode.F2;
        public Vector2 panelPos = new Vector2(20, 60);

        [Header("작업 범위 (m) — 슬라이더 끝값")]
        [Tooltip("팔이 닿을 수 있는 범위보다 넉넉히 잡는다. 실제 도달 여부는 서버가 알려준다.")]
        public Vector3 rangeMin = new Vector3(-0.10f, -0.45f, -0.10f);
        public Vector3 rangeMax = new Vector3(0.55f, 0.45f, 0.55f);

        SOArmIKController ik;

        // ± 스텝 (mm). 관절 패널의 0.5/1/5/10 과 같은 발상.
        static readonly float[] StepsMm = { 1f, 5f, 10f, 50f };
        int stepIdx = 1;

        // 관절 ± 스텝 (deg)
        static readonly float[] JointStepsDeg = { 0.5f, 1f, 5f, 10f };
        int jointStepIdx = 2;

        // 직접 입력 칸 (mm 문자열). 포커스가 없을 때만 실제 값으로 덮어쓴다.
        readonly string[] entry = new string[3];
        int editingAxis = -1;

        bool liveFollow = true;    // 슬라이더를 끄는 동안 계속 따라갈지
        bool targetDirty = false;

        const float W = 430f;
        static readonly string[] AxisName = { "X", "Y", "Z" };

        void Awake() { ik = GetComponent<SOArmIKController>(); }

        void Start()
        {
            // 목표를 현재 위치에서 시작해야 첫 조작에 팔이 안 튄다.
            if (ik != null) ik.RefreshCurrentTcp();
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;

            // 목표가 바뀌었으면 푼다. inFlight 가드는 컨트롤러가 갖고 있으므로
            // 매 프레임 불러도 요청이 쌓이지 않는다.
            if (targetDirty && liveFollow && !ik.Blocked)
            {
                ik.SolveAndApply();
                targetDirty = false;
            }
        }

        void OnGUI()
        {
            if (!visible || ik == null) return;

            float h = 486f;
            var rect = new Rect(panelPos.x, panelPos.y, W, h);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, W - 16, h - 12));

            DrawHeader();
            GUILayout.Space(4);
            DrawReadout();
            GUILayout.Space(4);
            DrawAxes();
            GUILayout.Space(2);
            DrawStepRow();
            GUILayout.Space(4);
            DrawOptions();
            GUILayout.Space(4);
            DrawActions();
            GUILayout.Space(4);
            DrawJoints();
            GUILayout.Space(4);
            DrawStatus();

            GUILayout.EndArea();
        }

        // ── 맨 위: 제목 + R1/R2 전환 ─────────────────────────
        void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"카티시안 (IK)   [{toggleKey}]", GUILayout.Width(190));
            GUILayout.FlexibleSpace();

            var prev = GUI.color;

            GUI.color = ik.useRobot2 ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.35f, 0.85f, 1f);
            if (GUILayout.Button("R1", GUILayout.Width(52)) && ik.useRobot2)
                SwitchRobot(false);

            GUI.color = ik.useRobot2 ? new Color(1f, 0.65f, 0.3f) : new Color(0.45f, 0.45f, 0.45f);
            if (GUILayout.Button("R2", GUILayout.Width(52)) && !ik.useRobot2)
                SwitchRobot(true);

            GUI.color = prev;
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 대상 로봇을 바꾼다.
        ///
        /// 바꾸는 즉시 목표를 **새 로봇의 현재 위치**로 다시 읽어야 한다.
        /// 안 그러면 R1 을 보던 목표가 그대로 R2 에 적용돼서 R2 가 갑자기
        /// 엉뚱한 데로 튄다. 로봇을 바꾸는 것은 "다른 팔을 새로 잡는 것" 이지
        /// "같은 목표를 옮기는 것" 이 아니다.
        /// </summary>
        void SwitchRobot(bool toRobot2)
        {
            ik.useRobot2 = toRobot2;
            targetDirty = false;
            editingAxis = -1;
            ik.RefreshCurrentTcp();
        }

        // ── 현재 / 목표 위치 표시 ────────────────────────────
        void DrawReadout()
        {
            Vector3 c = ik.CurrentTcp * 1000f;
            Vector3 t = ik.TargetTcp * 1000f;
            GUILayout.Label($"현재 TCP   X {c.x,7:F1}   Y {c.y,7:F1}   Z {c.z,7:F1}   (mm)");
            GUILayout.Label($"목표       X {t.x,7:F1}   Y {t.y,7:F1}   Z {t.z,7:F1}");
        }

        // ── 축별 슬라이더 + ± + 직접 입력 ────────────────────
        void DrawAxes()
        {
            for (int a = 0; a < 3; a++) DrawAxis(a);
        }

        void DrawAxis(int a)
        {
            float step = StepsMm[stepIdx] * 0.001f;
            float cur = Axis(ik.TargetTcp, a);

            GUILayout.BeginHorizontal();
            GUILayout.Label(AxisName[a], GUILayout.Width(14));

            if (GUILayout.Button("−", GUILayout.Width(26))) { ik.NudgeTarget(a, -step); targetDirty = true; }

            float lo = Axis(rangeMin, a), hi = Axis(rangeMax, a);
            float v = GUILayout.HorizontalSlider(cur, lo, hi, GUILayout.Width(190));
            if (!Mathf.Approximately(v, cur))
            {
                var t = ik.TargetTcp; SetAxis(ref t, a, v); ik.SetTarget(t);
                targetDirty = true;
            }

            if (GUILayout.Button("+", GUILayout.Width(26))) { ik.NudgeTarget(a, step); targetDirty = true; }

            // 직접 입력 (mm). 편집 중이 아닐 때만 실제 값으로 갱신한다 —
            // 매 프레임 덮어쓰면 타이핑이 지워진다.
            GUI.SetNextControlName("entry" + a);
            if (editingAxis != a) entry[a] = (Axis(ik.TargetTcp, a) * 1000f).ToString("F1");
            string s = GUILayout.TextField(entry[a], GUILayout.Width(64));
            if (GUI.GetNameOfFocusedControl() == "entry" + a) editingAxis = a;
            else if (editingAxis == a) editingAxis = -1;

            if (s != entry[a])
            {
                entry[a] = s;
                if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float mm))
                {
                    var t = ik.TargetTcp; SetAxis(ref t, a, mm * 0.001f); ik.SetTarget(t);
                    targetDirty = true;
                }
            }
            GUILayout.EndHorizontal();
        }

        void DrawStepRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("스텝", GUILayout.Width(34));
            for (int i = 0; i < StepsMm.Length; i++)
            {
                var prev = GUI.color;
                if (i == stepIdx) GUI.color = new Color(0.35f, 0.85f, 1f);
                if (GUILayout.Button($"{StepsMm[i]:0}mm", GUILayout.Width(56))) stepIdx = i;
                GUI.color = prev;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        void DrawOptions()
        {
            GUILayout.BeginHorizontal();
            liveFollow = GUILayout.Toggle(liveFollow, " 실시간 추종", GUILayout.Width(110));
            GUILayout.Label($"자세가중치 {ik.orientationWeight:F2}", GUILayout.Width(120));
            ik.orientationWeight = GUILayout.HorizontalSlider(ik.orientationWeight, 0f, 0.2f, GUILayout.Width(120));
            GUILayout.EndHorizontal();
            GUILayout.Label("  0 = 위치만 맞춤 (팔이 5축이라 자세는 다 못 맞춘다)");
        }

        void DrawActions()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("현재 위치 읽기")) ik.RefreshCurrentTcp();
            if (GUILayout.Button("목표 = 현재")) ik.SnapTargetToCurrent();

            GUI.enabled = !ik.Blocked;
            if (GUILayout.Button("이동")) ik.SolveAndApply();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        // ── 관절 직접 ± (deg) ────────────────────────────────
        void DrawJoints()
        {
            var robot = ik.ActiveRobot;
            GUILayout.Label($"관절 직접 조작 ({ik.ActiveRobotLabel}, deg)");

            GUILayout.BeginHorizontal();
            GUILayout.Label("스텝", GUILayout.Width(34));
            for (int i = 0; i < JointStepsDeg.Length; i++)
            {
                var prev = GUI.color;
                if (i == jointStepIdx) GUI.color = new Color(0.35f, 0.85f, 1f);
                if (GUILayout.Button($"{JointStepsDeg[i]:0.#}°", GUILayout.Width(46))) jointStepIdx = i;
                GUI.color = prev;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (robot == null) { GUILayout.Label("  (매니저 없음)"); return; }

            float d = JointStepsDeg[jointStepIdx];
            GUILayout.BeginHorizontal();
            for (int j = 0; j < 5; j++)
            {
                float ang = 0f;
                try { ang = robot.GetJointAngle(j); } catch { }

                GUILayout.BeginVertical(GUILayout.Width(78));
                GUILayout.Label($"J{j + 1} {ang,6:F1}");
                GUILayout.BeginHorizontal();
                GUI.enabled = !ik.Blocked;
                if (GUILayout.Button("−", GUILayout.Width(34))) NudgeJoint(j, -d);
                if (GUILayout.Button("+", GUILayout.Width(34))) NudgeJoint(j, +d);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 관절을 직접 움직인다. 이건 IK 를 안 거친다.
        /// 움직인 뒤 TCP 를 다시 읽어야 카티시안 목표가 실제 위치와 어긋나지 않는다.
        /// </summary>
        void NudgeJoint(int index, float deltaDeg)
        {
            var robot = ik.ActiveRobot;
            if (robot == null || ik.Blocked) return;

            float cur = 0f;
            try { cur = robot.GetJointAngle(index); } catch { }
            robot.SetJointTarget(index, cur + deltaDeg);

            // 관절로 움직였으니 목표도 새 위치로 따라와야 한다.
            ik.RefreshCurrentTcp(okFk => { if (okFk) ik.SnapTargetToCurrent(); });
        }

        void DrawStatus()
        {
            var prev = GUI.color;
            if (ik.Blocked) GUI.color = new Color(1f, 0.45f, 0.45f);
            else if (!ik.LastConverged) GUI.color = new Color(1f, 0.8f, 0.3f);
            GUILayout.Label(ik.Blocked ? "🛑 비상정지 중 — 조작 차단" : ik.StatusMessage);
            GUI.color = prev;
        }

        // ── Vector3 축 접근 ──────────────────────────────────
        static float Axis(Vector3 v, int a) => a == 0 ? v.x : (a == 1 ? v.y : v.z);
        static void SetAxis(ref Vector3 v, int a, float val)
        {
            if (a == 0) v.x = val; else if (a == 1) v.y = val; else v.z = val;
        }
    }
}
