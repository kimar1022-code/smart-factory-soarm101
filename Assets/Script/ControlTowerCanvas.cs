using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SOArmControl
{
    /// <summary>
    /// SO-ARM SYSTEM 관제 패널.
    ///
    /// 【가장 중요한 구조 — 생성과 배선의 분리】
    ///   BuildUI() 는 **모양만** 만든다. 버튼 동작은 절대 여기서 붙이지 않는다.
    ///   에디터에서 코드로 붙인 onClick 람다는 유니티가 직렬화하지 않아
    ///   Play 에 들어가는 순간 전부 사라진다. 그래서 "눌러도 아무 일도 안 나는"
    ///   화면이 된다. 배선은 반드시 실행 시(Awake)에 이름으로 찾아서 붙인다.
    ///
    /// 【구성 — 손그림 계획대로】
    ///   TOP    : SO-ARM System | 수동모드 | R1 only | R2 only | Mirror | 비상정지 | 홈 | Recorder
    ///   LEFT   : ROBOT 1 / ROBOT 2 — J1~J5 (스텝버튼 + 슬라이더 + 각도입력) + 그리퍼(슬라이더 + 닫기/반/열기)
    ///   CENTER : 2×2 — TOP VIEW | SIDE VIEW / FRONT VIEW | ROBOT STATUS
    ///   RIGHT  : Recorder 를 누르면 열리는 루틴 패널
    /// </summary>
    [DisallowMultipleComponent]
    public class ControlTowerCanvas : MonoBehaviour
    {
        static readonly Color Bg       = new Color32(0x0A, 0x0A, 0x0C, 0xFB);
        static readonly Color PanelBg  = new Color32(0x14, 0x15, 0x18, 0xFB);
        static readonly Color SubBg    = new Color32(0x1E, 0x1F, 0x24, 0xFF);
        static readonly Color Accent   = new Color32(0xFF, 0xC4, 0x00, 0xFF);
        static readonly Color TextMain = new Color32(0xFF, 0xFF, 0xFF, 0xFF);   // 순백 — 어두운 배경에서 또렷하게
        static readonly Color TextDim  = new Color32(0xB0, 0xB4, 0xBC, 0xFF);   // 기존 0x86 은 너무 어두워 안 보였다
        static readonly Color Bad      = new Color32(0xFF, 0x45, 0x36, 0xFF);
        static readonly Color Warn     = new Color32(0xFF, 0x8A, 0x1E, 0xFF);
        static readonly Color Edge     = new Color32(0x2C, 0x2E, 0x36, 0xFF);   // 패널 테두리

        [Header("연결 (비우면 자동 탐색)")]
        public SOArmDualManager dualManager;
        public RobotViewCamera viewCamera;
        public RecordManager recordManager;

        [Header("표시")]
        public string projectName = "SO-ARM SYSTEM";
        public Vector2 referenceResolution = new Vector2(1920, 1080);
        public int baseFontSize = 24;
        public int titleFontSize = 30;

        [Header("조작")]
        [Tooltip("−/+ 버튼이 한 번에 움직이는 각도")]
        public float stepDeg = 5f;

        [Header("3면 뷰 방향")]
        [Tooltip("앞 뷰를 반대편에서 본다. 좌우가 뒤집혀 보이면 켠다.")]
        public bool flipFront = false;
        [Tooltip("옆 뷰를 반대쪽에서 본다. 좌우가 뒤집혀 보이면 켠다.")]
        public bool flipSide;
        [Tooltip("위 뷰를 180° 돌린다. 팔이 화면 아래로 향하면 켠다.")]
        public bool topFlip;
        [Tooltip("앞·옆 뷰를 살짝 위에서 내려다보는 정도.\n" +
                 "참고 사진이 완전 수평이라 기본은 0 이다.")]
        [Range(0f, 0.6f)] public float viewTilt = 0f;

        [Header("경고 임계값")]
        [Tooltip("주황으로 바뀌는 온도. 여기서 +10°C 를 더 넘으면 빨강.\n\n" +
                 "서보에서 읽은 Max_Temperature_Limit 은 70°C 다.\n" +
                 "예전 기본값 50 은 근거 없이 정한 숫자여서, 정상 작동 중인 41°C 에도\n" +
                 "곧 경고가 뜨는 상태였다. 실제 한계에서 역산해 55/65 로 둔다.")]
        public int tempWarn = 55;      // 빨강 65 — 서보 한계 70 보다 5 여유
        public float voltWarn = 11.0f;

        [Header("동작")]
        public bool hideLegacyUI = true;

        // ── 상태에 따라 바뀌는 문구 ─────────────────────────────
        //
        // 이 라벨들은 상태(연결/비상/수동/재생)에 따라 코드가 매 프레임 다시 쓴다.
        // 그래서 화면에서 글자를 직접 고쳐도 Play 하는 순간 되돌아간다.
        // 여기서 바꾸면 그대로 유지된다.
        [Header("문구 — 상태에 따라 바뀌는 것들")]
        public string wordEstop = "비상정지";
        public string wordEstopRelease = "정지 해제";
        [Space(2)]
        public string wordTeach = "수동모드";
        public string wordTeachOn = "수동모드 ON";
        public string wordTeachShort = "✋ 수동";
        public string wordHold = "🔒 유지";
        [Space(2)]
        public string wordRecordOpen = "Recorder ◀";
        public string wordRecordClosed = "Recorder ▶";
        [Space(2)]
        public string wordPlay = "재생";
        public string wordPlayAll = "ALL";
        public string wordStop = "정지";
        public string wordLoopStart = "반복 시작";
        public string wordLoopEnd = "반복 끝";
        [Space(2)]
        public string wordOnline = "ONLINE";
        public string wordOffline = "OFFLINE";
        public string wordConn = "연결";
        public string wordDisconn = "끊김";

        [Header("안전 표시")]
        [Tooltip("수동모드일 때 버튼 테두리에 두르는 노랑·검정 줄무늬 두께(px)")]
        [Range(2, 20)] public int hazardBorder = 6;

        // ── 아이콘 ──────────────────────────────────────────────
        // CleanFlatIcon 은 파일명이 번호식(icon_line_arrow_37)이라 코드가 고를 수 없다.
        // 인스펙터에서 직접 끼우고, 비워 두면 글자만 나온다.
        // Assets/UI/Icons/ 에 들여놨다. 임포트 설정을 Sprite(2D and UI) 로 바꿔야 끼울 수 있다.
        [Header("아이콘 (비우면 글자만)")]
        public Sprite iconTeach, iconR1, iconR2, iconMirror, iconEstop, iconHome, iconRecord;
        [Space(4)]
        public Sprite iconPlay, iconNew, iconSave, iconLoad, iconDelete;
        [Space(4)]
        public Sprite iconPlus, iconMinus, iconGripOpen, iconGripClose;
        [Space(4)]
        [Tooltip("TOP/SIDE/FRONT VIEW 제목 앞")] public Sprite iconView;
        [Tooltip("ROBOT STATUS 제목 앞")] public Sprite iconRobotStatus;
        [Tooltip("SO-ARM SYSTEM 제목 앞")] public Sprite iconSystem;
        [Tooltip("왼쪽 ROBOT 1 / ROBOT 2 카드 제목 앞")] public Sprite iconRobotCard;
        [Tooltip("속도 라벨 앞")] public Sprite iconSpeed;
        [Tooltip("가속 라벨 앞")] public Sprite iconAccel;
        [Tooltip("반복 시작/끝 버튼")] public Sprite iconLoop;
        [Tooltip("스텝 추가 (+R1 / +R2 / +둘 다 / +대기)")] public Sprite iconAdd;

        [Tooltip("버튼 안 아이콘 크기(px). 0 이면 버튼 높이에 맞춘다.")]
        public int iconSize = 26;

        const string RootName = "ControlTowerCanvas";
        const int TopH = 66, LeftW = 470, RecW = 430;
        const string NA = "--";
        static readonly string[] PKeys = { "Top", "Side", "Front" };

        Font uiFont;
        Transform root;
        readonly Dictionary<string, Text> texts = new Dictionary<string, Text>();
        readonly Dictionary<string, Slider> sliders = new Dictionary<string, Slider>();
        readonly Dictionary<string, InputField> inputs = new Dictionary<string, InputField>();
        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        GameObject recordPanel;
        bool recordOpen;

        int selectedStep;

        // ── 카티시안 면 ─────────────────────────────────────────
        // 카드마다 따로 기억한다. R1 은 관절, R2 는 좌표로 두고 쓸 수 있어야 한다.
        static readonly float[] CartSteps = { 1f, 5f, 10f, 50f };
        readonly Dictionary<string, bool> cartMode = new Dictionary<string, bool>();
        readonly Dictionary<string, int> cartStep = new Dictionary<string, int>();

        /// <summary>Rx/Ry/Rz 한 번에 도는 각도. 5축이라 크게 주면 서버가 거절한다.</summary>
        [Tooltip("회전 버튼 한 번에 도는 각도(도)")]
        public float rotStepDeg = 5f;

        // 로봇마다 하나씩. 하나를 useRobot2 만 바꿔 돌려 쓰면, 응답이 늦게 올 때
        // 콜백이 엉뚱한 로봇에 적용된다(SOArmSocketClient 는 FIFO 매칭이다).
        SOArmIKController ik1, ik2;

        /// <summary>목록에 보이는 첫 스텝의 인덱스. 줄 클릭을 실제 스텝 번호로 바꿀 때 쓴다.</summary>
        int routineFrom;
        const int RoutineRows = 13;

        // ── 불러오기 창 ──
        // "가져오기" 가 목록의 첫 파일을 말없이 열던 때는 루틴이 하나뿐이라 넘어갔지만,
        // 파일이 늘면 원하는 것을 고를 방법이 없고 열자마자 작업 중이던 스텝이 사라진다.
        // 골라서 열고, 열기 전에 무엇이 사라지는지 보여 준다.
        bool loadOpen;
        int loadFrom, loadSel;
        string[] loadFiles = new string[0];
        const int LoadRows = 10;

        bool wired, uiReady, rangesApplied;
        int readyFrames;
        float lastSpeedSend, lastStatusPoll;
        bool prevC1, prevC2, prevE, prevT1, prevT2, primed;

        [Serializable]
        class StatusMsg
        {
            public float r1_temp = -1, r1_volt = -1, r1_load = -1;
            public float r2_temp = -1, r2_volt = -1, r2_load = -1;
        }
        StatusMsg status = new StatusMsg();

        void Awake()
        {
            if (dualManager == null) dualManager = FindAnyObjectByType<SOArmDualManager>();
            if (viewCamera == null) viewCamera = FindAnyObjectByType<RobotViewCamera>();
            if (recordManager == null) recordManager = FindAnyObjectByType<RecordManager>();
            CacheBindings();
            AdoptSceneWords();
            WireUp();
            ApplyLegacyVisibility();
        }

        /// <summary>
        /// 화면에 이미 적혀 있는 문구를 기본값으로 삼는다.
        ///
        /// 에디터에서 라벨을 고쳐 놓아도, 상태에 따라 바뀌는 칸은 코드가 매 프레임
        /// 다시 쓰기 때문에 Play 하는 순간 되돌아갔다.
        /// 시작 시점의 상태는 전부 기본(비상 아님·수동 아님·정지 중)이므로,
        /// 그때 화면에 보이던 글자를 그대로 기본 문구로 받아들이면
        /// **Play 해도 편집한 화면 그대로** 보인다.
        ///
        /// 반대 상태(정지 해제·수동모드 ON 등)의 문구는 에디터에서 볼 수 없으므로
        /// 인스펙터의 「문구」 항목을 쓴다.
        /// </summary>
        void AdoptSceneWords()
        {
            Take("BtnEstopLabel",    ref wordEstop);
            Take("BtnTeachAllLabel", ref wordTeach);
            Take("BtnRecordLabel",   ref wordRecordClosed);
            Take("BtnPlayLabel",     ref wordPlay);
            Take("BtnPlayAllLabel",  ref wordPlayAll);
            Take("BtnLoopLabel",     ref wordLoopStart);

            // 로봇 상태는 처음엔 연결 전이라 OFFLINE / 유지 / 끊김 이 떠 있다
            Take("R1State", ref wordOffline);
            Take("R1Teach", ref wordHold);
            Take("S1Conn",  ref wordDisconn);
        }

        void Take(string key, ref string target)
        {
            if (texts.TryGetValue(key, out var t) && t != null && !string.IsNullOrWhiteSpace(t.text))
                target = t.text;
        }

        bool IsComplete => root != null && root.Find("Center") != null;

        void ApplyLegacyVisibility()
        {
            if (!hideLegacyUI || !IsComplete) return;
            var m = FindAnyObjectByType<SmartFactoryUI_v3_4>();
            if (m != null && m.enabled) { m.enabled = false; Debug.Log("[관제] 기존 IMGUI 화면 끔"); }
            var r = FindAnyObjectByType<SmartFactoryRecordUI>();
            if (r != null && r.enabled) r.enabled = false;
        }

        void Update()
        {
            if (root == null) { CacheBindings(); WireUp(); ApplyLegacyVisibility(); if (root == null) return; }
            if (!uiReady && Application.isPlaying && ++readyFrames > 3) uiReady = true;
            PollStatus();
            Bind();
            UpdatePreviewCams();
            WatchChanges();
        }

        // ══════════════════════════════════════════════════════════
        // 배선 — 실행 시 이름으로 찾아 붙인다
        // ══════════════════════════════════════════════════════════

        void WireUp()
        {
            if (wired || root == null) return;
            wired = true;

            // 상단 바
            OnClick("BtnTeachAll", () => { var d = dualManager; if (d == null) return;
                bool on = !(d.robot1Teach || d.robot2Teach);
                d.SetTeach(true, on); d.SetTeach(false, on); Log("수동모드 " + (on ? "ON" : "OFF")); });
            OnClick("BtnR1Only", () => Mode(SOArmDualManager.ControlMode.Independent, true));
            OnClick("BtnR2Only", () => Mode(SOArmDualManager.ControlMode.Independent, false));
            OnClick("BtnMirror", () =>
            {
                if (dualManager == null) return;
                // ⚠️ R1 only / R2 only 를 거쳤다면 한쪽 채널이 꺼져 있다.
                //    Mirror 는 두 로봇을 같이 쓰는 모드이므로 반드시 둘 다 되살린다.
                //    안 그러면 Mirror 인데 한 대만 움직여서 고장으로 오해한다.
                dualManager.robot1Enabled = true;
                dualManager.robot2Enabled = true;
                dualManager.ChangeMode(SOArmDualManager.ControlMode.Mirror);
                Log("Mirror 모드 — 두 로봇 모두 활성");
            });
            OnClick("BtnEstop", () => { var d = dualManager; if (d == null) return;
                if (d.EmergencyStopped) { d.ReleaseStopAll(); Log("비상정지 해제"); } else { d.StopAll(); Log("비상정지"); } });
            OnClick("BtnHome", () =>
            {
                if (dualManager == null) return;
                // ⚠️ 수동모드에서는 서버가 일부 관절의 토크를 끈다.
                //    토크가 꺼진 서보는 Goal_Position 을 무시하므로 홈 명령이 그냥 씹힌다.
                //    "홈을 눌렀는데 안 간다" 의 정체가 이것이다. 먼저 수동모드를 끈다.
                if (EndTeachIfOn("홈으로 이동")) return;
                dualManager.GoToHomeAll();
                Log("홈으로 이동");
            });
            OnClick("BtnRecord", ToggleRecord);
            // 레코더 안의 ✕ 닫기는 없앴다 — 위 Recorder 버튼이 여닫는 토글이라 중복이었다.

            // 스텝 크기
            foreach (var v in new[] { 0.5f, 1f, 5f, 10f })
            { float sv = v; OnClick("BtnStep" + v.ToString(CultureInfo.InvariantCulture), () => { stepDeg = sv; Log($"스텝 {sv}°"); }); }

            // 로봇별
            WireRobot("R1", true);
            WireRobot("R2", false);

            EnsureIk();
            WireCart("R1");
            WireCart("R2");

            // 카메라 프리셋 버튼은 두지 않는다. 계획 그림의 4칸(위/옆/앞/상태)이
            // 곧 시점이고, 메인 카메라 화면은 관제에 노출되지 않는다.

            // 속도
            OnSlider("VelSlider", _ => SendSpeed());
            OnSlider("AccSlider", _ => SendSpeed());

            // 루틴
            OnClick("BtnAddR1", () => AddStep("robot1"));
            OnClick("BtnAddR2", () => AddStep("robot2"));
            OnClick("BtnAddBoth", () => AddStep("both"));
            OnClick("BtnAddWait", () => { if (recordManager != null) recordManager.AddWaitStep(1f); });
            OnClick("BtnLoop", () =>
            {
                if (recordManager == null) return;
                if (LoopOpen) { recordManager.AddLoopEndStep(); Log("반복 끝"); }
                else { recordManager.AddLoopStartStep(3); Log("반복 시작 (3회)"); }
            });
            // 목록 줄 클릭 = 선택. 화살표 버튼을 대신한다.
            for (int i = 0; i < RoutineRows; i++)
            {
                int row = i;
                OnClick("RoutineRow" + i, () => selectedStep = routineFrom + row);
            }
            OnClick("BtnMoveUp", () => { if (recordManager != null) { recordManager.MoveStepUp(selectedStep); selectedStep--; } });
            OnClick("BtnMoveDn", () => { if (recordManager != null) { recordManager.MoveStepDown(selectedStep); selectedStep++; } });
            OnClick("BtnDel", () => { if (recordManager != null) recordManager.RemoveStep(selectedStep); });
            // 재생 = 선택한 스텝 하나만. 만드는 중에 그 자세만 확인할 때 쓴다.
            OnClick("BtnPlay", () =>
            {
                var r = recordManager; if (r == null) return;
                if (r.IsPlaying) { r.StopPlayback(); Log("정지"); return; }
                if (EndTeachIfOn("재생")) return;
                r.PlayStep(selectedStep);
                Log($"Step {selectedStep + 1} 단독 실행");
            });

            // ALL = 루틴 전체 재생 (반복 구간 포함)
            OnClick("BtnPlayAll", () =>
            {
                var r = recordManager; if (r == null) return;
                if (r.IsPlaying) { r.StopPlayback(); Log("정지"); return; }
                if (EndTeachIfOn("전체 재생")) return;
                r.StartPlayback();
                Log("전체 재생");
            });
            OnClick("BtnNew", () => { if (recordManager != null) { recordManager.NewProject("Untitled"); selectedStep = 0; } });
            OnClick("BtnSave", () => { if (recordManager?.CurrentProject != null) recordManager.SaveProject(recordManager.CurrentProject.projectName); });
            OnClick("BtnLoad", OpenLoadPicker);

            // 불러오기 창 — 줄을 눌러 고르고, «불러오기» 로 확정한다.
            // 줄 클릭만으로 바로 열면 잘못 눌렀을 때 작업이 날아간다.
            for (int i = 0; i < LoadRows; i++)
            {
                int row = i;
                OnClick("LoadRow" + i, () => loadSel = loadFrom + row);
            }
            OnClick("BtnLoadPrev",   () => loadSel--);
            OnClick("BtnLoadNext",   () => loadSel++);
            OnClick("BtnLoadOk",     ConfirmLoad);
            OnClick("BtnLoadCancel", CloseLoadPicker);

            // 이름 입력 — Enter 를 치면 프로젝트 이름이 바뀌고 저장 경로도 따라 바뀐다
            OnSubmit("RoutineNameIn", s =>
            {
                if (recordManager == null) return;
                s = (s ?? "").Trim();
                if (s.Length == 0) { Log("이름이 비어 있어 바꾸지 않았습니다"); return; }
                recordManager.SetProjectName(s);
                Log($"루틴 이름 → {s}");
            });
        }

        /// <summary>
        /// 수동모드가 켜져 있으면 끄고 true 를 돌려준다(이번 클릭은 소비).
        ///
        /// 수동모드에서는 서버가 pan·wrist 의 토크를 끈다. 토크가 꺼진 서보는
        /// Goal_Position 쓰기를 무시하므로, 홈 이동이나 루틴 재생 명령이 조용히 씹힌다.
        /// "그리퍼만 따라오고 자세는 안 따라온다", "홈을 눌러도 안 간다" 가 모두 이 증상이다.
        /// (그리퍼는 토크를 유지하므로 혼자 잘 따라온다)
        ///
        /// 몰래 끄고 바로 실행하면 팔이 갑자기 움직여 놀라므로, 끄기만 하고 한 번 멈춘다.
        /// </summary>
        bool EndTeachIfOn(string what)
        {
            var d = dualManager;
            if (d == null) return false;
            if (!d.robot1Teach && !d.robot2Teach && !d.teachMirror) return false;

            d.SetTeach(true, false);
            d.SetTeach(false, false);
            if (d.teachMirror) d.SetTeachMirror(false);

            Log($"수동모드를 껐습니다. 토크가 돌아왔으니 «{what}» 를 다시 누르세요.");
            return true;
        }

        void Mode(SOArmDualManager.ControlMode m, bool r1)
        {
            if (dualManager == null) return;
            dualManager.ChangeMode(m);
            dualManager.robot1Enabled = r1;
            dualManager.robot2Enabled = !r1;
            Log(r1 ? "R1 only" : "R2 only");
        }

        void WireRobot(string pre, bool isR1)
        {
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                OnSlider($"{pre}J{i}", v => { if (uiReady && dualManager != null) dualManager.RouteJointCommand(isR1, idx, v); });
                OnClick($"{pre}J{i}Minus", () => Nudge(pre, idx, isR1, -stepDeg));
                OnClick($"{pre}J{i}Plus", () => Nudge(pre, idx, isR1, +stepDeg));
                OnSubmit($"{pre}J{i}In", s => TypeAngle(pre, idx, isR1, s));
            }
            OnSlider(pre + "Grip", v => { if (uiReady && dualManager != null) dualManager.RouteGripperCommand(isR1, v); });
            OnClick(pre + "GripClose", () => SetGrip(pre, isR1, 0f));
            OnClick(pre + "GripHalf", () => SetGrip(pre, isR1, 50f));
            OnClick(pre + "GripOpen", () => SetGrip(pre, isR1, 100f));
        }

        /// <summary>−/+ 버튼. 슬라이더 값을 옮기면 onValueChanged 로 명령이 나간다.</summary>
        void Nudge(string pre, int i, bool isR1, float delta)
        {
            if (!sliders.TryGetValue($"{pre}J{i}", out var s) || s == null) return;
            s.value = Mathf.Clamp(s.value + delta, s.minValue, s.maxValue);
        }

        /// <summary>입력창에 각도를 쳐 넣으면 그 각도로 간다. 범위를 벗어나면 잘라서 넣는다.</summary>
        void TypeAngle(string pre, int i, bool isR1, string txt)
        {
            if (!sliders.TryGetValue($"{pre}J{i}", out var s) || s == null) return;
            if (!float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            { Log($"각도 해석 실패: '{txt}'"); return; }
            s.value = Mathf.Clamp(v, s.minValue, s.maxValue);
            Log($"{pre} J{i + 1} → {s.value:F1}°");
        }

        void SetGrip(string pre, bool isR1, float pct)
        {
            if (sliders.TryGetValue(pre + "Grip", out var s) && s != null) s.value = pct;
            else if (dualManager != null) dualManager.RouteGripperCommand(isR1, pct);
        }

        // ══════════════════════════════════════════════════════════
        // 카티시안 면 — 스위치 · 이동 · 회전
        //
        // 여기서 서버로 나가는 건 **계산 요청**뿐이다. 나온 관절 각도를 실제로
        // 넣는 건 SOArmIKController 가 SOArmManager 를 거쳐서 한다. 그래야
        // 속도 제한 · 소프트 리밋 · 비상정지 · 그리퍼 안전 게이트가 그대로 걸린다.
        // ══════════════════════════════════════════════════════════

        void EnsureIk()
        {
            ik1 = MakeIk("IK_R1", false, dualManager?.robot1);
            ik2 = MakeIk("IK_R2", true, dualManager?.robot2);
        }

        SOArmIKController MakeIk(string name, bool useRobot2, SOArmManager m)
        {
            var t = transform.Find(name);
            var go = t != null ? t.gameObject : new GameObject(name);
            if (t == null) go.transform.SetParent(transform, false);

            var c = go.GetComponent<SOArmIKController>();
            if (c == null) c = go.AddComponent<SOArmIKController>();

            c.dualManager = dualManager;
            c.useRobot2 = useRobot2;
            // 소켓을 명시해 둔다. 비워 두면 씬에서 아무 거나 잡는데, 이 프로젝트는
            // 로봇마다 소켓이 따로라 어느 쪽을 잡았는지가 로그에서 안 보인다.
            // (ik 명령 자체는 mode 가 없어 어느 소켓으로 가도 계산은 같다)
            if (m != null && m.real != null && m.real.socketClient != null)
                c.socketClient = m.real.socketClient;

            return c;
        }

        SOArmIKController IkOf(string pre) => pre == "R1" ? ik1 : ik2;

        void WireCart(string pre)
        {
            OnClick(pre + "ModeSw", () => ToggleCartMode(pre));

            float Step() => CartSteps[cartStep.TryGetValue(pre, out var i) ? i : 1] * 0.001f;

            OnClick(pre + "CartXPlus",  () => Jog(pre, 0, +Step()));
            OnClick(pre + "CartXMinus", () => Jog(pre, 0, -Step()));
            OnClick(pre + "CartYPlus",  () => Jog(pre, 1, +Step()));
            OnClick(pre + "CartYMinus", () => Jog(pre, 1, -Step()));
            OnClick(pre + "CartZPlus",  () => Jog(pre, 2, +Step()));
            OnClick(pre + "CartZMinus", () => Jog(pre, 2, -Step()));

            string[] ax = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int a = i;
                OnClick($"{pre}Rot{ax[i]}Plus",  () => Rot(pre, a, +rotStepDeg));
                OnClick($"{pre}Rot{ax[i]}Minus", () => Rot(pre, a, -rotStepDeg));
            }

            OnClick(pre + "CartRead", () => { var k = IkOf(pre); if (k != null) k.RefreshCurrentTcp(); });

            for (int i = 0; i < CartSteps.Length; i++)
            {
                int idx = i;
                OnClick($"{pre}CartStep{i}", () => { cartStep[pre] = idx; Log($"{pre} 스텝 {CartSteps[idx]:0}mm"); });
            }
        }

        void ToggleCartMode(string pre)
        {
            bool on = !(cartMode.TryGetValue(pre, out var v) && v);
            cartMode[pre] = on;

            var jg = FindByName(pre + "JointGroup");
            var cg = FindByName(pre + "CartGroup");
            if (jg != null) jg.SetActive(!on);
            if (cg != null) cg.SetActive(on);

            // 좌표 면으로 들어올 때는 반드시 현재 위치를 먼저 읽는다.
            // 묵은 목표를 들고 있으면 첫 버튼 한 번에 팔이 그리로 달려간다.
            if (on)
            {
                var k = IkOf(pre);
                // ⚠️ 순서가 중요하다. 먼저 스냅하면 아직 안 읽은 값(0,0,0)을 목표로
                //    잡아 버리고, RefreshCurrentTcp 는 HasTarget 이 이미 true 라
                //    목표를 안 고친다. 그 상태로 버튼을 누르면 팔이 원점으로 달려간다.
                //    읽고 나서 스냅해야 한다.
                if (k != null) k.RefreshCurrentTcp(ok => { if (ok) k.SnapTargetToCurrent(); });
            }
            Log($"{pre} {(on ? "카티시안" : "조인트")} 모드");
        }

        /// <summary>이름으로 찾는다. 꺼져 있는 것도 포함해야 면 전환이 된다.</summary>
        GameObject FindByName(string name)
        {
            if (root == null) return null;
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt.gameObject;
            return null;
        }

        void Jog(string pre, int axis, float deltaM)
        {
            var k = IkOf(pre);
            if (k == null) return;
            if (!k.HasTarget) { k.RefreshCurrentTcp(); return; }

            // ⚠️ 응답을 기다리는 중이면 목표를 **밀지도 않는다**.
            //    밀어 놓고 SolveAndApply 가 inFlight 로 돌아가 버리면 그 거리가
            //    목표에 남는다. 연타하면 그게 쌓였다가 한 번에 튄다.
            if (k.IsBusy) return;

            k.NudgeTarget(axis, deltaM);
            k.SolveAndApply();
        }

        void Rot(string pre, int axis, float deltaDeg)
        {
            var k = IkOf(pre);
            if (k == null) return;
            if (!k.HasTarget) { k.RefreshCurrentTcp(); return; }
            if (k.IsBusy) return;
            k.JogRotation(axis, deltaDeg);
        }

        void ToggleRecord()
        {
            recordOpen = !recordOpen;
            if (recordPanel != null) recordPanel.SetActive(recordOpen);
            // 불러오기 창을 띄운 채 리코더를 닫으면, 다시 열었을 때 목록이 덮여 있다
            if (!recordOpen) CloseLoadPicker();
            if (dualManager != null) dualManager.SetRecordMode(recordOpen);
            Log(recordOpen ? "Recorder 열림" : "Recorder 닫힘");
        }

        void OnClick(string n, UnityEngine.Events.UnityAction a)
        { if (buttons.TryGetValue(n, out var b) && b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(a); } }

        void OnSlider(string n, UnityEngine.Events.UnityAction<float> a)
        { if (sliders.TryGetValue(n, out var s) && s != null) { s.onValueChanged.RemoveAllListeners(); s.onValueChanged.AddListener(a); } }

        void OnSubmit(string n, UnityEngine.Events.UnityAction<string> a)
        { if (inputs.TryGetValue(n, out var f) && f != null) { f.onEndEdit.RemoveAllListeners(); f.onEndEdit.AddListener(a); } }

        // ══════════════════════════════════════════════════════════
        // 바인딩
        // ══════════════════════════════════════════════════════════

        void CacheBindings()
        {
            texts.Clear(); sliders.Clear(); inputs.Clear(); buttons.Clear(); orbits.Clear(); images.Clear();
            root = transform.Find(RootName);
            if (root == null) return;

            // LED·게이지는 이름이 고유한 것만 담는다. Edge/Rule 처럼 반복되는 장식은 제외.
            foreach (var im in root.GetComponentsInChildren<Image>(true))
                if ((im.name.EndsWith("Led") || im.name.EndsWith("Bar")) && !images.ContainsKey(im.name))
                    images[im.name] = im;

            foreach (var t in root.GetComponentsInChildren<Text>(true)) if (!texts.ContainsKey(t.name)) texts[t.name] = t;
            foreach (var s in root.GetComponentsInChildren<Slider>(true)) if (!sliders.ContainsKey(s.name)) sliders[s.name] = s;
            foreach (var f in root.GetComponentsInChildren<InputField>(true)) if (!inputs.ContainsKey(f.name)) inputs[f.name] = f;
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (!buttons.ContainsKey(b.name)) buttons[b.name] = b;

            var rp = root.Find("RecordPanel");
            recordPanel = rp != null ? rp.gameObject : null;
            if (recordPanel != null) recordPanel.SetActive(recordOpen);

            // 불러오기 창은 항상 닫힌 상태에서 시작한다.
            // 화면을 다시 만들면 목록이 비어 있는데 창만 떠 있을 수 있다.
            loadOpen = false;
            var lp = LoadPanel; if (lp != null) lp.gameObject.SetActive(false);
        }

        void Set(string k, string v, Color? c = null)
        { if (texts.TryGetValue(k, out var t) && t != null) { t.text = v; if (c.HasValue) t.color = c.Value; } }

        void Bind()
        {
            if (dualManager == null) return;

            // ⚠️ TopProject 는 여기서 건드리지 않는다.
            //    고정 문구인데 매 프레임 덮어쓰면, 손으로 고친 제목이 Play 하는 순간 되돌아간다.
            //    제목은 생성할 때 한 번만 넣고, 이후엔 화면에서 직접 고치면 그대로 남는다.

            bool c1 = Live(dualManager.robot1), c2 = Live(dualManager.robot2);

            Set("BtnEstopLabel", dualManager.EmergencyStopped ? wordEstopRelease : wordEstop);
            Set("BtnRecordLabel", recordOpen ? wordRecordOpen : wordRecordClosed);
            // 수동모드는 팔이 손에 반응하는 상태다. 켜진 걸 놓치면 위험하므로
            // 켜졌을 때만 산업 안전 표시(노랑·검정 사선)를 깔아 멀리서도 보이게 한다.
            bool teachOn = dualManager.robot1Teach || dualManager.robot2Teach;
            Set("BtnTeachAllLabel", teachOn ? wordTeachOn : wordTeach, Accent);
            SetHazard("BtnTeachAll", teachOn);

            // 어떤 로봇을 잡고 있는지도 안전 정보다.
            // R1만 켜 둔 줄 모르고 R2를 움직이려다 아무 반응이 없으면 고장으로 오해한다.
            bool mirror = dualManager.controlMode == SOArmDualManager.ControlMode.Mirror;
            SetHazard("BtnMirror", mirror);
            SetHazard("BtnR1Only", !mirror && dualManager.robot1Enabled && !dualManager.robot2Enabled);
            SetHazard("BtnR2Only", !mirror && dualManager.robot2Enabled && !dualManager.robot1Enabled);

            Set("StepNow", $"{stepDeg:0.#}°", Accent);

            BindRobot("R1", dualManager.robot1, c1, dualManager.robot1Teach);
            BindRobot("R2", dualManager.robot2, c2, dualManager.robot2Teach);
            BindStatus();

            if (sliders.TryGetValue("VelSlider", out var vs) && vs != null) Set("VelValue", $"{vs.value:F0}", Accent);
            if (sliders.TryGetValue("AccSlider", out var acs) && acs != null) Set("AccValue", $"{acs.value:F0}", Accent);

            if (recordOpen) BindRoutine();
            if (recordOpen && loadOpen) BindLoadPicker();
        }

        void BindRobot(string p, SOArmManager m, bool conn, bool teach)
        {
            Set(p + "State", conn ? wordOnline : wordOffline, conn ? Accent : Bad);
            SetLed(p + "CardLed", conn ? Accent : Bad);
            Set(p + "Teach", teach ? wordTeachShort : wordHold, teach ? Accent : TextDim);

            bool live = m != null && conn;
            for (int i = 0; i < 5; i++)
            {
                // 입력창에 **현재 각도 하나만** 띄운다.
                // 연결돼 있으면 실측값, 아니면 슬라이더 목표값을 쓴다.
                // 편집 중일 때는 건드리지 않는다 — 덮어쓰면 글자를 지울 수가 없다.
                if (inputs.TryGetValue($"{p}J{i}In", out var f) && f != null && !f.isFocused)
                {
                    float shown = live ? SafeAngle(m, i)
                                : (sliders.TryGetValue($"{p}J{i}", out var s) && s != null ? s.value : 0f);
                    f.text = shown.ToString("F1", CultureInfo.InvariantCulture);
                }
            }
            Set($"{p}GripValue", live ? $"{SafeGrip(m),5:F0}%" : NA, live ? TextMain : TextDim);

            if (live && !rangesApplied) ApplyJointRanges(p, m);

            BindCart(p);
        }

        void BindCart(string p)
        {
            bool cart = cartMode.TryGetValue(p, out var v) && v;
            // 스위치 글자는 **지금 무엇을 보고 있는지**를 쓴다. 누르면 바뀔 것을
            // 쓰면(예: "카티시안") 지금 상태와 반대라 매번 헷갈린다.
            // Btn 이 만드는 글자의 이름은 "<버튼이름>Label" 이다 (Btn 헬퍼 참고).
            Set(p + "ModeSwLabel", cart ? "카티시안" : "조인트", cart ? Color.black : Accent);
            if (buttons.TryGetValue(p + "ModeSw", out var sw) && sw != null)
            {
                var img = sw.GetComponent<Image>();
                if (img != null) img.color = cart ? Accent : SubBg;
            }
            if (!cart) return;

            Set(p + "CartStepNow", $"{CartSteps[cartStep.TryGetValue(p, out var si) ? si : 1]:0}mm", Accent);

            var k = IkOf(p);
            if (k == null) return;

            var c = k.CurrentTcp * 1000f;
            Set(p + "CartPos", $"위치  X {c.x,6:F1}  Y {c.y,6:F1}  Z {c.z,6:F1} mm", TextMain);
            Set(p + "CartMsg", k.StatusMessage,
                k.Blocked ? Bad : k.LastConverged ? TextDim : Warn);
        }

        /// <summary>실행 후 진짜 관절 범위를 슬라이더에 다시 씌운다. 그 과정이 명령으로 새면 안 된다.</summary>
        void ApplyJointRanges(string pre, SOArmManager m)
        {
            bool ok = true;
            for (int i = 0; i < 5; i++)
            {
                if (!sliders.TryGetValue($"{pre}J{i}", out var s) || s == null) { ok = false; continue; }
                float lo = SafeMin(m, i), hi = SafeMax(m, i);
                if (hi <= lo) { ok = false; continue; }
                if (Mathf.Approximately(s.minValue, lo) && Mathf.Approximately(s.maxValue, hi)) continue;

                var saved = s.onValueChanged;
                s.onValueChanged = new Slider.SliderEvent();
                s.minValue = lo; s.maxValue = hi;
                s.value = Mathf.Clamp(SafeAngle(m, i), lo, hi);
                s.onValueChanged = saved;
            }
            if (ok && pre == "R2") rangesApplied = true;
        }

        void BindStatus()
        {
            Row("S1", "로봇 1", Live(dualManager.robot1), status.r1_temp, status.r1_volt, status.r1_load);
            Row("S2", "로봇 2", Live(dualManager.robot2), status.r2_temp, status.r2_volt, status.r2_load);

            var c = dualManager.robot1?.real?.socketClient;
            bool srv = c != null && c.IsConnected;
            Set("SrvState", srv ? "● 서버 연결됨" : "● 서버 끊김", srv ? Accent : Bad);
            Set("SrvDetail", srv ? c.StatusMessage : "응답 없음", TextDim);
            Set("ClockNow", DateTime.Now.ToString("HH:mm:ss"), TextDim);
        }

        void Row(string k, string name, bool conn, float t, float v, float l)
        {
            // 행 이름("로봇 1")도 고정 문구다. 생성할 때 넣고 여기서는 안 건드린다.
            Set(k + "Conn", conn ? wordConn : wordDisconn, conn ? Accent : Bad);

            var tc = t < 0 ? TextDim : t >= tempWarn + 10 ? Bad : t >= tempWarn ? Warn : Accent;
            var vc = v < 0 ? TextDim : v < voltWarn ? Bad : Accent;
            var lc = l < 0 ? TextDim : l >= 400 ? Warn : TextMain;

            Set(k + "Temp", t < 0 ? NA : $"{t:F0}°C", tc);
            Set(k + "Volt", v < 0 ? NA : $"{v:F1}V", vc);
            Set(k + "Load", l < 0 ? NA : $"{l:F0}", lc);

            SetLed(k + "Led", conn ? Accent : Bad);
            SetGauge(k + "TempBar", t, 20f, 70f, tc);     // 상온~과열
            SetGauge(k + "VoltBar", v, 9f, 13f, vc);      // 12V 계열
            SetGauge(k + "LoadBar", l, 0f, 600f, lc);
        }

        void SetLed(string n, Color c)
        { if (images.TryGetValue(n, out var i) && i != null) i.color = c; }

        // ── 안전 줄무늬 ─────────────────────────────────────────

        static Sprite hazardSprite;

        /// <summary>
        /// 노랑·검정 사선 타일을 코드로 만든다.
        /// 이미지 파일을 쓰지 않는 이유는 색이나 줄 간격을 바꿀 때마다
        /// 에셋을 다시 만들어야 해서다. 여기서는 숫자만 고치면 된다.
        /// </summary>
        static Sprite HazardSprite()
        {
            if (hazardSprite != null) return hazardSprite;

            const int size = 64, band = 10;              // band = 줄 두께(px)
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HazardStripe" };
            var yellow = new Color32(0xFF, 0xC4, 0x00, 0xFF);
            var black = new Color32(0x12, 0x12, 0x14, 0xFF);

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = (((x + y) / band) % 2 == 0) ? yellow : black;

            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();

            hazardSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                         100f, 0, SpriteMeshType.FullRect);
            hazardSprite.name = "HazardStripe";
            return hazardSprite;
        }

        /// <summary>버튼 바탕을 안전 줄무늬로 바꾼다. 끄면 원래 색으로 돌린다.</summary>
        /// <summary>
        /// 버튼 **테두리만** 안전 줄무늬로 바꾼다.
        ///
        /// 바탕 전체를 줄무늬로 칠하면 글자가 줄에 묻혀 읽기 어렵다.
        /// 실제 산업 현장 표시도 위험 구역의 가장자리에 두르지 면을 다 칠하지 않는다.
        /// 구현은 간단하다 — 버튼 바탕을 줄무늬로 깔고, 그 위에 원래 색 판을
        /// 테두리 두께만큼 안쪽으로 넣으면 가장자리만 남는다.
        /// </summary>
        void SetHazard(string buttonName, bool on)
        {
            if (!buttons.TryGetValue(buttonName, out var b) || b == null) return;
            var img = b.GetComponent<Image>();
            if (img == null) return;

            // ⚠️ 버튼 바탕은 건드리지 않는다.
            //    안쪽을 판으로 덮는 방식은 아이콘·글자와 그리기 순서가 얽혀
            //    바탕이 그대로 꽉 찬 것처럼 보였다. 테두리 네 줄을 따로 그린다.
            var frame = b.transform.Find("HazardFrame");

            if (!on)
            {
                // 예전 방식으로 바탕이 줄무늬가 돼 있었다면 되돌린다
                if (img.sprite != null && img.sprite.name == "HazardStripe")
                { img.sprite = null; img.type = Image.Type.Simple; img.color = SubBg; }

                var stale = b.transform.Find("HazardInner");
                if (stale != null) stale.gameObject.SetActive(false);
                if (frame != null) frame.gameObject.SetActive(false);
                return;
            }

            if (frame == null)
            {
                var holder = new GameObject("HazardFrame", typeof(RectTransform));
                holder.transform.SetParent(b.transform, false);
                var frt = holder.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
                frame = holder.transform;

                int t = hazardBorder;
                var edges = new[]
                {
                    (new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -t), new Vector2(0, 0)),  // 위
                    (new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0),  new Vector2(0, t)),  // 아래
                    (new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0),  new Vector2(t, 0)),  // 왼
                    (new Vector2(1, 0), new Vector2(1, 1), new Vector2(-t, 0), new Vector2(0, 0)),  // 오른
                };
                foreach (var (aMin, aMax, oMin, oMax) in edges)
                {
                    var e = new GameObject("Edge", typeof(RectTransform), typeof(Image));
                    e.transform.SetParent(frame, false);
                    var ert = e.GetComponent<RectTransform>();
                    ert.anchorMin = aMin; ert.anchorMax = aMax;
                    ert.offsetMin = oMin; ert.offsetMax = oMax;

                    var eim = e.GetComponent<Image>();
                    eim.sprite = HazardSprite();
                    eim.type = Image.Type.Tiled;
                    eim.pixelsPerUnitMultiplier = 2.4f;   // 클수록 줄이 촘촘해진다
                    eim.color = Color.white;
                    eim.raycastTarget = false;
                }
            }

            frame.gameObject.SetActive(true);
            frame.SetAsLastSibling();   // 글자보다 위 — 테두리가 글자에 안 가린다
        }

        /// <summary>값을 0~1 로 정규화해 막대를 채운다. 못 받은 값(-1)은 빈 막대로 둔다.</summary>
        void SetGauge(string n, float val, float lo, float hi, Color c)
        {
            if (!images.TryGetValue(n, out var img) || img == null) return;
            img.fillAmount = val < 0 ? 0f : Mathf.Clamp01((val - lo) / Mathf.Max(hi - lo, 0.0001f));
            img.color = c;
        }

        /// <summary>
        /// 아직 닫히지 않은 반복 구간이 있는지.
        /// loop_start 가 loop_end 보다 많으면 열려 있는 것이다.
        /// </summary>
        bool LoopOpen
        {
            get
            {
                var wp = recordManager?.CurrentProject?.waypoints;
                if (wp == null) return false;
                int open = 0;
                foreach (var w in wp)
                {
                    if (w.type == "loop_start") open++;
                    else if (w.type == "loop_end") open--;
                }
                return open > 0;
            }
        }

        /// <summary>
        /// 스텝 한 줄의 표시 문구.
        ///
        /// Waypoint.GetDisplayText() 는 "robot1: 이름" 만 준다. 그것만 보면
        /// 어떤 자세인지 알 수 없어 목록에서 스텝을 구분할 수가 없다.
        /// 관절 각도와 그리퍼 값을 같이 적는다.
        /// </summary>
        static string StepText(Waypoint w)
        {
            if (w == null) return "";
            switch (w.type)
            {
                case "wait":       return $"대기 {w.duration:F1}초";
                case "loop_start": return $"반복 시작 ({w.loopCount}회)";
                case "loop_end":   return "반복 끝";
            }

            string One(float[] j, float g) =>
                j == null || j.Length < 5 ? "--"
                : $"({j[0]:F0}, {j[1]:F0}, {j[2]:F0}, {j[3]:F0}, {j[4]:F0})  그리퍼 {g:F0}%";

            switch (w.target)
            {
                case "robot1": return $"R1 {One(w.joints, w.gripper)}";
                case "robot2": return $"R2 {One(w.joints2, w.gripper2)}";
                case "both":   return $"R1 {One(w.joints, w.gripper)}   R2 {One(w.joints2, w.gripper2)}";
                default:       return w.GetDisplayText();
            }
        }

        void ClearRows()
        {
            for (int r = 0; r < RoutineRows; r++)
            {
                Set($"RoutineRow{r}Label", "");
                if (buttons.TryGetValue("RoutineRow" + r, out var rb) && rb != null)
                {
                    var im = rb.GetComponent<Image>();
                    if (im != null) im.color = new Color(0, 0, 0, 0);
                }
            }
        }

        void BindRoutine()
        {
            if (recordManager == null || recordManager.CurrentProject == null)
            { Set("RoutineEmpty", "RecordManager 없음", Bad); ClearRows(); return; }

            var wp = recordManager.CurrentProject.waypoints;
            Set("RoutineCount", $"{wp.Count} 스텝", TextDim);

            // 편집 중에는 건드리지 않는다. 덮어쓰면 이름을 지울 수가 없다.
            if (inputs.TryGetValue("RoutineNameIn", out var nameIn) && nameIn != null && !nameIn.isFocused)
                nameIn.text = recordManager.CurrentProject.projectName ?? "";
            // 재생 = 선택 스텝 하나, ALL = 전체. 재생 중에는 둘 다 정지 버튼이 된다.
            Set("BtnPlayLabel", recordManager.IsPlaying ? wordStop : wordPlay);
            Set("BtnPlayAllLabel", recordManager.IsPlaying ? wordStop : wordPlayAll);
            Set("BtnLoopLabel", LoopOpen ? wordLoopEnd : wordLoopStart, LoopOpen ? Warn : Accent);
            Set("RoutineStatus", recordManager.StatusMessage, TextDim);

            // 저장 경로 — 저장하고 나서 파일을 어디서 찾을지 몰라 헤매지 않게
            string name = recordManager.CurrentProject.projectName;
            if (string.IsNullOrWhiteSpace(name)) name = "Untitled";
            Set("RoutinePath", $"저장 경로 :  Recordings/{name}.json", TextDim);

            if (wp.Count == 0)
            {
                Set("RoutineEmpty", "스텝 없음 — 아래 버튼으로 현재 자세를 추가하세요", TextDim);
                ClearRows();
                return;
            }
            Set("RoutineEmpty", "");

            selectedStep = Mathf.Clamp(selectedStep, 0, wp.Count - 1);

            // 선택이 항상 보이도록 창을 굴린다.
            routineFrom = Mathf.Clamp(selectedStep - RoutineRows / 2, 0, Mathf.Max(0, wp.Count - RoutineRows));

            for (int r = 0; r < RoutineRows; r++)
            {
                int idx = routineFrom + r;
                bool has = idx < wp.Count;
                bool sel = has && idx == selectedStep;

                Set($"RoutineRow{r}Label",
                    has ? $"{wp[idx].stepNumber,2}.  {StepText(wp[idx])}" : "",
                    sel ? Color.black : TextMain);

                // 선택된 줄만 옐로 바탕. 나머지는 투명이라 배경이 그대로 보인다.
                if (buttons.TryGetValue("RoutineRow" + r, out var rb) && rb != null)
                {
                    var im = rb.GetComponent<Image>();
                    if (im != null) im.color = sel ? Accent : new Color(0, 0, 0, 0);
                }
            }
        }

        static bool Live(SOArmManager m) => m != null && m.real != null && m.real.IsConnected;
        void Log(string s) => Debug.Log("[관제] " + s);

        static float SafeAngle(SOArmManager m, int i, float f = 0f) { try { return m != null ? m.GetJointAngle(i) : f; } catch { return f; } }
        static float SafeMin(SOArmManager m, int i, float f = -180f) { try { return m != null ? m.GetJointMinAngle(i) : f; } catch { return f; } }
        static float SafeMax(SOArmManager m, int i, float f = 180f) { try { return m != null ? m.GetJointMaxAngle(i) : f; } catch { return f; } }
        static string SafeName(SOArmManager m, int i, string f) { try { return m != null ? m.GetJointName(i) : f; } catch { return f; } }
        static float SafeGrip(SOArmManager m, float f = 50f) { try { return m != null ? m.GetGripperPercent() : f; } catch { return f; } }

        void WatchChanges()
        {
            if (dualManager == null) return;
            bool c1 = Live(dualManager.robot1), c2 = Live(dualManager.robot2);
            bool e = dualManager.EmergencyStopped, t1 = dualManager.robot1Teach, t2 = dualManager.robot2Teach;
            if (!primed) { prevC1 = c1; prevC2 = c2; prevE = e; prevT1 = t1; prevT2 = t2; primed = true; return; }
            if (c1 != prevC1) Log(c1 ? "로봇1 연결됨" : "로봇1 끊김");
            if (c2 != prevC2) Log(c2 ? "로봇2 연결됨" : "로봇2 끊김");
            if (e != prevE) Log(e ? "비상정지" : "비상정지 해제");
            if (t1 != prevT1) Log("로봇1 수동 " + (t1 ? "ON" : "OFF"));
            if (t2 != prevT2) Log("로봇2 수동 " + (t2 ? "ON" : "OFF"));
            prevC1 = c1; prevC2 = c2; prevE = e; prevT1 = t1; prevT2 = t2;
        }

        void PollStatus()
        {
            if (Time.realtimeSinceStartup - lastStatusPoll < 2f) return;
            lastStatusPoll = Time.realtimeSinceStartup;
            var c = dualManager?.robot1?.real?.socketClient;
            if (c == null || !c.IsConnected) return;

            c.SendRaw("{\"type\":\"status\",\"mode\":\"both\"}\n", resp =>
            {
                if (string.IsNullOrEmpty(resp)) return;
                try
                {
                    var s = JsonUtility.FromJson<StatusMsg>(resp);
                    if (s == null) return;
                    // 부분 응답이 기존 정상값을 지우지 않게 받은 것만 갱신한다.
                    if (s.r1_temp >= 0) status.r1_temp = s.r1_temp;
                    if (s.r1_volt >= 0) status.r1_volt = s.r1_volt;
                    if (s.r1_load >= 0) status.r1_load = s.r1_load;
                    if (s.r2_temp >= 0) status.r2_temp = s.r2_temp;
                    if (s.r2_volt >= 0) status.r2_volt = s.r2_volt;
                    if (s.r2_load >= 0) status.r2_load = s.r2_load;
                }
                catch { }
            });
        }

        void UpdatePreviewCams()
        {
            if (!RobotBounds(out var b)) return;
            float radius = Mathf.Max(b.extents.magnitude, 0.15f);
            for (int i = 0; i < PKeys.Length; i++)
            {
                var t = transform.Find("PreviewCam_" + PKeys[i]);
                if (t == null) continue;
                var cam = t.GetComponent<Camera>();
                if (cam == null) continue;

                if (cam.targetTexture == null)
                {
                    var rt = new RenderTexture(760, 480, 16) { name = "PreviewRT_" + PKeys[i] };
                    cam.targetTexture = rt;
                    if (root != null)
                        foreach (var raw in root.GetComponentsInChildren<RawImage>(true))
                            if (raw.name == "ViewImg_" + PKeys[i]) { raw.texture = rt; break; }
                }

                float dist = radius / Mathf.Sin(cam.fieldOfView * Mathf.Deg2Rad * 0.5f) * 1.12f;
                var d = PreviewDir(i);

                // 사용자가 그 칸을 마우스로 돌린 만큼 기본 방향에서 더 회전시킨다.
                // 기본 방향 자체는 건드리지 않으므로 우클릭 한 번이면 원래대로 돌아온다.
                var orb = FindOrbit(PKeys[i]);
                if (orb != null)
                {
                    d = Quaternion.Euler(orb.pitch, orb.yaw, 0f) * d;
                    dist *= orb.zoom;
                }

                t.position = b.center + d * dist;
                t.rotation = Quaternion.LookRotation(-d, PreviewUp(i));
                cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 3f);
                cam.farClipPlane = dist + radius * 4f;
            }
        }

        /// <summary>
        /// 뷰별 카메라 방향. **월드 축 고정**이다.
        ///
        /// 한때 로봇 트랜스폼의 forward/right 를 기준으로 잡아봤는데,
        /// 잘 나오던 위·옆 뷰까지 같이 틀어져서 되돌렸다.
        /// 바꿔야 했던 건 앞 뷰의 앞뒤뿐이었다.
        ///
        /// 반환값은 **로봇에서 카메라로 향하는** 방향이다(카메라는 그 자리에서 되돌아본다).
        /// </summary>
        Vector3 PreviewDir(int i)
        {
            switch (i)
            {
                case 0:  // 위 — 바로 위에서 내려다본다
                    return Vector3.up;
                case 1:  // 옆 — Z 축에서. 두 로봇이 Z 로 떨어져 있어 하나만 크게 보인다
                    return new Vector3(0f, viewTilt, flipSide ? -1f : 1f).normalized;
                default: // 앞 — X 축에서. 두 로봇이 나란히 보인다
                    return new Vector3(flipFront ? -1f : 1f, viewTilt, 0f).normalized;
            }
        }

        /// <summary>
        /// 카메라의 위쪽 방향.
        ///
        /// 위 뷰는 시선이 수직이라 up 을 Vector3.up 으로 두면 방향이 정해지지 않아
        /// 화면이 제멋대로 돌아간다. 팔이 뻗는 쪽(−Z)이 화면 위로 오도록 명시한다.
        /// </summary>
        Vector3 PreviewUp(int i)
        {
            if (i != 0) return Vector3.up;
            return topFlip ? Vector3.forward : Vector3.back;
        }

        readonly Dictionary<string, PreviewOrbit> orbits = new Dictionary<string, PreviewOrbit>();

        PreviewOrbit FindOrbit(string key)
        {
            if (orbits.TryGetValue(key, out var o) && o != null) return o;
            if (root == null) return null;
            foreach (var p in root.GetComponentsInChildren<PreviewOrbit>(true))
                if (p.name == "ViewImg_" + key) { orbits[key] = p; return p; }
            return null;
        }

        bool RobotBounds(out Bounds b)
        {
            b = new Bounds(Vector3.zero, Vector3.one);
            bool any = false;
            foreach (var m in new[] { dualManager?.robot1, dualManager?.robot2 })
            {
                if (m == null) continue;
                foreach (var r in m.GetComponentsInChildren<Renderer>())
                { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            }
            return any;
        }

        void SendSpeed()
        {
            if (!uiReady || dualManager?.robot1?.real == null) return;
            if (Time.realtimeSinceStartup - lastSpeedSend < 0.4f) return;
            lastSpeedSend = Time.realtimeSinceStartup;
            var c = dualManager.robot1.real.socketClient;
            if (c == null) return;
            int v = sliders.TryGetValue("VelSlider", out var vs) && vs != null ? (int)vs.value : 800;
            int a = sliders.TryGetValue("AccSlider", out var acs) && acs != null ? (int)acs.value : 50;
            c.SendRaw($"{{\"type\":\"set_speed\",\"mode\":\"both\",\"velocity\":{v},\"acceleration\":{a}}}\n");
        }

        void AddStep(string target)
        {
            if (recordManager == null) return;
            int v = sliders.TryGetValue("VelSlider", out var vs) && vs != null ? (int)vs.value : 800;
            int a = sliders.TryGetValue("AccSlider", out var acs) && acs != null ? (int)acs.value : 50;
            recordManager.AddMotionStep(target, v, a);
            Log($"스텝 추가 ({target})");
        }

        // ── 불러오기 창 ──────────────────────────────────────────

        Transform LoadPanel => root != null ? root.Find("RecordPanel/LoadPanel") : null;

        void OpenLoadPicker()
        {
            if (recordManager == null) return;

            // 열 때마다 다시 읽는다. 창을 띄워 둔 사이에 저장한 파일이 목록에 없으면
            // "저장했는데 안 보인다" 가 된다.
            var f = recordManager.ListSavedFiles() ?? new string[0];
            // 최근에 고친 것이 위로. 방금 저장한 루틴을 찾아 내려가지 않아도 된다.
            Array.Sort(f, (a, b) => System.IO.File.GetLastWriteTime(System.IO.Path.Combine(recordManager.RecordingsFolder, b))
                             .CompareTo(System.IO.File.GetLastWriteTime(System.IO.Path.Combine(recordManager.RecordingsFolder, a))));
            loadFiles = f;
            loadSel = 0; loadFrom = 0;

            loadOpen = true;
            var lp = LoadPanel; if (lp != null) lp.gameObject.SetActive(true);
        }

        void CloseLoadPicker()
        {
            loadOpen = false;
            var lp = LoadPanel; if (lp != null) lp.gameObject.SetActive(false);
        }

        void ConfirmLoad()
        {
            if (recordManager == null) return;
            if (loadFiles.Length == 0) { CloseLoadPicker(); return; }

            loadSel = Mathf.Clamp(loadSel, 0, loadFiles.Length - 1);
            string file = loadFiles[loadSel];

            if (recordManager.LoadProject(System.IO.Path.GetFileNameWithoutExtension(file)))
            {
                selectedStep = 0;
                Log($"루틴 불러옴 — {file}");
                CloseLoadPicker();
            }
            // 실패하면 창을 열어 둔다. 닫아 버리면 왜 안 됐는지 볼 곳이 없다.
        }

        void BindLoadPicker()
        {
            int n = loadFiles.Length;
            Set("LoadCount", n > 0 ? $"{n} 개" : "", TextDim);

            if (n == 0)
            {
                Set("LoadEmpty", "저장된 루틴이 없습니다.\n스텝을 만든 뒤 «저장» 을 먼저 누르세요.", TextDim);
                Set("LoadPage", "");
                Set("LoadWarn", "");
                for (int r = 0; r < LoadRows; r++) ClearLoadRow(r);
                return;
            }
            Set("LoadEmpty", "");

            loadSel = Mathf.Clamp(loadSel, 0, n - 1);
            loadFrom = Mathf.Clamp(loadFrom, 0, Mathf.Max(0, n - LoadRows));
            // 선택이 창 밖으로 나가면 따라 굴린다
            if (loadSel < loadFrom) loadFrom = loadSel;
            if (loadSel >= loadFrom + LoadRows) loadFrom = loadSel - LoadRows + 1;

            for (int r = 0; r < LoadRows; r++)
            {
                int idx = loadFrom + r;
                if (idx >= n) { ClearLoadRow(r); continue; }

                bool sel = idx == loadSel;
                Set($"LoadRow{r}Label", "  " + System.IO.Path.GetFileNameWithoutExtension(loadFiles[idx]),
                    sel ? Color.black : TextMain);
                if (buttons.TryGetValue("LoadRow" + r, out var rb) && rb != null)
                {
                    var im = rb.GetComponent<Image>();
                    if (im != null) im.color = sel ? Accent : new Color(0, 0, 0, 0);
                }
            }

            Set("LoadPage", n > LoadRows ? $"{loadSel + 1} / {n}" : $"{n} 개", TextDim);

            // 열면 지금 것이 통째로 바뀐다. 스텝이 남아 있으면 반드시 알린다.
            int cur = recordManager?.CurrentProject?.waypoints?.Count ?? 0;
            Set("LoadWarn", cur > 0 ? $"⚠ 지금 스텝 {cur}개가 사라집니다. 필요하면 «취소» 후 저장하세요." : "",
                cur > 0 ? Warn : TextDim);
        }

        void ClearLoadRow(int r)
        {
            Set($"LoadRow{r}Label", "");
            if (buttons.TryGetValue("LoadRow" + r, out var rb) && rb != null)
            {
                var im = rb.GetComponent<Image>();
                if (im != null) im.color = new Color(0, 0, 0, 0);
            }
        }

        // ══════════════════════════════════════════════════════════
        // 생성 — 모양만. 동작은 WireUp() 이 실행 시에 붙인다.
        // ══════════════════════════════════════════════════════════

        [ContextMenu("관제 화면 생성")]
        public void BuildUI()
        {
            // ⚠️ 지우기 전에 현재 배치를 자동으로 남긴다.
            //    "레이아웃 저장" 을 깜빡하고 생성을 누르면 손본 것이 전부 날아간다.
            var prev = transform.Find(RootName);
            if (prev != null)
            {
                root = prev;
                SaveLayout();
                Debug.Log("[관제] 다시 만들기 전에 현재 배치를 자동 저장했습니다");
            }

            var old = transform.Find(RootName);
            if (old != null) DestroyImmediate(old.gameObject);
            foreach (var k in PKeys) { var c = transform.Find("PreviewCam_" + k); if (c != null) DestroyImmediate(c.gameObject); }
            root = null; wired = false;

            if (dualManager == null) dualManager = FindAnyObjectByType<SOArmDualManager>();
            if (viewCamera == null) viewCamera = FindAnyObjectByType<RobotViewCamera>();
            if (recordManager == null) recordManager = FindAnyObjectByType<RecordManager>();

            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Debug.Log("[관제] EventSystem 생성 (버튼·입력에 필요)");
            }

            var go = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = referenceResolution;
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            sc.matchWidthOrHeight = 0.5f;

            var rt = go.GetComponent<RectTransform>();
            BuildTop(rt); BuildLeft(rt); BuildCenter(rt); BuildRecord(rt);

            CacheBindings();
            ApplyIcons();          // 인스펙터에 끼워 둔 아이콘이 있으면 붙인다
            LoadLayout();          // 손으로 옮겨 둔 배치가 있으면 그대로 되살린다
            ApplyLegacyVisibility();
            Debug.Log("[관제] 생성 완료 — 동작은 Play 시 자동 배선됩니다");
        }

        // ══════════════════════════════════════════════════════════
        // 레이아웃 저장 / 복원
        //
        // 에디터에서 슬라이더·버튼·입력창을 손으로 옮겨 놓아도
        // 「관제 화면 생성」을 다시 누르면 전부 기본 배치로 돌아가 버린다.
        // 그래서 배치를 파일로 남기고, 생성 직후 자동으로 다시 씌운다.
        //
        // 경로는 **프로젝트 루트**다(Assets 밖). persistentDataPath 에 두면
        // 저장소에 안 들어가서 PC 를 바꾸거나 팀과 공유할 때 사라진다.
        // ══════════════════════════════════════════════════════════

        [Serializable]
        class RectRec
        {
            public string path;
            public Vector2 aMin, aMax, oMin, oMax, pivot;

            // ⚠️ 위치만 저장했더니, 화면을 다시 만들 때 손으로 바꾼 글자·크기·색이
            //    전부 코드 기본값으로 돌아갔다. 텍스트 속성도 같이 남긴다.
            public bool hasText;
            public string text;
            public int fontSize;
            public Color color;
            public int alignment;
            public bool bestFit;
            public int minSize, maxSize;

            // 버튼 바탕색·아이콘 틴트 등
            public bool hasImage;
            public Color imgColor;
        }

        [Serializable]
        class LayoutFile { public List<RectRec> items = new List<RectRec>(); }

        public string LayoutPath =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "control_tower_layout.json"));

        static string PathOf(Transform t, Transform root)
        {
            var sb = new System.Text.StringBuilder(t.name);
            for (var p = t.parent; p != null && p != root; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        /// <summary>
        /// 버튼 이름 → 아이콘을 붙인다.
        /// 생성과 분리해 둔 이유는, 인스펙터에서 아이콘만 바꾸고 다시 적용할 수 있게 하기 위해서다.
        /// 화면을 통째로 다시 만들면 손본 배치가 날아간다.
        /// </summary>
        [ContextMenu("아이콘 적용")]
        public void ApplyIcons()
        {
            if (root == null) root = transform.Find(RootName);
            if (root == null) return;

            var map = new Dictionary<string, Sprite>
            {
                { "BtnTeachAll", iconTeach }, { "BtnR1Only", iconR1 }, { "BtnR2Only", iconR2 },
                { "BtnMirror", iconMirror }, { "BtnEstop", iconEstop }, { "BtnHome", iconHome },
                { "BtnRecord", iconRecord },
                { "BtnPlay", iconPlay }, { "BtnNew", iconNew }, { "BtnSave", iconSave },
                { "BtnLoad", iconLoad }, { "BtnDel", iconDelete },
                { "BtnLoadOk", iconLoad }, { "BtnLoadCancel", iconDelete },
                { "BtnLoop", iconLoop },
                { "BtnAddR1", iconAdd }, { "BtnAddR2", iconAdd },
                { "BtnAddBoth", iconAdd }, { "BtnAddWait", iconAdd },
            };
            for (int i = 0; i < 5; i++)
                foreach (var p in new[] { "R1", "R2" })
                { map[$"{p}J{i}Plus"] = iconPlus; map[$"{p}J{i}Minus"] = iconMinus; }
            foreach (var p in new[] { "R1", "R2" })
            { map[p + "GripOpen"] = iconGripOpen; map[p + "GripClose"] = iconGripClose; }

            // 제목 아이콘 — 버튼이 아니라 패널 헤더에 붙는 것들
            foreach (var im in root.GetComponentsInChildren<Image>(true))
            {
                bool isTitle = im.name.StartsWith("ViewIcon_") || im.name == "StatusIcon"
                               || im.name == "SysIcon" || im.name.EndsWith("CardIcon")
                               || im.name == "SpeedIcon" || im.name == "AccelIcon";
                if (!isTitle) continue;

                Sprite sp2 = im.name.StartsWith("ViewIcon_") ? iconView
                           : im.name == "StatusIcon" ? iconRobotStatus
                           : im.name == "SysIcon" ? iconSystem
                           : im.name == "SpeedIcon" ? iconSpeed
                           : im.name == "AccelIcon" ? iconAccel
                           : iconRobotCard;

                im.sprite = sp2;
                im.color = sp2 == null ? new Color(0, 0, 0, 0) : Accent;   // 없으면 투명
            }

            int n = 0;
            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                if (!map.TryGetValue(b.name, out var sp)) continue;
                var slot = b.transform.Find("Icon");

                if (sp == null) { if (slot != null) DestroyImmediate(slot.gameObject); continue; }

                // ⚠️ 이미 있는 아이콘의 위치·크기는 건드리지 않는다.
                //    손으로 옮겨 둔 것을 아이콘을 다시 적용할 때마다 되돌리면 안 된다.
                //    새로 만들 때만 기본 자리를 잡아 준다.
                bool created = slot == null;
                if (created)
                {
                    var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(b.transform, false);
                    go.transform.SetAsFirstSibling();
                    slot = go.transform;

                    var rt = slot.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f);
                    rt.pivot = new Vector2(0, 0.5f);
                    rt.anchoredPosition = new Vector2(8, 0);
                    rt.sizeDelta = new Vector2(iconSize, iconSize);
                }

                var img = slot.GetComponent<Image>();
                img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
                img.color = b.GetComponent<Image>() != null && b.GetComponent<Image>().color == Accent
                            ? Color.black : Accent;      // 옐로 버튼 위에서는 검정이 읽힌다

                // 글자 밀기도 처음 붙일 때만. 나중에 옮겨 둔 배치를 되돌리지 않는다.
                if (created)
                {
                    var lbl = b.transform.Find(b.name + "Label") as RectTransform;
                    if (lbl != null) lbl.offsetMin = new Vector2(iconSize + 12, lbl.offsetMin.y);
                }
                n++;
            }
            Debug.Log($"[관제] 아이콘 {n}개 적용");
        }

        [ContextMenu("레이아웃 저장")]
        public void SaveLayout()
        {
            if (root == null) root = transform.Find(RootName);
            if (root == null) { Debug.LogWarning("[관제] 화면이 없습니다."); return; }

            var f = new LayoutFile();
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.transform == root) continue;

                var rec = new RectRec
                {
                    path = PathOf(rt.transform, root),
                    aMin = rt.anchorMin, aMax = rt.anchorMax,
                    oMin = rt.offsetMin, oMax = rt.offsetMax,
                    pivot = rt.pivot,
                };

                var t = rt.GetComponent<Text>();
                if (t != null)
                {
                    rec.hasText = true;
                    rec.text = t.text;
                    rec.fontSize = t.fontSize;
                    rec.color = t.color;
                    rec.alignment = (int)t.alignment;
                    rec.bestFit = t.resizeTextForBestFit;
                    rec.minSize = t.resizeTextMinSize;
                    rec.maxSize = t.resizeTextMaxSize;
                }

                var im = rt.GetComponent<Image>();
                if (im != null) { rec.hasImage = true; rec.imgColor = im.color; }

                f.items.Add(rec);
            }

            System.IO.File.WriteAllText(LayoutPath, JsonUtility.ToJson(f, true), System.Text.Encoding.UTF8);
            Debug.Log($"[관제] 레이아웃 저장 — {f.items.Count}개\n{LayoutPath}");
        }

        [ContextMenu("레이아웃 불러오기")]
        public void LoadLayout()
        {
            if (root == null) root = transform.Find(RootName);
            if (root == null) return;
            if (!System.IO.File.Exists(LayoutPath)) return;

            LayoutFile f;
            try { f = JsonUtility.FromJson<LayoutFile>(System.IO.File.ReadAllText(LayoutPath, System.Text.Encoding.UTF8)); }
            catch (Exception e) { Debug.LogWarning($"[관제] 레이아웃 불러오기 실패: {e.Message}"); return; }
            if (f?.items == null) return;

            // 경로로 찾아 씌운다. 없어진 항목은 조용히 건너뛴다 —
            // 구조가 바뀌어도 나머지 배치는 살아남아야 한다.
            var map = new Dictionary<string, RectTransform>();
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                if (rt.transform != root) map[PathOf(rt.transform, root)] = rt;

            int hit = 0;
            foreach (var it in f.items)
            {
                if (string.IsNullOrEmpty(it.path) || !map.TryGetValue(it.path, out var rt)) continue;

                rt.anchorMin = it.aMin; rt.anchorMax = it.aMax;
                rt.offsetMin = it.oMin; rt.offsetMax = it.oMax;
                rt.pivot = it.pivot;

                if (it.hasText)
                {
                    var t = rt.GetComponent<Text>();
                    if (t != null)
                    {
                        // 매 프레임 값이 바뀌는 칸(각도·온도 등)은 다음 Bind 에서 덮어쓰므로
                        // 여기서 되살려도 무해하다. 대신 손으로 바꾼 고정 문구가 살아남는다.
                        t.text = it.text ?? t.text;
                        if (it.fontSize > 0) t.fontSize = it.fontSize;
                        t.color = it.color;
                        t.alignment = (TextAnchor)it.alignment;
                        t.resizeTextForBestFit = it.bestFit;
                        if (it.minSize > 0) t.resizeTextMinSize = it.minSize;
                        if (it.maxSize > 0) t.resizeTextMaxSize = it.maxSize;
                    }
                }

                if (it.hasImage)
                {
                    var im = rt.GetComponent<Image>();
                    if (im != null) im.color = it.imgColor;
                }

                hit++;
            }
            Debug.Log($"[관제] 레이아웃 복원 — {hit}/{f.items.Count}개 적용");
        }

        [ContextMenu("저장된 레이아웃 삭제")]
        public void ClearLayout()
        {
            try { if (System.IO.File.Exists(LayoutPath)) { System.IO.File.Delete(LayoutPath); Debug.Log("[관제] 저장된 레이아웃 삭제"); } }
            catch (Exception e) { Debug.LogWarning(e.Message); }
        }

        [ContextMenu("관제 화면 삭제")]
        public void DestroyUI()
        {
            var old = transform.Find(RootName);
            if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
            foreach (var k in PKeys) { var c = transform.Find("PreviewCam_" + k); if (c != null) DestroyImmediate(c.gameObject); }
            var m = FindAnyObjectByType<SmartFactoryUI_v3_4>(); if (m != null) m.enabled = true;
            var r = FindAnyObjectByType<SmartFactoryRecordUI>(); if (r != null) r.enabled = true;
            texts.Clear(); sliders.Clear(); inputs.Clear(); buttons.Clear();
            root = null; recordPanel = null; wired = false;
        }

        void BuildTop(RectTransform p)
        {
            var bar = Panel(p, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -TopH), Vector2.zero, Bg);
            TitleIcon(bar, "SysIcon", 30f, new Vector2(18, -20));
            Label(bar, "TopProject", projectName, TextAnchor.MiddleLeft, titleFontSize, Accent,
                  new Vector2(0, 0), new Vector2(0.19f, 1), new Vector2(56, 0), Vector2.zero);

            string[] id = { "BtnTeachAll", "BtnR1Only", "BtnR2Only", "BtnMirror", "BtnEstop", "BtnHome", "BtnRecord" };
            string[] tx = { "수동모드", "R1 only", "R2 only", "Mirror", "비상정지", "홈", "Recorder ▶" };
            for (int i = 0; i < id.Length; i++)
            {
                float x0 = 0.20f + i * 0.113f;
                bool danger = id[i] == "BtnEstop";
                bool primary = id[i] == "BtnRecord";
                Btn(bar, id[i], tx[i], new Vector2(x0, 0.14f), new Vector2(x0 + 0.109f, 0.86f),
                    danger ? Bad : primary ? Accent : SubBg, danger || primary ? Color.black : Accent);
            }
            var rule = Panel(bar, "Rule", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -2), Vector2.zero, Accent);
            rule.GetComponent<Image>().raycastTarget = false;
        }

        void BuildLeft(RectTransform p)
        {
            var col = Panel(p, "LeftColumn", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(LeftW, -TopH), new Color(0, 0, 0, 0));

            // 스텝 크기 — 원래 UI 의 0.5 / 1 / 5 / 10 도 버튼
            var srow = Panel(col, "StepRow", new Vector2(0, 0.955f), new Vector2(1, 1), new Vector2(10, 2), new Vector2(-6, -2), PanelBg);
            Label(srow, "StepLbl", "스텝", TextAnchor.MiddleLeft, baseFontSize - 4, TextDim, new Vector2(0.02f, 0), new Vector2(0.16f, 1), Vector2.zero, Vector2.zero);
            Label(srow, "StepNow", "5°", TextAnchor.MiddleLeft, baseFontSize - 3, Accent, new Vector2(0.16f, 0), new Vector2(0.30f, 1), Vector2.zero, Vector2.zero);
            float[] sv = { 0.5f, 1f, 5f, 10f };
            for (int i = 0; i < sv.Length; i++)
                Btn(srow, "BtnStep" + sv[i].ToString(CultureInfo.InvariantCulture), sv[i].ToString("0.#") + "°",
                    new Vector2(0.32f + i * 0.17f, 0.1f), new Vector2(0.48f + i * 0.17f, 0.9f), SubBg, Accent);

            RobotCard(col, "R1", "ROBOT 1", dualManager?.robot1, 0.485f, 0.95f);
            RobotCard(col, "R2", "ROBOT 2", dualManager?.robot2, 0.01f, 0.475f);
        }

        void RobotCard(RectTransform p, string pre, string title, SOArmManager m, float y0, float y1)
        {
            var c = Panel(p, pre + "Card", new Vector2(0, y0), new Vector2(1, y1), new Vector2(10, 3), new Vector2(-6, -3), PanelBg);
            Rule(c); Frame(c, Edge);
            TitleIcon(c, pre + "CardIcon", 24f, new Vector2(12, -6));
            Label(c, pre + "Title", title, TextAnchor.MiddleLeft, titleFontSize - 2, Accent,
                  new Vector2(0, 0.90f), new Vector2(0.29f, 0.99f), new Vector2(44, 0), Vector2.zero);

            // 조인트 ↔ 카티시안 전환 스위치. 카드마다 따로 논다 —
            // R1 은 관절로 잡고 R2 는 좌표로 미는 식이 실제로 자주 필요하다.
            Btn(c, pre + "ModeSw", "조인트", new Vector2(0.295f, 0.90f), new Vector2(0.46f, 0.99f), SubBg, Accent);
            // 글자 ● 대신 실제 LED 를 찍는다. 계기판은 점 하나로 상태가 읽혀야 한다.
            Led(c, pre + "CardLed", new Vector2(0.475f, 0.945f), 13f);
            Label(c, pre + "State", "OFFLINE", TextAnchor.MiddleRight, baseFontSize - 4, Bad,
                  new Vector2(0.49f, 0.90f), new Vector2(0.80f, 0.99f), Vector2.zero, Vector2.zero);
            Label(c, pre + "Teach", "🔒 유지", TextAnchor.MiddleRight, baseFontSize - 5, TextDim,
                  new Vector2(0.78f, 0.90f), new Vector2(1, 0.99f), Vector2.zero, new Vector2(-12, 0));

            // 두 면은 카드 전체를 덮는 **투명** 그룹이다. 투명하면 raycastTarget 이
            // 꺼지므로(Panel 참고) 제목줄 클릭을 안 가로챈다.
            //
            // ⚠️ 그룹으로 감싸도 기존 배선은 안 깨진다. CacheBindings 가
            //    GetComponentsInChildren<T>(true) 로 **꺼져 있는 것까지** 이름으로
            //    담기 때문이다. 그래서 안쪽 위젯의 앵커 숫자도 손대지 않는다.
            var jg = Panel(c, pre + "JointGroup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            var cg = Panel(c, pre + "CartGroup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));

            for (int i = 0; i < 5; i++)
            {
                float top = 0.885f - i * 0.128f, bot = top - 0.108f;
                // 이름은 하나만 쓴다. 모터 이름을 쓰는 이유는 서버 로그·문서와 같은
                // 표기라서 문제가 났을 때 바로 대조되기 때문이다. (못 읽으면 J1~J5)
                string nm = SafeName(m, i, "");
                Label(jg, $"{pre}J{i}Label", string.IsNullOrEmpty(nm) ? $"J{i + 1}" : nm,
                      TextAnchor.MiddleLeft, baseFontSize - 4, TextDim,
                      new Vector2(0.03f, bot), new Vector2(0.30f, top), Vector2.zero, Vector2.zero);

                Btn(jg, $"{pre}J{i}Minus", "−", new Vector2(0.30f, bot), new Vector2(0.38f, top), SubBg, Accent);
                Btn(jg, $"{pre}J{i}Plus", "+", new Vector2(0.385f, bot), new Vector2(0.465f, top), SubBg, Accent);

                float lo = SafeMin(m, i), hi = SafeMax(m, i);
                if (hi <= lo) { lo = -180f; hi = 180f; }
                Sldr(jg, $"{pre}J{i}", lo, hi, Mathf.Clamp(SafeAngle(m, i), lo, hi),
                     new Vector2(0.47f, bot + 0.022f), new Vector2(0.76f, top - 0.022f));

                // 각도는 **입력창 하나에만** 띄운다. 옆에 실측 라벨을 따로 두면
                // 같은 줄에 숫자가 둘이라 어느 쪽이 진짜인지 헷갈린다.
                Input(jg, $"{pre}J{i}In", new Vector2(0.765f, bot), new Vector2(0.985f, top));
            }

            // 그리퍼 — 슬라이더 **와** 닫기/반/열기 버튼 둘 다
            Label(jg, pre + "GripLabel", "그리퍼", TextAnchor.MiddleLeft, baseFontSize - 5, Accent,
                  new Vector2(0.03f, 0.135f), new Vector2(0.22f, 0.235f), Vector2.zero, Vector2.zero);
            Sldr(jg, pre + "Grip", 0f, 100f, 50f, new Vector2(0.23f, 0.155f), new Vector2(0.76f, 0.215f));
            Label(jg, pre + "GripValue", NA, TextAnchor.MiddleRight, baseFontSize - 5, TextMain,
                  new Vector2(0.77f, 0.135f), new Vector2(0.985f, 0.235f), Vector2.zero, Vector2.zero);

            Btn(jg, pre + "GripClose", "닫기", new Vector2(0.03f, 0.02f), new Vector2(0.35f, 0.125f), SubBg, Accent);
            Btn(jg, pre + "GripHalf", "반", new Vector2(0.36f, 0.02f), new Vector2(0.66f, 0.125f), SubBg, Accent);
            Btn(jg, pre + "GripOpen", "열기", new Vector2(0.67f, 0.02f), new Vector2(0.985f, 0.125f), SubBg, Accent);

            CartFace(cg, pre);
            cg.gameObject.SetActive(false);   // 처음엔 조인트 면
        }

        /// <summary>
        /// 카드의 카티시안 면. 슬라이더가 아니라 **방향 버튼**이다.
        ///
        /// 왜 버튼인가: 좌표 조작은 "여기서 5mm 만 왼쪽" 처럼 조금씩 미는 일이
        /// 대부분이다. 슬라이더는 끝값을 정해야 하고 손이 떨리면 수십 mm 가 튄다.
        ///
        /// 축 이름은 로봇 기준(m)이다. 화면 방향이 아니다.
        /// </summary>
        void CartFace(RectTransform g, string pre)
        {
            // ── 이동 패드 ──────────────────────────────────────
            Btn(g, pre + "CartZPlus",  "▲\nZ+", new Vector2(0.40f, 0.775f), new Vector2(0.60f, 0.885f), SubBg, Accent);
            Btn(g, pre + "CartXPlus",  "↗ X+",  new Vector2(0.62f, 0.775f), new Vector2(0.79f, 0.885f), SubBg, Accent);
            Btn(g, pre + "CartXMinus", "↙ X−",  new Vector2(0.21f, 0.775f), new Vector2(0.38f, 0.885f), SubBg, Accent);

            Btn(g, pre + "CartYMinus", "◀ Y−", new Vector2(0.03f, 0.645f), new Vector2(0.28f, 0.760f), SubBg, Accent);
            Btn(g, pre + "CartYPlus",  "Y+ ▶", new Vector2(0.72f, 0.645f), new Vector2(0.97f, 0.760f), SubBg, Accent);
            Label(g, pre + "CartHint", "TCP", TextAnchor.MiddleCenter, baseFontSize - 5, TextDim,
                  new Vector2(0.29f, 0.645f), new Vector2(0.71f, 0.760f), Vector2.zero, Vector2.zero);

            Btn(g, pre + "CartZMinus", "Z−\n▼", new Vector2(0.40f, 0.515f), new Vector2(0.60f, 0.630f), SubBg, Accent);

            // ── 회전 ───────────────────────────────────────────
            Label(g, pre + "RotLabel", "회전 (공구 기준)", TextAnchor.MiddleLeft, baseFontSize - 6, TextDim,
                  new Vector2(0.03f, 0.435f), new Vector2(0.60f, 0.500f), Vector2.zero, Vector2.zero);

            string[] ax = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                float x0 = 0.03f + i * 0.325f;
                Btn(g, $"{pre}Rot{ax[i]}Minus", $"R{ax[i].ToLower()} −",
                    new Vector2(x0, 0.310f), new Vector2(x0 + 0.305f, 0.420f), SubBg, Accent);
                Btn(g, $"{pre}Rot{ax[i]}Plus", $"R{ax[i].ToLower()} +",
                    new Vector2(x0, 0.185f), new Vector2(x0 + 0.305f, 0.295f), SubBg, Accent);
            }

            // ── 읽기 ───────────────────────────────────────────
            Label(g, pre + "CartPos", "위치 --", TextAnchor.MiddleLeft, baseFontSize - 6, TextMain,
                  new Vector2(0.03f, 0.120f), new Vector2(0.97f, 0.180f), Vector2.zero, Vector2.zero);
            Label(g, pre + "CartMsg", "현재 위치를 읽는 중", TextAnchor.MiddleLeft, baseFontSize - 6, TextDim,
                  new Vector2(0.03f, 0.062f), new Vector2(0.75f, 0.118f), Vector2.zero, Vector2.zero);
            Btn(g, pre + "CartRead", "읽기", new Vector2(0.76f, 0.062f), new Vector2(0.97f, 0.118f), SubBg, TextDim);

            // ── 스텝 (mm) ──────────────────────────────────────
            Label(g, pre + "CartStepNow", "5mm", TextAnchor.MiddleLeft, baseFontSize - 6, Accent,
                  new Vector2(0.03f, 0.005f), new Vector2(0.20f, 0.058f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < CartSteps.Length; i++)
            {
                float x0 = 0.21f + i * 0.195f;
                Btn(g, $"{pre}CartStep{i}", $"{CartSteps[i]:0}mm",
                    new Vector2(x0, 0.005f), new Vector2(x0 + 0.185f, 0.058f), SubBg, Accent);
            }
        }

        void BuildCenter(RectTransform p)
        {
            var c = Panel(p, "Center", new Vector2(0, 0), new Vector2(1, 1), new Vector2(LeftW, 0), new Vector2(0, -TopH), new Color(0, 0, 0, 0));
            ViewCell(c, "Top", "TOP VIEW", 0.005f, 0.505f, 0.497f, 0.995f);
            ViewCell(c, "Side", "SIDE VIEW", 0.503f, 0.505f, 0.995f, 0.995f);
            ViewCell(c, "Front", "FRONT VIEW", 0.005f, 0.005f, 0.497f, 0.495f);
            StatusCell(c, 0.503f, 0.005f, 0.995f, 0.495f);

            foreach (var k in PKeys)
            {
                if (transform.Find("PreviewCam_" + k) != null) continue;
                var go = new GameObject("PreviewCam_" + k, typeof(Camera));
                go.transform.SetParent(transform, false);
                var cam = go.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color32(0x0E, 0x0E, 0x11, 0xFF);
                cam.fieldOfView = 38f; cam.depth = -20; cam.allowHDR = false;
            }
        }

        void ViewCell(RectTransform p, string key, string title, float x0, float y0, float x1, float y1)
        {
            var cell = Panel(p, "ViewCell_" + key, new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero, PanelBg);
            Rule(cell); Frame(cell, Edge);
            TitleIcon(cell, "ViewIcon_" + key);
            Label(cell, "ViewLbl_" + key, title, TextAnchor.MiddleLeft, baseFontSize - 2, Accent,
                  new Vector2(0, 0.93f), new Vector2(1, 1), new Vector2(44, 0), new Vector2(-14, -5));
            var go = new GameObject("ViewImg_" + key, typeof(RectTransform), typeof(RawImage), typeof(PreviewOrbit));
            go.transform.SetParent(cell, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1, 0.925f);
            rt.offsetMin = new Vector2(6, 6); rt.offsetMax = new Vector2(-6, -2);

            var raw = go.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = true;    // 드래그를 받으려면 반드시 켜져 있어야 한다

            Label(cell, "ViewHint_" + key, "드래그 회전 · 휠 확대 · 우클릭 초기화",
                  TextAnchor.MiddleRight, baseFontSize - 8, TextDim,
                  new Vector2(0.35f, 0.93f), new Vector2(1, 1), Vector2.zero, new Vector2(-14, -5));
        }

        void StatusCell(RectTransform p, float x0, float y0, float x1, float y1)
        {
            var c = Panel(p, "StatusCell", new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero, PanelBg);
            Rule(c); Frame(c, Edge);
            TitleIcon(c, "StatusIcon");
            Label(c, "StatusTitle", "ROBOT STATUS", TextAnchor.MiddleLeft, baseFontSize - 2, Accent,
                  new Vector2(0, 0.90f), new Vector2(0.6f, 1), new Vector2(44, 0), new Vector2(0, -5));
            Label(c, "ClockNow", "", TextAnchor.MiddleRight, baseFontSize - 4, TextDim,
                  new Vector2(0.6f, 0.90f), new Vector2(1, 1), Vector2.zero, new Vector2(-14, -5));

            // ── 표 ──────────────────────────────────────────────
            // 열 경계. 라벨을 흩뿌리는 대신 실제 표로 그린다.
            float[] col = { 0.03f, 0.32f, 0.49f, 0.66f, 0.82f, 0.97f };
            string[] head = { "로봇", "연결", "발열", "전압", "부하" };
            float hTop = 0.86f, hBot = 0.735f;   // 머리글 행
            float r1Top = 0.735f, r1Bot = 0.615f;
            float r2Top = 0.615f, r2Bot = 0.495f;

            // 머리글 배경
            Panel(c, "TblHeadBg", new Vector2(col[0], hBot), new Vector2(col[5], hTop), Vector2.zero, Vector2.zero, SubBg);
            // 로봇2 행에 옅은 배경 — 줄이 갈려 읽기 쉽게
            Panel(c, "TblRow2Bg", new Vector2(col[0], r2Bot), new Vector2(col[5], r2Top), Vector2.zero, Vector2.zero,
                  new Color32(0x18, 0x19, 0x1D, 0xFF));

            for (int i = 0; i < head.Length; i++)
                Label(c, "H" + i, head[i], i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                      baseFontSize - 5, Accent,
                      new Vector2(col[i], hBot), new Vector2(col[i + 1], hTop),
                      i == 0 ? new Vector2(10, 0) : Vector2.zero, Vector2.zero);

            // 세로 구분선
            for (int i = 1; i < col.Length - 1; i++)
            {
                var v = Panel(c, "TblVLine" + i, new Vector2(col[i], r2Bot), new Vector2(col[i], hTop),
                              new Vector2(0, 0), new Vector2(1, 0), new Color32(0x30, 0x32, 0x38, 0xFF));
                v.GetComponent<Image>().raycastTarget = false;
            }
            // 가로 구분선 (머리글 아래 · 행 사이)
            foreach (var y in new[] { hBot, r1Bot })
            {
                var hl = Panel(c, "TblHLine", new Vector2(col[0], y), new Vector2(col[5], y),
                               new Vector2(0, 0), new Vector2(0, 1), new Color32(0x30, 0x32, 0x38, 0xFF));
                hl.GetComponent<Image>().raycastTarget = false;
            }

            StatusRow(c, "S1", col, r1Bot, r1Top);
            StatusRow(c, "S2", col, r2Bot, r2Top);

            Label(c, "SrvState", "● 서버 끊김", TextAnchor.MiddleLeft, baseFontSize - 4, Bad,
                  new Vector2(0.04f, 0.30f), new Vector2(0.55f, 0.42f), Vector2.zero, Vector2.zero);
            Label(c, "SrvDetail", "", TextAnchor.MiddleLeft, baseFontSize - 5, TextDim,
                  new Vector2(0.04f, 0.20f), new Vector2(0.97f, 0.30f), Vector2.zero, Vector2.zero);

            InlineIcon(c, "SpeedIcon", new Vector2(0.035f, 0.10f), new Vector2(0.075f, 0.18f));
            Label(c, "SpeedTitle", "속도", TextAnchor.MiddleLeft, baseFontSize - 4, TextDim, new Vector2(0.085f, 0.09f), new Vector2(0.20f, 0.19f), Vector2.zero, Vector2.zero);
            Sldr(c, "VelSlider", 0, 3000, 800, new Vector2(0.20f, 0.11f), new Vector2(0.44f, 0.17f));
            Label(c, "VelValue", "800", TextAnchor.MiddleRight, baseFontSize - 4, Accent, new Vector2(0.44f, 0.09f), new Vector2(0.52f, 0.19f), Vector2.zero, Vector2.zero);
            InlineIcon(c, "AccelIcon", new Vector2(0.545f, 0.10f), new Vector2(0.585f, 0.18f));
            Label(c, "AccTitle", "가속", TextAnchor.MiddleLeft, baseFontSize - 4, TextDim, new Vector2(0.595f, 0.09f), new Vector2(0.70f, 0.19f), Vector2.zero, Vector2.zero);
            Sldr(c, "AccSlider", 1, 254, 50, new Vector2(0.70f, 0.11f), new Vector2(0.90f, 0.17f));
            Label(c, "AccValue", "50", TextAnchor.MiddleRight, baseFontSize - 4, Accent, new Vector2(0.90f, 0.09f), new Vector2(0.97f, 0.19f), Vector2.zero, Vector2.zero);
        }

        void StatusRow(RectTransform c, string k, float[] col, float bot, float top)
        {
            string[] key = { "Name", "Conn", "Temp", "Volt", "Load" };
            float h = top - bot;

            for (int i = 0; i < key.Length; i++)
            {
                // 숫자는 위쪽에, 게이지는 칸 바닥에 얇게 깐다
                float tBot = (i >= 2) ? bot + h * 0.28f : bot;
                Label(c, k + key[i], i == 0 ? "" : NA,
                      i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                      baseFontSize - 4, i == 0 ? TextMain : TextDim,
                      new Vector2(col[i], tBot), new Vector2(col[i + 1], top),
                      i == 0 ? new Vector2(30, 0) : Vector2.zero, Vector2.zero);   // LED 자리 확보

                if (i >= 2)   // 발열 · 전압 · 부하에만 게이지
                    Gauge(c, k + key[i] + "Bar",
                          new Vector2(col[i] + 0.012f, bot + h * 0.10f),
                          new Vector2(col[i + 1] - 0.012f, bot + h * 0.22f));
            }

            Led(c, k + "Led", new Vector2(col[0] + 0.012f, (bot + top) * 0.5f), 12f);
        }

        void BuildRecord(RectTransform p)
        {
            var v = Panel(p, "RecordPanel", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-RecW, 0), new Vector2(0, -TopH), Bg);
            Rule(v); Frame(v, Edge);
            // 제목은 상단 바 버튼과 같은 말(Recorder)로 맞춘다. 같은 것을 두 이름으로 부르면 헷갈린다.
            Label(v, "RoutineTitle", "Recorder", TextAnchor.MiddleLeft, titleFontSize, Accent,
                  new Vector2(0, 0.955f), new Vector2(0.30f, 1), new Vector2(18, 0), Vector2.zero);

            // 루틴 이름 = 저장 파일명.
            // 입력칸이 없으면 언제나 Untitled.json 하나만 덮어쓰게 되어 루틴을 여러 개 못 만든다.
            Input(v, "RoutineNameIn", new Vector2(0.30f, 0.957f), new Vector2(0.80f, 0.998f));

            Label(v, "RoutineCount", "", TextAnchor.MiddleRight, baseFontSize - 4, TextDim,
                  new Vector2(0.80f, 0.955f), new Vector2(1, 1), Vector2.zero, new Vector2(-18, 0));

            // 목록은 줄마다 버튼이다. 글자 덩어리 하나로 두면 클릭으로 고를 수가 없어
            // 선택용 화살표 버튼을 따로 둬야 했다. 직접 누르는 편이 낫다.
            var lb = Panel(v, "RoutineListBg", new Vector2(0.03f, 0.305f), new Vector2(0.97f, 0.945f), Vector2.zero, Vector2.zero, SubBg);
            for (int i = 0; i < RoutineRows; i++)
            {
                float h = 1f / RoutineRows;
                float top = 1f - i * h, bot = top - h;
                var rb = Btn(lb, "RoutineRow" + i, "", new Vector2(0, bot), new Vector2(1, top),
                             new Color(0, 0, 0, 0), TextMain);
                var lbl = rb.transform.Find("RoutineRow" + i + "Label") as RectTransform;
                if (lbl != null)
                {
                    var t = lbl.GetComponent<Text>();
                    t.alignment = TextAnchor.MiddleLeft;
                    t.resizeTextForBestFit = false;      // 줄마다 크기가 달라지면 읽기 나쁘다
                    t.fontSize = baseFontSize - 4;
                    lbl.offsetMin = new Vector2(14, 0);
                    lbl.offsetMax = new Vector2(-10, 0);
                }
            }
            Label(v, "RoutineEmpty", "", TextAnchor.UpperLeft, baseFontSize - 3, TextDim,
                  new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.93f), Vector2.zero, Vector2.zero);

            Label(v, "RoutinePath", "", TextAnchor.MiddleLeft, baseFontSize - 5, TextDim,
                  new Vector2(0.03f, 0.255f), new Vector2(0.97f, 0.298f), Vector2.zero, Vector2.zero);
            Label(v, "RoutineStatus", "", TextAnchor.MiddleLeft, baseFontSize - 5, TextDim,
                  new Vector2(0.03f, 0.212f), new Vector2(0.97f, 0.253f), Vector2.zero, Vector2.zero);

            // 스텝 추가 — 4칸 균등
            BtnRow(v, 0.03f, 0.97f, 0.135f, 0.202f, 0.012f, new[]
            {
                ("BtnAddR1",   "R1",   SubBg, Accent),
                ("BtnAddR2",   "R2",   SubBg, Accent),
                ("BtnAddBoth", "둘 다", SubBg, Accent),
                ("BtnAddWait", "대기",  SubBg, Accent),
            }, labelMax: 18);

            // 반복은 버튼 하나로 합쳤다.
            // 시작/끝은 짝이어야 하는데, 버튼이 둘이면 순서를 틀리거나 짝을 안 맞추기 쉽다.
            // 열려 있으면 "끝", 닫혀 있으면 "시작"이 되도록 상태를 보고 바뀐다.
            Btn(v, "BtnLoop", "반복 시작", new Vector2(0.03f, 0.068f), new Vector2(0.29f, 0.130f), SubBg, Accent);

            // ALL = 루틴 전체 재생. 아래 '재생' 은 선택한 스텝 하나만 돌린다.
            Btn(v, "BtnPlayAll", "ALL", new Vector2(0.30f, 0.068f), new Vector2(0.52f, 0.130f), SubBg, Accent);

            // ⚠️ 두 쌍은 기능이 다르다.
            //    선택 = 목록에서 커서를 옮긴다 (스텝 순서는 그대로)
            //    순서 = 선택된 스텝 자체를 위아래로 옮긴다 (실행 순서가 바뀐다)
            // 선택 화살표는 없앴다 — 목록을 직접 누르면 되므로 버튼 두 개가 불필요했다.
            Btn(v, "BtnMoveUp", "순서 ▲", new Vector2(0.55f, 0.068f), new Vector2(0.755f, 0.130f), SubBg, Warn);
            Btn(v, "BtnMoveDn", "순서 ▼", new Vector2(0.765f, 0.068f), new Vector2(0.97f, 0.130f), SubBg, Warn);

            // 닫기(✕)는 뺐다. 상단 Recorder 버튼이 열고 닫는 토글이라 같은 일을 하는 버튼이 둘이었다.
            // 그 자리를 나눠 나머지 버튼을 넓혔다.
            // 실행 · 파일 — 5칸 균등. 폭·높이·글자 크기를 전부 같게 맞춘다.
            BtnRow(v, 0.03f, 0.97f, 0.005f, 0.063f, 0.012f, new[]
            {
                ("BtnPlay", "재생",     Accent, Color.black),
                ("BtnDel",  "삭제",     SubBg,  Warn),
                ("BtnSave", "저장",     SubBg,  Accent),
                ("BtnNew",  "새로",     SubBg,  Accent),
                ("BtnLoad", "가져오기", SubBg,  Accent),
            }, labelMax: 18);

            BuildLoadPicker(v);

            v.gameObject.SetActive(false);
        }

        /// <summary>
        /// 저장된 루틴을 골라 여는 창. 리코더 패널 위에 덮어 띄운다.
        ///
        /// 리코더 패널은 이미 꽉 차 있어 목록을 넣을 자리가 없다. 아래로 늘리면
        /// 사용자가 손봐 저장해 둔 배치가 밀린다. 필요할 때만 덮는 편이 안전하다.
        /// </summary>
        void BuildLoadPicker(RectTransform p)
        {
            var v = Panel(p, "LoadPanel", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero, Bg);
            Frame(v, Accent);

            Label(v, "LoadTitle", "루틴 불러오기", TextAnchor.MiddleLeft, titleFontSize - 2, Accent,
                  new Vector2(0.04f, 0.925f), new Vector2(0.70f, 0.985f), Vector2.zero, Vector2.zero);
            Label(v, "LoadCount", "", TextAnchor.MiddleRight, baseFontSize - 4, TextDim,
                  new Vector2(0.70f, 0.925f), new Vector2(0.96f, 0.985f), Vector2.zero, Vector2.zero);

            var lb = Panel(v, "LoadListBg", new Vector2(0.04f, 0.30f), new Vector2(0.96f, 0.915f), Vector2.zero, Vector2.zero, SubBg);
            for (int i = 0; i < LoadRows; i++)
            {
                float h = 1f / LoadRows;
                float top = 1f - i * h, bot = top - h;
                var rb = Btn(lb, "LoadRow" + i, "", new Vector2(0, bot), new Vector2(1, top),
                             new Color(0, 0, 0, 0), TextMain);
                var lbl = rb.transform.Find("LoadRow" + i + "Label") as RectTransform;
                if (lbl != null)
                {
                    var t = lbl.GetComponent<Text>();
                    t.alignment = TextAnchor.MiddleLeft;
                    t.resizeTextForBestFit = false;
                    t.fontSize = baseFontSize - 4;
                    lbl.offsetMin = new Vector2(14, 0);
                    lbl.offsetMax = new Vector2(-10, 0);
                }
            }
            Label(v, "LoadEmpty", "", TextAnchor.UpperLeft, baseFontSize - 3, TextDim,
                  new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.90f), Vector2.zero, Vector2.zero);

            // 지금 열려 있는 것이 사라진다는 사실은 누르기 전에 보여야 한다.
            Label(v, "LoadWarn", "", TextAnchor.MiddleLeft, baseFontSize - 4, Warn,
                  new Vector2(0.04f, 0.235f), new Vector2(0.96f, 0.293f), Vector2.zero, Vector2.zero);

            Btn(v, "BtnLoadPrev", "▲", new Vector2(0.04f, 0.135f), new Vector2(0.24f, 0.225f), SubBg, Accent);
            Label(v, "LoadPage", "", TextAnchor.MiddleCenter, baseFontSize - 4, TextDim,
                  new Vector2(0.24f, 0.135f), new Vector2(0.76f, 0.225f), Vector2.zero, Vector2.zero);
            Btn(v, "BtnLoadNext", "▼", new Vector2(0.76f, 0.135f), new Vector2(0.96f, 0.225f), SubBg, Accent);

            BtnRow(v, 0.04f, 0.96f, 0.03f, 0.125f, 0.02f, new[]
            {
                ("BtnLoadOk",     "불러오기", Accent, Color.black),
                ("BtnLoadCancel", "취소",     SubBg,  TextMain),
            }, labelMax: 20);

            v.gameObject.SetActive(false);
        }

        // ── 헬퍼 ────────────────────────────────────────────────

        RectTransform Panel(Transform p, string n, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color col)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(p, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            var img = go.GetComponent<Image>(); img.color = col; img.raycastTarget = col.a > 0.02f;
            return rt;
        }

        void Rule(Transform p)
        {
            var rt = Panel(p, "Rule", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -3), Vector2.zero, Accent);
            rt.GetComponent<Image>().raycastTarget = false;
        }

        /// <summary>
        /// 패널 테두리 1px. 계기판처럼 칸이 또렷하게 끊겨 보이게 한다.
        /// Outline 컴포넌트는 Text 에만 먹으므로 얇은 패널 네 개로 그린다.
        /// </summary>
        void Frame(Transform p, Color c)
        {
            var e = new[]
            {
                (new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1)),   // 아래
                (new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -1), new Vector2(0, 0)),  // 위
                (new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0)),   // 왼쪽
                (new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 0), new Vector2(0, 0)),  // 오른쪽
            };
            foreach (var (aMin, aMax, oMin, oMax) in e)
            {
                var rt = Panel(p, "Edge", aMin, aMax, oMin, oMax, c);
                rt.GetComponent<Image>().raycastTarget = false;
            }
        }

        /// <summary>상태 LED. 글자 ● 대신 실제 점을 찍어야 계기판처럼 보인다.</summary>
        RectTransform Led(Transform p, string n, Vector2 anchor, float size = 14f)
        {
            var rt = Panel(p, n, anchor, anchor, Vector2.zero, Vector2.zero, TextDim);
            rt.sizeDelta = new Vector2(size, size);
            rt.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        /// <summary>패널 제목 왼쪽 아이콘 자리. 스프라이트는 ApplyIcons() 가 채운다.</summary>
        void TitleIcon(Transform p, string n, float size = 24f, Vector2? pos = null)
        {
            var rt = Panel(p, n, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos ?? new Vector2(12, -8);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
        }

        /// <summary>패널 중간에 끼우는 작은 아이콘. 제목용 TitleIcon 과 달리 비율 좌표로 놓는다.</summary>
        void InlineIcon(Transform p, string n, Vector2 aMin, Vector2 aMax)
        {
            var rt = Panel(p, n, aMin, aMax, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            var img = rt.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
        }

        /// <summary>계기판식 막대. 값의 크기를 눈으로 바로 잡게 한다.</summary>
        Image Gauge(Transform p, string n, Vector2 aMin, Vector2 aMax)
        {
            Panel(p, n + "Track", aMin, aMax, Vector2.zero, Vector2.zero, new Color32(0x26, 0x28, 0x2E, 0xFF));
            var rt = Panel(p, n, aMin, aMax, Vector2.zero, Vector2.zero, Accent);
            var img = rt.GetComponent<Image>();
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 0f;
            img.raycastTarget = false;
            return img;
        }

        Text Label(Transform p, string n, string s, TextAnchor a, int size, Color col, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(p, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            var t = go.GetComponent<Text>();
            t.font = uiFont; t.fontSize = size; t.color = col; t.alignment = a; t.text = s;
            t.raycastTarget = false; t.supportRichText = false;
            return t;
        }

        /// <summary>
        /// 한 줄에 나란히 놓는 버튼들. 폭·간격을 같게 나눠 준다.
        /// 손으로 좌표를 하나씩 적으면 0.25 / 0.19 / 0.145 처럼 제각각이 되어
        /// 글자 크기까지 달라 보인다.
        /// </summary>
        void BtnRow(Transform p, float x0, float x1, float y0, float y1, float gap,
                    (string id, string label, Color bg, Color fg)[] items, int labelMax = 0)
        {
            int n = items.Length;
            float w = (x1 - x0 - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                float bx = x0 + i * (w + gap);
                Btn(p, items[i].id, items[i].label,
                    new Vector2(bx, y0), new Vector2(bx + w, y1),
                    items[i].bg, items[i].fg, labelMax);
            }
        }

        Button Btn(Transform p, string n, string label, Vector2 aMin, Vector2 aMax, Color bg, Color fg,
                   int labelMax = 0)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(p, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = new Vector2(2, 2); rt.offsetMax = new Vector2(-2, -2);
            go.GetComponent<Image>().color = bg;
            var b = go.GetComponent<Button>();
            var cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1f, 1f, 1f, 0.72f);
            cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            b.colors = cb;
            // 버튼 글씨는 본문과 같은 크기로 둔다. 작게 깎으면 상단 바처럼
            // 짧은 칸에서 제일 먼저 안 보이게 되는 게 버튼 라벨이다.
            var lb = Label(go.transform, n + "Label", label, TextAnchor.MiddleCenter, baseFontSize, fg,
                           Vector2.zero, Vector2.one, new Vector2(2, 0), new Vector2(-2, 0));

            // 버튼 폭에 맞춰 글자를 자동으로 줄인다.
            // 고정 크기로 두니 "순서 ▲", "삭제" 처럼 좁은 버튼에서 글자가 잘렸다.
            lb.resizeTextForBestFit = true;
            lb.resizeTextMinSize = 11;
            // labelMax 를 주면 그 크기를 넘지 않는다. 한 줄에 놓인 버튼들의
            // 글자 크기를 같게 맞출 때 쓴다 (긴 라벨만 혼자 작아지는 것을 막는다).
            lb.resizeTextMaxSize = labelMax > 0 ? labelMax : baseFontSize;
            return b;
        }

        Slider Sldr(Transform p, string n, float min, float max, float val, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(p, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            Panel(go.transform, "Background", new Vector2(0, 0.3f), new Vector2(1, 0.7f), Vector2.zero, Vector2.zero, SubBg);
            var fa = Panel(go.transform, "Fill Area", new Vector2(0, 0.3f), new Vector2(1, 0.7f), Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            var fill = Panel(fa, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Accent);
            var ha = Panel(go.transform, "Handle Slide Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            var h = Panel(ha, "Handle", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero, Accent);
            h.sizeDelta = new Vector2(12, 0);

            var s = go.GetComponent<Slider>();
            s.fillRect = fill; s.handleRect = h; s.targetGraphic = h.GetComponent<Image>();
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min; s.maxValue = max; s.value = val;
            return s;
        }

        /// <summary>각도 입력창. 평소엔 현재 각도가 떠 있고, 지우고 쳐 넣으면 그 각도로 간다.</summary>
        InputField Input(Transform p, string n, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(p, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = new Vector2(2, 3); rt.offsetMax = new Vector2(-2, -3);
            go.GetComponent<Image>().color = SubBg;

            var txt = Label(go.transform, n + "Text", "", TextAnchor.MiddleRight, baseFontSize - 4, TextMain,
                            Vector2.zero, Vector2.one, new Vector2(4, 1), new Vector2(-4, -1));
            txt.raycastTarget = false;
            txt.supportRichText = false;

            var f = go.GetComponent<InputField>();
            f.textComponent = txt;
            f.contentType = InputField.ContentType.Standard;   // 음수·소수를 받아야 하므로 숫자 전용은 쓰지 않는다
            f.lineType = InputField.LineType.SingleLine;
            f.characterLimit = 8;
            return f;
        }
    }
}
