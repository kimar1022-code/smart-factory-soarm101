using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 스마트팩토리 제어 UI v3.4
    ///
    /// v3.3 → v3.4 변경:
    ///  🆕 "홈으로 이동" 버튼 (서버에 home 명령 전송)
    ///  🆕 "홈포즈 지정" 버튼 (서버에 set_home 안전 명령)
    ///  🛡️ 홈포즈 지정 시 확인 다이얼로그 (실수 방지)
    /// </summary>
    public class SmartFactoryUI_v3_4 : MonoBehaviour
    {
        [Header("매니저 연결")]
        public SOArmDualManager dualManager;

        [Header("UI 크기 (0이면 자동)")]
        public int panelWidth = 0;
        public int panelHeight = 0;

        [Header("기본 스텝 각도")]
        public float stepDeg = 5f;

        [Header("폰트 크기 (0이면 기본)")]
        public int fontSize = 0;

        [Header("🎚️ 속도 설정")]
        [Range(0, 3000)]
        public int motorVelocity = 800;
        [Range(1, 254)]
        public int motorAcceleration = 50;

        private float[] r1Sliders = new float[6];
        private float[] r2Sliders = new float[6];
        private float r1Gripper = 50f;
        private float r2Gripper = 50f;

        private string stepText = "5";

        private Vector2 r1Scroll;
        private Vector2 r2Scroll;

        private int actualPanelWidth;
        private int actualPanelHeight;

        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private bool stylesInitialized = false;

        private bool speedSettingsApplied = false;
        private float lastSpeedSendTime = 0f;

        // 홈포즈 지정 확인 다이얼로그
        private bool showSetHomeDialog = false;
        private int setHomeTarget = 0; // 0=robot1, 1=robot2
        private string setHomeStatus = "";

     // ════════════════════════════════════════
     // 외부에서 슬라이더값 읽기용 (Record UI에서 사용)
     // ════════════════════════════════════════
        public float[] GetR1Sliders() { return r1Sliders; }
        public float[] GetR2Sliders() { return r2Sliders; }
        public float GetR1Gripper() { return r1Gripper; }
        public float GetR2Gripper() { return r2Gripper; }

        void Start()
        {
            if (dualManager == null) dualManager = FindAnyObjectByType<SOArmDualManager>();
            InitSlidersFromRobot(r1Sliders, dualManager?.robot1);
            InitSlidersFromRobot(r2Sliders, dualManager?.robot2);
        }

        void InitSlidersFromRobot(float[] sliders, SOArmManager robot)
        {
            if (robot == null) return;
            var home = robot.GetHomePose();
            for (int i = 0; i < Mathf.Min(home.Length, sliders.Length); i++)
                sliders[i] = home[i];
        }

        void InitStyles()
        {
            if (stylesInitialized) return;

            boxStyle = new GUIStyle(GUI.skin.box);
            labelStyle = new GUIStyle(GUI.skin.label);

            if (fontSize > 0)
            {
                // GUI.skin의 모든 스타일에 폰트 크기 적용
                // (다음 OnGUI부터 모든 Button, Label, Box, TextField 등에 반영됨)
                GUI.skin.box.fontSize = fontSize;
                GUI.skin.label.fontSize = fontSize;
                GUI.skin.button.fontSize = fontSize;
                GUI.skin.textField.fontSize = fontSize;
                GUI.skin.textArea.fontSize = fontSize;
                GUI.skin.toggle.fontSize = fontSize;
                GUI.skin.horizontalSlider.fontSize = fontSize;

                // 로컬 스타일도 적용 (혹시 사용될 때 대비)
                boxStyle.fontSize = fontSize;
                labelStyle.fontSize = fontSize;
            }

            stylesInitialized = true;
        }

        void CalculateSizes()
        {
            // 좌측 슬라이더 패널: 폭 좁게, 세로로 2개 쌓기
            actualPanelWidth = (panelWidth > 0) ? panelWidth : 280;

            int topBar = 160;
            int bottomBar = 60;
            int verticalMargin = 30;
            int availableHeight = Screen.height - topBar - bottomBar - verticalMargin;

            // 패널 1개 높이 = 사용 가능 높이의 절반 (gap 5px)
            int gap = 5;
            actualPanelHeight = (panelHeight > 0) ? panelHeight : ((availableHeight - gap) / 2);

            if (actualPanelWidth < 280) actualPanelWidth = 280;
            if (actualPanelHeight < 320) actualPanelHeight = 320;
        }

        void OnGUI()
        {
            if (dualManager == null) return;

            InitStyles();
            CalculateSizes();

            DrawTopBar();
            DrawRobotPanel("🤖 Robot 1", 10, 160,
                dualManager.robot1, r1Sliders, ref r1Gripper, true, 1, ref r1Scroll);
            DrawRobotPanel("🤖 Robot 2", 10, 160 + actualPanelHeight + 5,
                dualManager.robot2, r2Sliders, ref r2Gripper, false, 2, ref r2Scroll);
            DrawTeachPanel(10 + actualPanelWidth + 10, 160);
            DrawBottomBar();

            // 🛑 비상정지 배너 — 상태를 못 보고 조작하는 일이 없도록 화면 상단에 크게
            if (dualManager.EmergencyStopped)
            {
                DrawEmergencyBanner();
            }

            // 홈포즈 지정 확인 다이얼로그
            if (showSetHomeDialog)
            {
                DrawSetHomeDialog();
            }
        }

        /// <summary>비상정지 중임을 화면 상단에 확실히 알린다. 깜빡여서 놓치지 않게.</summary>
        void DrawEmergencyBanner()
        {
            int w = Mathf.Min(560, Screen.width - 40);
            var rect = new Rect((Screen.width - w) / 2, 8, w, 52);

            bool blink = Mathf.FloorToInt(Time.realtimeSinceStartup * 2f) % 2 == 0;
            var prevBg = GUI.backgroundColor;
            var prevCol = GUI.color;

            GUI.backgroundColor = blink ? new Color(0.85f, 0.1f, 0.1f) : new Color(0.5f, 0.05f, 0.05f);
            GUI.Box(rect, GUIContent.none);

            GUI.color = Color.white;
            var st = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
            };
            GUI.Label(rect, "🛑  비상정지  —  조작이 차단됨.  '정지 해제'를 눌러 재개", st);

            GUI.backgroundColor = prevBg;
            GUI.color = prevCol;
        }

        // ════════════════════════════════════════════════════════
        // 수동(직접교시) 모드 + 모션 녹화/재생
        // ════════════════════════════════════════════════════════
        //
        // 【수동모드가 왜 필요한가】
        //   슬라이더로 자세를 만드는 건 느리고 감이 안 온다.
        //   손으로 직접 팔을 끌어다 놓는 게 산업 현장의 직접교시(Direct Teaching) 방식이다.
        //
        // 【왜 토크를 "끄는" 가】
        //   STS3215 는 1:345 감속기라 역구동이 구조적으로 안 된다.
        //   Torque_Limit 을 500 → 192 (38%) 까지 내려도 손으로 안 꺾였다.
        //   → 완전히 끄는 것 말고는 방법이 없다.
        //   단, 무게를 드는 shoulder_lift / elbow_flex 를 끄면 팔이 주저앉으므로
        //   중력 모멘트가 없는 관절(pan / wrist_flex / wrist_roll)만 서버가 끈다.
        //
        // 【미러 수동】
        //   양쪽을 동시에 손으로 밀 수는 없다.
        //   그래서 "로봇1을 손으로 가르치면 로봇2가 따라 하는" 형태다.
        //   로봇2는 명령을 따라야 하므로 토크를 유지한다.
        void DrawTeachPanel(int x, int y)
        {
            int w = Mathf.Min(430, Screen.width - x - 10);
            if (w < 260) return;                       // 화면이 좁으면 그리지 않는다
            EnsureRecorders();

            GUI.Box(new Rect(x, y, w, 250), "✋ 수동 모드 (직접교시) & 🎬 모션 녹화");

            int bx = x + 10, by = y + 26, bw = (w - 30) / 3, bh = 26;

            // ── 수동모드 3종 ────────────────────────────────────
            DrawTeachToggle(new Rect(bx, by, bw, bh), "1번 수동",
                dualManager.robot1Teach, v => dualManager.SetTeach(true, v));
            DrawTeachToggle(new Rect(bx + bw + 5, by, bw, bh), "2번 수동",
                dualManager.robot2Teach, v => dualManager.SetTeach(false, v));
            DrawTeachToggle(new Rect(bx + (bw + 5) * 2, by, bw, bh), "미러 수동",
                dualManager.teachMirror, v => dualManager.SetTeachMirror(v));

            GUI.Label(new Rect(bx, by + bh + 2, w - 20, 20),
                dualManager.teachMirror
                    ? "미러: 1번을 손으로 밀면 2번이 따라 합니다"
                    : (dualManager.robot1Teach || dualManager.robot2Teach)
                        ? "회전축·손목이 풀렸습니다. 어깨/팔꿈치는 무게 때문에 잠겨 있습니다"
                        : "켜면 손으로 팔을 밀어 자세를 가르칠 수 있습니다");

            // ── 녹화 대상 ───────────────────────────────────────
            int ry = by + bh + 24;
            GUI.Label(new Rect(bx, ry, 60, 20), "녹화 대상:");
            if (GUI.Toggle(new Rect(bx + 70, ry, 60, 20), recordTarget == 0, " 1번")) recordTarget = 0;
            if (GUI.Toggle(new Rect(bx + 135, ry, 60, 20), recordTarget == 1, " 2번")) recordTarget = 1;

            var rec = ActiveRecorder;
            if (rec == null)
            {
                GUI.Label(new Rect(bx, ry + 24, w - 20, 20), "⚠ Recorder 컴포넌트가 없습니다");
                return;
            }

            GUI.Label(new Rect(bx + 200, ry, w - 210, 20),
                $"{rec.FrameCount} 프레임 / {rec.ClipDuration:F1}초");

            // ── 녹화 · 재생 ─────────────────────────────────────
            int cy = ry + 26;
            int cw = (w - 35) / 4;

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = rec.isRecording ? new Color(0.9f, 0.2f, 0.2f) : prevBg;
            if (GUI.Button(new Rect(bx, cy, cw, bh), rec.isRecording ? "⏹ 녹화중" : "🔴 녹화"))
            {
                if (rec.isRecording) rec.StopRecording();
                else rec.StartRecording();
            }
            GUI.backgroundColor = rec.isPlaying ? new Color(0.2f, 0.8f, 0.3f) : prevBg;
            if (GUI.Button(new Rect(bx + (cw + 5), cy, cw, bh), rec.isPlaying ? "⏹ 재생중" : "▶ 재생"))
            {
                if (rec.isPlaying) rec.StopPlayback();
                else rec.StartPlayback();
            }
            GUI.backgroundColor = prevBg;

            if (GUI.Button(new Rect(bx + (cw + 5) * 2, cy, cw, bh), "💾 저장"))
                rec.SaveToFile();
            if (GUI.Button(new Rect(bx + (cw + 5) * 3, cy, cw, bh), "📂 불러오기"))
                LoadLatestMotion(rec);

            // ── 재생 옵션 ───────────────────────────────────────
            int sy = cy + bh + 6;
            GUI.Label(new Rect(bx, sy, 70, 20), $"속도 {rec.playbackSpeed:F1}x");
            rec.playbackSpeed = GUI.HorizontalSlider(
                new Rect(bx + 75, sy + 6, w - 190, 18), rec.playbackSpeed, 0.1f, 1.5f);
            rec.loop = GUI.Toggle(new Rect(x + w - 100, sy, 90, 20), rec.loop, " 반복재생");

            // ── 진행 바 ─────────────────────────────────────────
            int py = sy + 26;
            var bar = new Rect(bx, py, w - 20, 14);
            GUI.Box(bar, GUIContent.none);
            if (rec.isPlaying)
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
                GUI.Box(new Rect(bar.x, bar.y, bar.width * rec.PlayProgress, bar.height), GUIContent.none);
                GUI.backgroundColor = prevBg;
            }

            GUI.Label(new Rect(bx, py + 18, w - 20, 40), recorderStatus);
        }

        void DrawTeachToggle(Rect r, string label, bool on, System.Action<bool> setter)
        {
            var prev = GUI.backgroundColor;
            if (on) GUI.backgroundColor = new Color(1f, 0.65f, 0.1f);   // 켜져 있으면 주황
            if (GUI.Button(r, (on ? "✋ " : "") + label)) setter(!on);
            GUI.backgroundColor = prev;
        }

        [Header("🎬 모션 녹화 (없으면 자동 탐색)")]
        public SOArmMotionRecorder recorder1;
        public SOArmMotionRecorder recorder2;

        private int recordTarget = 0;                 // 0 = 로봇1, 1 = 로봇2
        private string recorderStatus = "";
        private bool recordersHooked = false;

        SOArmMotionRecorder ActiveRecorder => recordTarget == 0 ? recorder1 : recorder2;

        void EnsureRecorders()
        {
            if (recordersHooked) return;
            recordersHooked = true;

            // 인스펙터에서 안 꽂아뒀으면 씬에서 찾아 로봇별로 배정한다.
            if (recorder1 == null || recorder2 == null)
            {
                var all = FindObjectsByType<SOArmMotionRecorder>(FindObjectsSortMode.None);
                foreach (var r in all)
                {
                    if (r.target == dualManager.robot1 && recorder1 == null) recorder1 = r;
                    else if (r.target == dualManager.robot2 && recorder2 == null) recorder2 = r;
                }
            }

            // 그래도 없으면 만들어 붙인다.
            // 씬을 직접 손보지 않아도 바로 쓸 수 있게 하기 위함이다.
            if (recorder1 == null && dualManager.robot1 != null)
                recorder1 = Attach(dualManager.robot1);
            if (recorder2 == null && dualManager.robot2 != null)
                recorder2 = Attach(dualManager.robot2);

            if (recorder1 != null) recorder1.OnRecorderEvent += m => recorderStatus = m;
            if (recorder2 != null) recorder2.OnRecorderEvent += m => recorderStatus = m;
        }

        static SOArmMotionRecorder Attach(SOArmManager robot)
        {
            var r = robot.gameObject.GetComponent<SOArmMotionRecorder>();
            if (r == null) r = robot.gameObject.AddComponent<SOArmMotionRecorder>();
            r.target = robot;
            return r;
        }

        /// <summary>가장 최근에 저장한 궤적을 불러온다. 파일 선택 UI 없이 한 번에 쓰기 위함.</summary>
        void LoadLatestMotion(SOArmMotionRecorder rec)
        {
            var files = SOArmMotionRecorder.ListSavedFiles();
            if (files.Length == 0)
            {
                recorderStatus = "⚠ 저장된 궤적이 없습니다";
                return;
            }
            rec.LoadFromFile(files[0]);
        }

        void DrawTopBar()
        {
            int totalWidth = Screen.width - 20;

            string channelText = $"{(dualManager.robot1Enabled ? "R1" : "--")}/{(dualManager.robot2Enabled ? "R2" : "--")}";
            GUI.Box(new Rect(10, 10, totalWidth, 45),
                $"제어 모드: {dualManager.controlMode}   |   채널: {channelText}" +
                (dualManager.isRecordModeActive ? "   |   🎬 REC" : ""));

            int x = 20, y = 28, w = 95, h = 22;
            int dx = w + 5;

            // ── 제어 방식 (2개) ──
            if (GUI.Button(new Rect(x + dx * 0, y, w, h), "독립"))
                dualManager.ChangeMode(SOArmDualManager.ControlMode.Independent);
            if (GUI.Button(new Rect(x + dx * 1, y, w, h), "미러"))
                dualManager.ChangeMode(SOArmDualManager.ControlMode.Mirror);

            // ── 녹화 토글 (제어 방식과 직교) ──
            bool newRec = GUI.Toggle(new Rect(x + dx * 2, y, w, h),
                dualManager.isRecordModeActive, " 🎬 Record");
            if (newRec != dualManager.isRecordModeActive)
                dualManager.SetRecordMode(newRec);

            // ── 채널 on/off (예전 Robot1만/Robot2만 대체) ──
            dualManager.robot1Enabled = GUI.Toggle(new Rect(x + dx * 3, y, 78, h),
                dualManager.robot1Enabled, " R1 사용");
            dualManager.robot2Enabled = GUI.Toggle(new Rect(x + dx * 3 + 82, y, 78, h),
                dualManager.robot2Enabled, " R2 사용");

            GUI.Box(new Rect(10, 60, totalWidth, 40), "버튼 1회 이동 각도");
            GUI.Label(new Rect(20, 78, 80, 20), "스텝 (°):");
            string newText = GUI.TextField(new Rect(95, 76, 55, 22), stepText);
            if (newText != stepText)
            {
                stepText = newText;
                if (float.TryParse(stepText, out float v) && v > 0f)
                    stepDeg = v;
            }
            GUI.Label(new Rect(155, 78, 80, 20), $"= {stepDeg:F2}°");

            int qx = 240;
            if (GUI.Button(new Rect(qx + 0, 76, 38, 22), "0.5°"))  { stepDeg = 0.5f; stepText = "0.5"; }
            if (GUI.Button(new Rect(qx + 42, 76, 38, 22), "1°"))   { stepDeg = 1f;   stepText = "1"; }
            if (GUI.Button(new Rect(qx + 84, 76, 38, 22), "5°"))   { stepDeg = 5f;   stepText = "5"; }
            if (GUI.Button(new Rect(qx + 126, 76, 38, 22), "10°")) { stepDeg = 10f;  stepText = "10"; }

            // 속도 조절
            GUI.Box(new Rect(10, 105, totalWidth, 50), "🎚️ 모터 속도 조절");

            GUI.Label(new Rect(20, 122, 75, 20), $"속도:");
            int newVel = (int)GUI.HorizontalSlider(new Rect(75, 128, 200, 18),
                motorVelocity, 0f, 3000f);
            if (newVel != motorVelocity) motorVelocity = newVel;
            GUI.Label(new Rect(280, 122, 60, 20), $"{motorVelocity}");

            GUI.Label(new Rect(345, 122, 75, 20), "가속도:");
            int newAcc = (int)GUI.HorizontalSlider(new Rect(405, 128, 150, 18),
                motorAcceleration, 1f, 254f);
            if (newAcc != motorAcceleration) motorAcceleration = newAcc;
            GUI.Label(new Rect(560, 122, 50, 20), $"{motorAcceleration}");

            int pxStart = 620;
            if (GUI.Button(new Rect(pxStart + 0, 125, 60, 22), "🐢 정밀")) {
                motorVelocity = 400; motorAcceleration = 30;
                ApplySpeedToServer();
            }
            if (GUI.Button(new Rect(pxStart + 65, 125, 60, 22), "🚶 일반")) {
                motorVelocity = 800; motorAcceleration = 50;
                ApplySpeedToServer();
            }
            if (GUI.Button(new Rect(pxStart + 130, 125, 60, 22), "🏃 빠름")) {
                motorVelocity = 1500; motorAcceleration = 100;
                ApplySpeedToServer();
            }

            GUI.color = speedSettingsApplied ? Color.green : Color.yellow;
            if (GUI.Button(new Rect(pxStart + 195, 125, 80, 22),
                speedSettingsApplied ? "✓ 적용됨" : "▶ 적용")) {
                ApplySpeedToServer();
            }
            GUI.color = Color.white;
        }

        void ApplySpeedToServer()
        {
            if (Time.realtimeSinceStartup - lastSpeedSendTime < 0.5f) return;
            lastSpeedSendTime = Time.realtimeSinceStartup;

            string json = $"{{\"type\":\"set_speed\",\"mode\":\"both\"," +
                          $"\"velocity\":{motorVelocity}," +
                          $"\"acceleration\":{motorAcceleration}}}";

            SendToServer(json);
            speedSettingsApplied = true;
            Debug.Log($"[Speed] velocity={motorVelocity}, accel={motorAcceleration} 전송");
        }

        /// <summary>
        /// 🆕 홈으로 이동 명령 전송
        /// </summary>
        void SendGoHome(string mode)
        {
            string json = $"{{\"type\":\"home\",\"mode\":\"{mode}\"}}";
            SendToServer(json);
            Debug.Log($"[Home] {mode} 홈으로 이동 명령 전송");

            // 시뮬도 홈으로
            if (mode == "robot1" || mode == "both") {
                dualManager.robot1?.GoToHome();
                InitSlidersFromRobot(r1Sliders, dualManager.robot1);
            }
            if (mode == "robot2" || mode == "both") {
                dualManager.robot2?.GoToHome();
                InitSlidersFromRobot(r2Sliders, dualManager.robot2);
            }
        }

        /// <summary>
        /// 🆕 안전한 홈포즈 지정 명령 전송
        /// </summary>
        void SendSetHome(string robotName)
        {
            if (dualManager.robot1?.real == null)
            {
                setHomeStatus = "❌ robot1 연결 안 됨";
                return;
            }

            var client = dualManager.robot1.real.socketClient;
            if (client == null)
            {
                setHomeStatus = "❌ SocketClient 없음";
                return;
            }

            // 전용 메서드 사용 + 응답 콜백
            client.RequestSetHome(robotName, (response) => {
                Debug.Log($"[SetHome] {robotName} 응답: {response}");
                setHomeStatus = $"✅ {robotName} 홈포즈 지정 완료";
            });

            setHomeStatus = $"⏳ {robotName} 홈포즈 지정 중...";
            Debug.Log($"[SetHome] {robotName} 홈포즈 지정 요청");
        }
        void SendToServer(string jsonLine)
        {
            if (dualManager.robot1?.real != null)
            {
                var client = dualManager.robot1.real.socketClient;
                if (client != null)
                {
                    if (!jsonLine.EndsWith("\n"))
                        jsonLine += "\n";
                    client.SendRaw(jsonLine);
                }
            }
        }
        /// <summary>
        /// 🆕 홈포즈 지정 확인 다이얼로그 (실수 방지!)
        /// </summary>
        void DrawSetHomeDialog()
        {
            int w = 500;
            int h = 220;
            int x = (Screen.width - w) / 2;
            int y = (Screen.height - h) / 2;

            // 배경 어둡게
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 다이얼로그 박스
            GUI.Box(new Rect(x, y, w, h), "");
            
            // 제목
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 16;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x, y + 10, w, 30), 
                "⚠️ 홈포즈 지정 확인", titleStyle);

            // 본문
            string robotName = setHomeTarget == 0 ? "Robot 1" : "Robot 2";
            string msg = $"현재 {robotName}의 자세를 새 홈 (0°)으로 저장합니다.\n\n" +
                         "✅ 자동 안전 장치:\n" +
                         "  • 캘리브 파일 자동 백업\n" +
                         "  • 범위 초과 시 자동 보정 (autocorrect)\n" +
                         "  • 적용 실패 시 자동 복구\n\n" +
                         "계속하시겠습니까?";
            GUI.Label(new Rect(x + 20, y + 45, w - 40, 130), msg);

            // 상태 메시지
            if (!string.IsNullOrEmpty(setHomeStatus)) {
                GUI.color = Color.yellow;
                GUI.Label(new Rect(x + 20, y + 155, w - 40, 20), setHomeStatus);
                GUI.color = Color.white;
            }

            // 버튼
            int btnY = y + h - 40;
            int btnW = 150;
            int btnH = 30;
            int gap = 20;
            int totalBtnW = btnW * 2 + gap;
            int btnX = x + (w - totalBtnW) / 2;

            GUI.color = Color.red;
            if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "✅ 예, 저장하기")) {
                SendSetHome(setHomeTarget == 0 ? "robot1" : "robot2");
            }
            GUI.color = Color.white;
            
            if (GUI.Button(new Rect(btnX + btnW + gap, btnY, btnW, btnH), "❌ 취소")) {
                showSetHomeDialog = false;
                setHomeStatus = "";
            }
        }

        void DrawRobotPanel(string title, int x, int y, SOArmManager robot,
            float[] sliders, ref float gripper, bool isRobot1, int robotIndex,
            ref Vector2 scrollPos)
        {
            GUI.Box(new Rect(x, y, actualPanelWidth, actualPanelHeight), title);

            string status = robot != null ? robot.StatusMessage : "Not assigned";
            GUI.Label(new Rect(x + 10, y + 22, actualPanelWidth - 20, 35), $"상태: {status}");

            if (robot == null) return;

            int innerX = x + 10;
            int innerY = y + 58;
            int innerW = actualPanelWidth - 20;

            // 하단 버튼이 2줄 필요해서 영역 더 잡음
            int bottomButtonHeight = 70;  // 홈으로 + 홈포즈 지정 두 줄
            int scrollHeight = actualPanelHeight - 65 - bottomButtonHeight;
            int contentHeight = 6 * 70 + 90;

            Rect viewRect = new Rect(innerX, innerY, innerW, scrollHeight);
            Rect contentRect = new Rect(0, 0, innerW - 20, contentHeight);
            scrollPos = GUI.BeginScrollView(viewRect, scrollPos, contentRect);

            int cy = 0;
            int cw = innerW - 25;

            for (int i = 0; i < Mathf.Min(sliders.Length, robot.JointCount); i++)
            {
                float min = robot.GetJointMinAngle(i);
                float max = robot.GetJointMaxAngle(i);

                string realLabel = "";
                if (robot.real != null && robot.real.LastReadAngles != null)
                {
                    var rname = (robot.real.joints != null && i < robot.real.joints.Length)
                        ? robot.real.joints[i].motorName : null;
                    if (!string.IsNullOrEmpty(rname) &&
                        robot.real.LastReadAngles.TryGetValue(rname, out float rv))
                    {
                        realLabel = $"  [실:{rv:F0}°]";
                    }
                }

                GUI.Label(new Rect(0, cy, cw, 18),
                    $"{robot.GetJointName(i)}: {sliders[i]:F1}°{realLabel}");
                cy += 18;

                float newVal = GUI.HorizontalSlider(
                    new Rect(0, cy + 4, cw, 18),
                    sliders[i], min, max);
                cy += 22;

                int btnW = (cw - 4) / 3;
                if (GUI.Button(new Rect(0, cy, btnW, 22), $"−{stepDeg:F1}°"))
                    newVal = Mathf.Clamp(sliders[i] - stepDeg, min, max);
                if (GUI.Button(new Rect(btnW + 2, cy, btnW, 22), "0°"))
                    newVal = 0f;
                if (GUI.Button(new Rect(btnW * 2 + 4, cy, btnW, 22), $"+{stepDeg:F1}°"))
                    newVal = Mathf.Clamp(sliders[i] + stepDeg, min, max);
                cy += 30;

                if (Mathf.Abs(newVal - sliders[i]) > 0.001f)
                {
                    sliders[i] = newVal;
                    dualManager.RouteJointCommand(isRobot1, i, newVal);

                    if (dualManager.controlMode == SOArmDualManager.ControlMode.Mirror)
                    {
                        if (isRobot1) r2Sliders[i] = newVal;
                        else r1Sliders[i] = newVal;
                    }
                }
            }

            GUI.Label(new Rect(0, cy, cw, 18), $"Gripper: {gripper:F0}%");
            cy += 18;
            float newGrip = GUI.HorizontalSlider(new Rect(0, cy + 4, cw, 18),
                gripper, 0f, 100f);
            cy += 22;

            int gBtnW = (cw - 4) / 3;
            if (GUI.Button(new Rect(0, cy, gBtnW, 22), "🤏 닫기")) newGrip = 0f;
            if (GUI.Button(new Rect(gBtnW + 2, cy, gBtnW, 22), "🖐 반")) newGrip = 50f;
            if (GUI.Button(new Rect(gBtnW * 2 + 4, cy, gBtnW, 22), "🖐 열기")) newGrip = 100f;
            cy += 30;

            if (Mathf.Abs(newGrip - gripper) > 0.5f)
            {
                gripper = newGrip;
                dualManager.RouteGripperCommand(isRobot1, gripper);

                if (dualManager.controlMode == SOArmDualManager.ControlMode.Mirror)
                {
                    if (isRobot1) r2Gripper = gripper;
                    else r1Gripper = gripper;
                }
            }

            GUI.EndScrollView();

            // ============== 하단 버튼 영역 (2줄) ==============
            int by = y + actualPanelHeight - bottomButtonHeight + 5;
            int rBtnW = (innerW - 10) / 2;

            // 1줄: 홈으로 이동 + 정지
            if (GUI.Button(new Rect(innerX, by, rBtnW, 25), "🏠 홈으로"))
            {
                SendGoHome(isRobot1 ? "robot1" : "robot2");
            }
            if (GUI.Button(new Rect(innerX + rBtnW + 10, by, rBtnW, 25), "⏸ 정지"))
                robot.StopMotion();

            // 🆕 2줄: 홈포즈 지정 버튼
            GUI.color = new Color(1f, 0.7f, 0.3f); // 주황색 (위험 표시)
            if (GUI.Button(new Rect(innerX, by + 30, innerW - 10, 25), 
                "⚙️ 현재 자세를 홈으로 지정 (안전 모드)"))
            {
                showSetHomeDialog = true;
                setHomeTarget = isRobot1 ? 0 : 1;
                setHomeStatus = "";
            }
            GUI.color = Color.white;
        }

        void DrawBottomBar()
        {
            int y = Screen.height - 50;
            int totalWidth = Screen.width - 20;
            int btnW = (totalWidth - 30) / 3;
            int btnH = 35;
            int x = 10;
            int gap = 10;

            if (GUI.Button(new Rect(x, y, btnW, btnH), "🏠 전체 홈으로"))
            {
                SendGoHome("both");
            }
            // 🛑 비상정지 / 해제 — 상태에 따라 버튼이 바뀐다
            if (dualManager.EmergencyStopped)
            {
                var prev = GUI.color;
                GUI.color = new Color(0.4f, 1f, 0.4f);
                if (GUI.Button(new Rect(x + btnW + gap, y, btnW, btnH), "▶ 정지 해제"))
                    dualManager.ReleaseStopAll();
                GUI.color = prev;
            }
            else
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.45f, 0.45f);
                if (GUI.Button(new Rect(x + btnW + gap, y, btnW, btnH), "🛑 비상정지 (ESC)"))
                    dualManager.StopAll();
                GUI.color = prev;
            }

            if (GUI.Button(new Rect(x + (btnW + gap) * 2, y, btnW, btnH), "🔌 재연결"))
                dualManager.ConnectAll();
        }
    }
}
