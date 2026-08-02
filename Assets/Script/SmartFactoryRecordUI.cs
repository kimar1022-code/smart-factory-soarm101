using System.Linq;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// Record 모드 전용 UI.
    /// dualManager.controlMode == Record 일 때만 표시.
    /// 슬라이더는 기존 SmartFactoryUI가 그려주고, 여기는 시퀀스 빌더만 담당.
    /// </summary>
    public class SmartFactoryRecordUI : MonoBehaviour
    {
        [Header("매니저 연결")]
        public SOArmDualManager dualManager;
        public RecordManager recordManager;
        public SmartFactoryUI_v3_4 mainUI; // 슬라이더값 가져오려고 참조

        [Header("UI 설정")]
        public int panelHeight = 320;     // 하단 Record 패널 높이
        public int fontSize = 0;

        [Header("🔍 UI 배율")]
        [Tooltip("메인 UI 의 uiScale 과 같은 값으로 맞춰야 두 패널이 어긋나지 않는다.")]
        [Range(1f, 3f)]
        public float uiScale = 1.6f;

        /// <summary>배율을 적용한 가상 화면 크기. 배치 계산은 전부 이 값을 쓴다.</summary>
        int VW => Mathf.RoundToInt(Screen.width / Mathf.Max(uiScale, 0.01f));
        int VH => Mathf.RoundToInt(Screen.height / Mathf.Max(uiScale, 0.01f));

        // 텍스트 입력 상태
        private string projectNameInput = "Untitled";
        private string waitDurationText = "1.0";
        private string loopCountText = "3";
        private string saveFileNameText = "";
        private string loadFileNameText = "";

        // 스크롤 위치
        private Vector2 stepListScroll;
        private Vector2 fileListScroll;

        // 다이얼로그
        private bool showLoadDialog = false;
        private string[] availableFiles = new string[0];

        // 편집 모드
        private int editingStepIndex = -1;
        private string editingNameInput = "";

        void Start()
        {
            if (dualManager == null)
                dualManager = FindAnyObjectByType<SOArmDualManager>();
            if (recordManager == null)
                recordManager = FindAnyObjectByType<RecordManager>();
            if (mainUI == null)
                mainUI = FindAnyObjectByType<SmartFactoryUI_v3_4>();
        }

        /// <summary>
        /// 실제로 쓸 글자 크기(px).
        /// fontSize 가 0 이하면 화면 높이에 맞춰 자동으로 정한다.
        /// 인스펙터에 0 이 저장돼 있어도 기본 11px 로 떨어져 안 보이는 것을 막는다.
        /// </summary>
        int FontPx => fontSize > 0 ? fontSize : 12;   // 확대는 uiScale 이 담당한다

        /// <summary>버튼 한 줄 높이. 글자가 커지면 같이 커져야 잘리지 않는다.</summary>
        int RowH => FontPx + 14;

        void ApplyFont()
        {
            int f = FontPx;
            GUI.skin.box.fontSize = f;
            GUI.skin.label.fontSize = f;
            GUI.skin.button.fontSize = f;
            GUI.skin.textField.fontSize = f;
            GUI.skin.textArea.fontSize = f;
            GUI.skin.toggle.fontSize = f;
            GUI.skin.horizontalSlider.fontSize = f;
        }

        void OnGUI()
        {
            if (dualManager == null || recordManager == null) return;

            // Record 토글이 켜졌을 때만 UI 표시 (Independent/Mirror 어느 쪽에서든 가능)
            if (!dualManager.isRecordModeActive) return;

            // 메인 UI 와 동일한 배율. 두 패널이 같은 좌표계를 써야 겹치거나 어긋나지 않는다.
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(uiScale, uiScale, 1f));

            ApplyFont();
            DrawRecordPanel();

            if (showLoadDialog) DrawLoadDialog();
        }

        /// <summary>
        /// 수동모드(직접교시) 조작.
        ///
        /// 예전에는 메인 UI 에 별도 패널로 있었다. "자세를 만든다 → 스텝으로 저장한다" 가
        /// 하나의 흐름이라 교시를 이 패널 안으로 합쳤다. 산업용 티치펜던트도
        /// 프리드라이브와 프로그램 편집이 같은 화면에 있다.
        /// </summary>
        void DrawTeachRow(int x, int y, int w)
        {
            int bh = RowH;
            int bw = (w - 10) / 3;

            DrawTeachToggle(new Rect(x, y, bw, bh), "1번 수동",
                dualManager.robot1Teach, v => dualManager.SetTeach(true, v));
            DrawTeachToggle(new Rect(x + bw + 5, y, bw, bh), "2번 수동",
                dualManager.robot2Teach, v => dualManager.SetTeach(false, v));
            DrawTeachToggle(new Rect(x + (bw + 5) * 2, y, bw, bh), "미러 수동",
                dualManager.teachMirror, v => dualManager.SetTeachMirror(v));

            GUI.Label(new Rect(x, y + bh + 2, w, FontPx + 8),
                dualManager.teachMirror
                    ? "미러: 1번을 밀면 2번이 따라 합니다"
                    : (dualManager.robot1Teach || dualManager.robot2Teach)
                        ? "회전축·손목이 풀렸습니다. 그리퍼는 슬라이더로 조작하세요"
                        : "켜면 손으로 팔을 밀어 자세를 만들 수 있습니다");
        }

        void DrawTeachToggle(Rect r, string label, bool on, System.Action<bool> setter)
        {
            var prev = GUI.backgroundColor;
            if (on) GUI.backgroundColor = new Color(1f, 0.65f, 0.1f);   // 켜져 있으면 주황
            if (GUI.Button(r, (on ? "✋ " : "") + label)) setter(!on);
            GUI.backgroundColor = prev;
        }

        void DrawRecordPanel()
        {
            int margin = 10;
            int topBar = 160;
            int bottomBar = 60;

            // 우측 세로 패널. 글자를 키웠으므로 폭도 같이 넓힌다.
            int w = Mathf.Min(440, VW - margin * 2);
            int x = VW - w - margin;
            int y = topBar;
            int h = VH - topBar - bottomBar - margin;

            GUI.Box(new Rect(x, y, w, h), "🎬 Record Mode");

            int curY = y + RowH;
            int curX = x + 10;
            int innerW = w - 20;

            // ── 0행: 수동모드(직접교시) ── 자세를 만드는 단계라 맨 위에 둔다
            DrawTeachRow(curX, curY, innerW);
            curY += RowH + FontPx + 14;

            // ── 1행: 프로젝트 이름 ──
            DrawTopRow(curX, curY, innerW);
            curY += RowH * 2 + 14;  // 두 줄 차지

            // ── 2행: 스텝 추가 버튼들 ──
            DrawAddButtonsRow(curX, curY, innerW);
            curY += RowH * 2 + 40;  // 두 줄 차지

            // ── 3행: 스텝 리스트 ──
            int listHeight = h - (curY - y) - 80;
            DrawStepList(curX, curY, innerW, listHeight);
            curY += listHeight + 5;

            // ── 4행: 재생 컨트롤 ──
            DrawPlaybackControls(curX, curY, innerW);
        }

        void DrawTopRow(int x, int y, int w)
        {
            int rh = RowH;
            int lblW = FontPx * 5;

            // 1줄: 프로젝트 이름 + 이름변경
            GUI.Label(new Rect(x, y + 4, lblW, rh), "프로젝트:");
            projectNameInput = GUI.TextField(
                new Rect(x + lblW, y + 4, w - lblW - FontPx * 5 - 5, rh), projectNameInput);
            if (GUI.Button(new Rect(x + w - FontPx * 5, y + 4, FontPx * 5, rh), "이름변경"))
                recordManager.SetProjectName(projectNameInput);

            // 2줄: 새로 / 저장 / 불러오기
            int by = y + rh + 8;
            int bw = (w - 10) / 3;
            if (GUI.Button(new Rect(x, by, bw, rh), "🆕 새로"))
                recordManager.NewProject(projectNameInput);
            if (GUI.Button(new Rect(x + bw + 5, by, bw, rh), "💾 저장"))
            {
                string name = string.IsNullOrEmpty(saveFileNameText) ? projectNameInput : saveFileNameText;
                recordManager.SaveProject(name);
            }
            if (GUI.Button(new Rect(x + (bw + 5) * 2, by, bw, rh), "📂 불러오기"))
            {
                availableFiles = recordManager.ListSavedFiles();
                showLoadDialog = true;
            }
        }

        void DrawAddButtonsRow(int x, int y, int w)
        {
            GUI.Label(new Rect(x, y, w, 18), "스텝 추가:");

            int by = y + 20;
            int bh = 26;

            // 1줄: R1, R2, 둘다
            int bw1 = (w - 10) / 3;

            GUI.color = new Color(0.6f, 0.85f, 1f);
            if (GUI.Button(new Rect(x, by, bw1, bh), "+ R1"))
                AddR1Step();

            GUI.color = new Color(0.6f, 1f, 0.7f);
            if (GUI.Button(new Rect(x + bw1 + 5, by, bw1, bh), "+ R2"))
                AddR2Step();

            GUI.color = new Color(0.85f, 0.7f, 1f);
            if (GUI.Button(new Rect(x + (bw1 + 5) * 2, by, bw1, bh), "+ 둘 다"))
                AddBothStep();

            // 2줄: 대기 + 반복
            int by2 = by + bh + 4;
            GUI.color = new Color(1f, 0.9f, 0.6f);
            if (GUI.Button(new Rect(x, by2, 45, bh), "+ ⏱"))
            {
                if (float.TryParse(waitDurationText, out float d))
                    recordManager.AddWaitStep(d);
            }
            waitDurationText = GUI.TextField(new Rect(x + 47, by2 + 2, 35, bh - 4), waitDurationText);
            GUI.Label(new Rect(x + 84, by2 + 4, 12, bh), "s");

            GUI.color = new Color(1f, 0.7f, 0.9f);
            if (GUI.Button(new Rect(x + 102, by2, 45, bh), "+ 🔁"))
            {
                if (int.TryParse(loopCountText, out int c) && c > 0)
                    recordManager.AddLoopStartStep(c);
            }
            loopCountText = GUI.TextField(new Rect(x + 149, by2 + 2, 28, bh - 4), loopCountText);
            GUI.Label(new Rect(x + 179, by2 + 4, 16, bh), "회");

            if (GUI.Button(new Rect(x + 200, by2, w - 200, bh), "+ 🔁끝"))
                recordManager.AddLoopEndStep();

            GUI.color = Color.white;
        }

        void DrawStepList(int x, int y, int w, int h)
        {
            GUI.Box(new Rect(x, y, w, h), "스텝 리스트");

            var project = recordManager.CurrentProject;
            if (project == null || project.waypoints.Count == 0)
            {
                GUI.Label(new Rect(x + 10, y + 32, w - 20, 22),
                    "스텝 없음. 위의 + 버튼으로 추가하세요.");
                return;
            }

            int rowH = 24;
            int contentH = project.waypoints.Count * rowH + 10;
            Rect viewRect = new Rect(x, y + 28, w, h - 32);
            Rect contentRect = new Rect(0, 0, w - 25, contentH);

            stepListScroll = GUI.BeginScrollView(viewRect, stepListScroll, contentRect);

            for (int i = 0; i < project.waypoints.Count; i++)
            {
                var wp = project.waypoints[i];
                DrawStepRow(0, i * rowH, w - 25, rowH, i, wp);
            }

            GUI.EndScrollView();
        }

        void DrawStepRow(int x, int y, int w, int h, int index, Waypoint wp)
        {
            // 재생 중인 스텝 하이라이트
            if (recordManager.IsPlaying && index == recordManager.CurrentStepIndex)
            {
                GUI.color = new Color(1f, 1f, 0.5f);
                GUI.Box(new Rect(x, y, w, h), "");
                GUI.color = Color.white;
            }

            // 타입별 컬러 배지
            Color badgeColor = wp.type switch
            {
                "motion" => wp.target switch
                {
                    "robot1" => new Color(0.4f, 0.6f, 0.9f),
                    "robot2" => new Color(0.4f, 0.8f, 0.5f),
                    "both" => new Color(0.7f, 0.5f, 0.9f),
                    _ => Color.gray
                },
                "wait" => new Color(0.95f, 0.8f, 0.4f),
                "loop_start" => new Color(0.95f, 0.5f, 0.7f),
                "loop_end" => new Color(0.95f, 0.5f, 0.7f),
                _ => Color.gray
            };

            // #번호
            GUI.Label(new Rect(x + 5, y + 2, 30, h - 2), $"#{wp.stepNumber}");

            // 타입 배지
            GUI.color = badgeColor;
            string badgeText = wp.type == "motion" ? wp.target.ToUpper() :
                               wp.type == "wait" ? "WAIT" :
                               wp.type == "loop_start" ? "LOOP" :
                               wp.type == "loop_end" ? "END" : "?";
            GUI.Box(new Rect(x + 35, y + 3, 55, h - 6), badgeText);
            GUI.color = Color.white;

            // 이름/편집
            if (editingStepIndex == index)
            {
                editingNameInput = GUI.TextField(new Rect(x + 95, y + 3, 280, h - 6), editingNameInput);
                if (GUI.Button(new Rect(x + 380, y + 3, 35, h - 6), "✓"))
                {
                    recordManager.RenameStep(index, editingNameInput);
                    editingStepIndex = -1;
                }
                if (GUI.Button(new Rect(x + 418, y + 3, 35, h - 6), "✗"))
                {
                    editingStepIndex = -1;
                }
            }
            else
            {
                GUI.Label(new Rect(x + 95, y + 2, w - 250, h - 2), wp.GetDisplayText());

                if (GUI.Button(new Rect(x + w - 145, y + 3, 30, h - 6), "✏"))
                {
                    editingStepIndex = index;
                    editingNameInput = wp.name;
                }
            }

            // 속도 (motion일 때만)
            if (wp.type == "motion" && editingStepIndex != index)
            {
                GUI.Label(new Rect(x + w - 250, y + 2, 70, h - 2),
                    $"V{wp.velocity}");
            }

            // 위/아래/삭제 버튼
            if (editingStepIndex != index)
            {
                if (GUI.Button(new Rect(x + w - 110, y + 3, 25, h - 6), "▲"))
                    recordManager.MoveStepUp(index);
                if (GUI.Button(new Rect(x + w - 82, y + 3, 25, h - 6), "▼"))
                    recordManager.MoveStepDown(index);

                GUI.color = new Color(1f, 0.7f, 0.7f);
                if (GUI.Button(new Rect(x + w - 50, y + 3, 40, h - 6), "🗑"))
                    recordManager.RemoveStep(index);
                GUI.color = Color.white;
            }
        }

        void DrawPlaybackControls(int x, int y, int w)
        {
            int stepCount = recordManager.CurrentProject?.waypoints.Count ?? 0;

            // 상태 표시 (좁아서 짧게)
            GUI.Label(new Rect(x, y, w, 18), $"📊 스텝 {stepCount}개");
            GUI.Label(new Rect(x, y + 16, w, 18), $"{recordManager.StatusMessage}");

            int by = y + 38;
            int bh = 30;
            int bw = (w - 10) / 2;

            if (!recordManager.IsPlaying)
            {
                GUI.color = new Color(0.6f, 1f, 0.6f);
                if (GUI.Button(new Rect(x, by, bw, bh), "▶ 재생"))
                    recordManager.StartPlayback();
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(1f, 0.7f, 0.5f);
                if (GUI.Button(new Rect(x, by, bw, bh), "⏸ 재생중"))
                    recordManager.StopPlayback();
                GUI.color = Color.white;
            }

            if (GUI.Button(new Rect(x + bw + 10, by, bw, bh), "⏹ 정지"))
                recordManager.StopPlayback();
        }

        void DrawLoadDialog()
        {
            int dw = 500;
            int dh = 400;
            int dx = (VW - dw) / 2;
            int dy = (VH - dh) / 2;

            // 배경
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, VW, VH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Box(new Rect(dx, dy, dw, dh), "📂 프로젝트 불러오기");

            if (availableFiles.Length == 0)
            {
                GUI.Label(new Rect(dx + 20, dy + 40, dw - 40, 30),
                    "저장된 파일 없음. 먼저 저장하세요.");
            }
            else
            {
                GUI.Label(new Rect(dx + 20, dy + 30, dw - 40, 22),
                    $"파일 {availableFiles.Length}개:");

                Rect viewRect = new Rect(dx + 20, dy + 55, dw - 40, dh - 130);
                Rect contentRect = new Rect(0, 0, dw - 60, availableFiles.Length * 30);
                fileListScroll = GUI.BeginScrollView(viewRect, fileListScroll, contentRect);

                for (int i = 0; i < availableFiles.Length; i++)
                {
                    if (GUI.Button(new Rect(0, i * 30, dw - 60, 28), availableFiles[i]))
                    {
                        loadFileNameText = availableFiles[i];
                    }
                }

                GUI.EndScrollView();

                GUI.Label(new Rect(dx + 20, dy + dh - 70, 80, 22), "선택:");
                loadFileNameText = GUI.TextField(new Rect(dx + 80, dy + dh - 70, dw - 100, 22), loadFileNameText);
            }

            // 버튼들
            if (GUI.Button(new Rect(dx + dw - 200, dy + dh - 38, 90, 28), "✅ 불러오기"))
            {
                if (!string.IsNullOrEmpty(loadFileNameText))
                {
                    if (recordManager.LoadProject(loadFileNameText))
                    {
                        projectNameInput = recordManager.CurrentProject.projectName;
                        showLoadDialog = false;
                    }
                }
            }
            if (GUI.Button(new Rect(dx + dw - 100, dy + dh - 38, 80, 28), "❌ 취소"))
            {
                showLoadDialog = false;
            }
        }

        // ════════════════════════════════════════
        //            스텝 추가 헬퍼
        // ════════════════════════════════════════

        void AddR1Step()
        {
            var (r1Joints, r1Gripper) = GetCurrentSliders(true);
            var (r2Joints, r2Gripper) = GetCurrentSliders(false);
            recordManager.AddMotionStepFromUI("robot1",
                r1Joints, r1Gripper, r2Joints, r2Gripper,
                GetUIVelocity(), GetUIAcceleration());
        }

        void AddR2Step()
        {
            var (r1Joints, r1Gripper) = GetCurrentSliders(true);
            var (r2Joints, r2Gripper) = GetCurrentSliders(false);
            recordManager.AddMotionStepFromUI("robot2",
                r1Joints, r1Gripper, r2Joints, r2Gripper,
                GetUIVelocity(), GetUIAcceleration());
        }

        void AddBothStep()
        {
            var (r1Joints, r1Gripper) = GetCurrentSliders(true);
            var (r2Joints, r2Gripper) = GetCurrentSliders(false);
            recordManager.AddMotionStepFromUI("both",
                r1Joints, r1Gripper, r2Joints, r2Gripper,
                GetUIVelocity(), GetUIAcceleration());
        }

        // ════════════════════════════════════════
        //          SmartFactoryUI에서 슬라이더값 가져오기
        // ════════════════════════════════════════

        (float[], float) GetCurrentSliders(bool robot1)
        {
            float[] joints = new float[6];
            float gripper = 50f;

            if (mainUI != null)
            {
                if (robot1)
                {
                    joints = mainUI.GetR1Sliders();
                    gripper = mainUI.GetR1Gripper();
                }
                else
                {
                    joints = mainUI.GetR2Sliders();
                    gripper = mainUI.GetR2Gripper();
                }
            }
            return (joints, gripper);
        }

        int GetUIVelocity()
        {
            return mainUI != null ? mainUI.motorVelocity : 800;
        }

        int GetUIAcceleration()
        {
            return mainUI != null ? mainUI.motorAcceleration : 50;
        }
    }
}