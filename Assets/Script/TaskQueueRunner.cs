using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 작업 큐 실행기. 저장된 루틴 여러 개를 줄 세워 연속 실행한다.
    ///
    /// 【원칙 — RecordManager 를 고치지 않고 위에 얹는다】
    ///   큐는 `LoadProject` → `StartPlayback` → `IsPlaying` 감시만 쓴다. 스텝의
    ///   순서·반복·대기는 이미 `RecordManager` 가 하고 있으므로 다시 만들지 않는다.
    ///   유일한 예외가 실패 신호(`LastPlaybackFailed`)다 — 그게 없으면 `stopOnError`
    ///   가 죽은 옵션이 되어 팔이 목표에 못 간 채로 다음 루틴이 이어진다.
    ///
    /// 【제일 중요한 규칙 — 다음 항목 전에 실제 완료를 확인한다】
    ///   고정 시간 대기를 쓰면 안 된다. 2026-08-02 에 잡은 "루틴 재생이 스텝을
    ///   건너뛰던 문제" 와 정확히 같은 부류다. 그때는 목표만 던지고 0.5초를 기다려
    ///   팔이 도착하기 전에 다음 목표가 덮어썼다. 큐에서 같은 실수를 하면 이전
    ///   루틴이 도는 중에 `LoadProject` 가 `CurrentProject` 를 갈아치운다.
    ///
    /// 명세: `docs/TASK_QUEUE.md`, 요구사항 `SR_21` / `FR-42`~`FR-48`.
    /// </summary>
    [DisallowMultipleComponent]
    public class TaskQueueRunner : MonoBehaviour
    {
        public enum QueueState { Idle, Running, Paused, Complete, Aborted }

        [Header("매니저 연결 (비우면 자동 탐색)")]
        public RecordManager recordManager;
        public SOArmDualManager dualManager;

        [Header("실행 상태 (읽기 전용)")]
        [SerializeField] private QueueState state = QueueState.Idle;
        [SerializeField] private string statusMessage = "Idle";

        [Tooltip("StartPlayback() 을 부른 뒤 재생이 실제로 시작될 때까지 기다리는 최대 시간(초).\n" +
                 "이 시간을 넘으면 재생이 시작조차 못 한 것으로 보고 실패 처리한다.")]
        public float playbackStartTimeoutSec = 2f;

        TaskQueue current;

        /// <summary>
        /// 현재 편집·실행 중인 큐.
        ///
        /// 처음 읽을 때 만든다. `Awake` 에서 만들면 에디터에서 관제 화면을 생성하며
        /// 이 컴포넌트를 막 붙였을 때는 `Awake` 가 아직 안 돌아 `null` 인 채로 보인다.
        /// </summary>
        public TaskQueue Current
        {
            get { if (current == null) current = TaskQueue.NewQueue("Untitled"); return current; }
            private set { current = value; }
        }

        public QueueState State => state;
        public string StatusMessage => statusMessage;

        /// <summary>
        /// 큐가 팔을 잡고 있는가.
        ///
        /// 일시정지 중에도 참이다 — 항목 경계에서 멈춘 것뿐이고 곧 이어서 돌기
        /// 때문에, 이 틈에 사람이 슬라이더로 팔을 옮겨 두면 다음 루틴이 엉뚱한
        /// 자세에서 시작한다. 관제 화면은 이 값을 보고 관절 입력을 막는다.
        /// </summary>
        public bool IsBusy => state == QueueState.Running || state == QueueState.Paused;

        Coroutine runCoroutine;
        bool pauseRequested;
        bool skipRequested;

        /// <summary>큐를 시작할 때 통신이 살아 있었는가. 6절 3번(소켓 끊김) 판정에 쓴다.</summary>
        bool startedConnected;

        void Start()
        {
            if (recordManager == null) recordManager = FindAnyObjectByType<RecordManager>();
            if (dualManager == null) dualManager = FindAnyObjectByType<SOArmDualManager>();
        }

        // ════════════════════════════════════════
        //              저장 / 불러오기 (FR-46)
        // ════════════════════════════════════════

        /// <summary>
        /// 큐 파일이 사는 곳 — `Recordings/Queues/`.
        ///
        /// ⚠️ 루틴과 **같은 폴더에 두면 안 된다.** `RecordManager.ListSavedFiles()` 는
        ///    `Recordings/` 의 `.json` 을 전부 루틴으로 간주한다. 큐 파일을 옆에 두면
        ///    "루틴 불러오기" 목록에 큐가 섞여 나오고, 고르면 `waypoints` 가 없어
        ///    빈 루틴이 열린다. `GetFiles` 는 기본이 비재귀라 하위 폴더로 내리면
        ///    기존 코드를 건드리지 않고 분리된다.
        /// </summary>
        public string QueuesFolder
        {
            get
            {
                string root = recordManager != null
                    ? recordManager.RecordingsFolder
                    : Path.Combine(Application.dataPath, "..", "Recordings");
                string folder = Path.Combine(root, "Queues");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public void NewQueue(string name = "Untitled")
        {
            AbortQueue();
            Current = TaskQueue.NewQueue(name);
            statusMessage = $"새 큐: {name}";
            Debug.Log($"[큐] {statusMessage}");
        }

        public void SetQueueName(string name)
        {
            if (Current == null || string.IsNullOrWhiteSpace(name)) return;
            Current.queueName = name;
            Current.Touch();
        }

        public bool SaveQueue(string fileName)
        {
            if (Current == null) return false;
            try
            {
                if (!fileName.EndsWith(".json")) fileName += ".json";
                string path = Path.Combine(QueuesFolder, fileName);
                File.WriteAllText(path, JsonUtility.ToJson(Current, prettyPrint: true));
                statusMessage = $"💾 큐 저장됨: {fileName}";
                Debug.Log($"[큐] {statusMessage} → {path}");
                return true;
            }
            catch (Exception e)
            {
                statusMessage = $"❌ 큐 저장 실패: {e.Message}";
                Debug.LogError($"[큐] {statusMessage}");
                return false;
            }
        }

        public bool LoadQueue(string fileName)
        {
            try
            {
                if (!fileName.EndsWith(".json")) fileName += ".json";
                string path = Path.Combine(QueuesFolder, fileName);
                if (!File.Exists(path))
                {
                    statusMessage = $"❌ 큐 파일 없음: {fileName}";
                    Debug.LogWarning($"[큐] {statusMessage}");
                    return false;
                }

                AbortQueue();
                Current = JsonUtility.FromJson<TaskQueue>(File.ReadAllText(path));
                if (Current == null) { statusMessage = "❌ 큐를 읽을 수 없습니다"; return false; }
                if (Current.items == null) Current.items = new List<QueueItem>();

                // 저장된 실행 상태는 버린다. 언제나 처음부터 돈다 (명세 미결 O-3).
                Current.ResetRunState();

                statusMessage = $"📂 큐 불러옴: {Current.queueName} ({Current.Count}건)";
                Debug.Log($"[큐] {statusMessage}");
                return true;
            }
            catch (Exception e)
            {
                statusMessage = $"❌ 큐 불러오기 실패: {e.Message}";
                Debug.LogError($"[큐] {statusMessage}");
                return false;
            }
        }

        /// <summary>`Recordings/Queues/` 의 큐 파일 목록.</summary>
        public string[] ListSavedQueues()
        {
            try
            {
                var files = Directory.GetFiles(QueuesFolder, "*.json");
                for (int i = 0; i < files.Length; i++) files[i] = Path.GetFileName(files[i]);
                return files;
            }
            catch { return new string[0]; }
        }

        /// <summary>큐에 넣을 후보 = 저장된 루틴 전부. `RecordManager` 가 가진 목록을 그대로 쓴다.</summary>
        public string[] ListRoutineCandidates()
            => recordManager != null ? recordManager.ListSavedFiles() : new string[0];

        // ════════════════════════════════════════
        //                항목 조작 (FR-43·44)
        // ════════════════════════════════════════
        //
        // 실행 중에는 목록을 바꾸지 않는다. 돌고 있는 항목의 인덱스가 밀리면
        // 엉뚱한 루틴이 다음 차례가 된다.

        public bool CanEdit => !IsBusy;

        public void AddItem(string routineFileName)
        {
            if (!CanEdit || Current == null) return;
            var it = Current.Add(routineFileName);
            if (it != null)
            {
                statusMessage = $"✚ 큐에 추가: {it.Title}";
                Debug.Log($"[큐] {statusMessage}");
            }
        }

        public void RemoveItem(int i) { if (CanEdit) Current?.Remove(i); }
        public void MoveItemUp(int i) { if (CanEdit) Current?.MoveUp(i); }
        public void MoveItemDown(int i) { if (CanEdit) Current?.MoveDown(i); }
        public void SetRepeat(int i, int n) { if (CanEdit) Current?.SetRepeat(i, n); }
        public void ToggleEnabled(int i) { if (CanEdit) Current?.ToggleEnabled(i); }

        // ════════════════════════════════════════
        //                  실행 (FR-42·45)
        // ════════════════════════════════════════

        public void StartQueue()
        {
            if (IsBusy) { Debug.LogWarning("[큐] 이미 실행 중"); return; }
            if (recordManager == null)
            {
                statusMessage = "❌ RecordManager 가 없습니다";
                Debug.LogWarning($"[큐] {statusMessage}"); return;
            }
            if (Current == null || Current.Count == 0)
            {
                statusMessage = "❌ 큐가 비어 있습니다";
                Debug.LogWarning($"[큐] {statusMessage}"); return;
            }
            if (Current.EnabledCount == 0)
            {
                statusMessage = "❌ 켜져 있는 항목이 없습니다";
                Debug.LogWarning($"[큐] {statusMessage}"); return;
            }
            // 🛑 비상 정지는 큐보다 위다. 정지 중에는 시작 자체를 막는다.
            if (EmergencyStopped)
            {
                statusMessage = "🛑 비상정지 중 — 해제 후 시작하세요";
                Debug.LogWarning($"[큐] {statusMessage}"); return;
            }

            Current.ResetRunState();
            pauseRequested = false;
            skipRequested = false;
            startedConnected = AnyLive;
            runCoroutine = StartCoroutine(RunRoutine());
        }

        /// <summary>
        /// 일시정지 — **현재 루틴은 끝까지 재생하고 다음 항목 앞에서 멈춘다.**
        ///
        /// `RecordManager` 에 일시정지가 없어서 그렇다. `StopPlayback()` 은 코루틴을
        /// 죽이고 `currentStepIndex` 를 -1 로 되돌리므로 재개 지점이 남지 않는다.
        /// 스텝 중간 일시정지는 `PlaybackRoutine()` 을 재개 가능한 형태로 고치는
        /// 별개 작업이다 (명세 미결 O-2).
        /// </summary>
        public void RequestPause()
        {
            if (state != QueueState.Running) return;
            pauseRequested = true;
            statusMessage = "⏸ 일시정지 요청 — 현재 항목을 마치고 멈춥니다";
            Debug.Log($"[큐] {statusMessage}");
        }

        public void Resume()
        {
            if (state != QueueState.Paused) return;
            pauseRequested = false;
            Debug.Log("[큐] ▶ 재개");
        }

        /// <summary>일시정지 ↔ 재개. 버튼 하나로 쓰기 위한 것.</summary>
        public void TogglePause()
        {
            if (state == QueueState.Paused) Resume();
            else RequestPause();
        }

        /// <summary>현재 항목을 접고 다음 항목으로. 접힌 항목은 `skipped` 로 남는다.</summary>
        public void SkipCurrent()
        {
            if (!IsBusy) return;

            // 일시정지 중에는 받지 않는다. 지금 도는 항목이 없어서 접을 것도 없고,
            // 요청만 세워 두면 재개할 때 항목 시작에서 지워져 아무 일도 안 일어난다.
            // 눌렀는데 조용한 것이 제일 나쁘다.
            if (state == QueueState.Paused)
            {
                statusMessage = "⏸ 일시정지 중입니다 — «재개» 후에 건너뛰세요";
                Debug.LogWarning($"[큐] {statusMessage}");
                return;
            }

            skipRequested = true;
            recordManager?.StopPlayback();
            statusMessage = "⏭ 현재 항목 건너뜀";
            Debug.Log($"[큐] {statusMessage}");
        }

        /// <summary>큐를 접는다. **팔은 현재 자세를 그대로 유지한다** — 홈으로 보내지 않는다.</summary>
        public void AbortQueue()
        {
            if (runCoroutine != null) { StopCoroutine(runCoroutine); runCoroutine = null; }

            // ⚠️ 큐가 돌고 있을 때만 재생을 끊는다.
            //    `NewQueue`·`LoadQueue` 도 이 함수를 거치는데, 무조건 끊으면
            //    사용자가 Recorder 에서 직접 돌리던 루틴까지 같이 멈춘다.
            if (IsBusy) recordManager?.StopPlayback();

            if (Current != null)
            {
                // 돌던 항목을 running 인 채로 두면 다음에 열었을 때 진행 중으로 보인다.
                var it = Current.At(Current.currentIndex);
                if (it != null && it.IsRunning) it.MarkFailed("중단됨");
                Current.currentIndex = -1;
            }

            pauseRequested = false;
            skipRequested = false;

            // ABORTED 는 지나가는 상태다 (명세 5절 그림: ABORTED ──▶ IDLE).
            // 머무르지 않으므로 화면에는 남기지 않고 로그로만 알린다.
            if (IsBusy)
            {
                statusMessage = "⏹ 큐 중단 — 팔은 현재 자세를 유지합니다";
                Debug.Log($"[큐] {statusMessage}");
            }
            state = QueueState.Idle;
        }

        // ════════════════════════════════════════
        //                 실행 루프
        // ════════════════════════════════════════

        IEnumerator RunRoutine()
        {
            state = QueueState.Running;
            statusMessage = "▶ 큐 시작";
            Debug.Log($"[큐] {statusMessage}");

            do
            {
                for (int i = 0; i < Current.Count; i++)
                {
                    Current.currentIndex = i;
                    var item = Current.At(i);
                    if (item == null) continue;

                    // 꺼둔 항목은 지나간다 (FR-44)
                    if (!item.enabled) { item.MarkSkipped(); continue; }

                    // ── 항목 경계 일시정지 (FR-48) ──
                    yield return HoldWhilePaused();
                    if (state != QueueState.Running) yield break;

                    // 🛑 비상정지는 큐보다 위다
                    if (EmergencyStopped) { StopAll("🛑 비상정지 — 큐 중단", item, "비상정지"); yield break; }

                    // 소켓이 끊기면 stopOnError 와 무관하게 즉시 중단한다.
                    // 통신이 없으면 다음 항목도 실패하므로 실패 항목을 쌓을 이유가 없다.
                    //
                    // 처음부터 안 붙어 있던 경우는 막지 않는다 — 라파 없이 시뮬레이션만으로
                    // 큐를 짜 보는 것이 실제 작업 순서다. 막는 것은 "돌던 중에 끊긴" 경우다.
                    if (startedConnected && !AnyLive)
                    { StopAll("🔌 통신 끊김 — 큐 중단", item, "소켓 끊김"); yield break; }

                    skipRequested = false;
                    item.MarkRunning();

                    bool ok = true;
                    for (int rep = 0; rep < Mathf.Max(1, item.repeatCount); rep++)
                    {
                        statusMessage = $"▶ {i + 1}/{Current.Count} {item.Title}"
                                      + (item.repeatCount > 1 ? $" ({rep + 1}/{item.repeatCount}회)" : "");
                        Debug.Log($"[큐] {statusMessage}");

                        yield return RunOnce(item);

                        if (skipRequested) { item.MarkSkipped(); ok = false; break; }
                        if (state != QueueState.Running) yield break;   // 중단·비상정지로 접혔다
                        if (item.IsFailed) { ok = false; break; }
                    }

                    if (ok) item.MarkDone();

                    if (item.IsFailed && Current.stopOnError)
                    {
                        statusMessage = $"⛔ {item.Title} 실패 — 큐를 멈춥니다 ({item.lastError})";
                        Debug.LogWarning($"[큐] {statusMessage}");
                        Current.currentIndex = -1;
                        state = QueueState.Idle;
                        runCoroutine = null;
                        yield break;
                    }

                    // ⚠️ 항목마다 최소 한 프레임은 넘긴다.
                    //    RunOnce 는 루틴 파일을 못 열면 yield 없이 돌아온다. 그 상태로
                    //    loopQueue 가 켜져 있으면 코루틴이 한 프레임 안에서 영원히
                    //    돌아 에디터가 멈춘다.
                    yield return null;
                }

                // 큐 전체 반복이면 상태를 지우고 다시 돈다
                if (Current.loopQueue)
                {
                    Debug.Log("[큐] 🔁 큐 한 바퀴 완료 — 다시 처음부터");
                    foreach (var it in Current.items) it?.ResetRunState();
                    yield return null;
                }
            }
            while (Current.loopQueue && state == QueueState.Running);

            Current.currentIndex = -1;
            state = QueueState.Complete;
            statusMessage = "✅ 큐 완료";
            Debug.Log($"[큐] {statusMessage}");
            runCoroutine = null;
        }

        /// <summary>
        /// 항목 하나를 한 번 실행한다 — 루틴을 적재하고, 재생하고, **실제로 끝날 때까지** 기다린다.
        /// </summary>
        IEnumerator RunOnce(QueueItem item)
        {
            // 1. 적재
            if (!recordManager.LoadProject(item.fileName))
            {
                item.MarkFailed($"루틴을 열 수 없음: {item.fileName}");
                yield break;
            }

            var proj = recordManager.CurrentProject;
            if (proj == null || proj.waypoints == null || proj.waypoints.Count == 0)
            {
                item.MarkFailed("스텝이 없는 루틴");
                yield break;
            }

            // 2. 재생 시작
            recordManager.StartPlayback();

            // StartPlayback 은 조건이 안 맞으면 조용히 돌아간다(비상정지·빈 루틴 등).
            // 코루틴 본문은 첫 yield 까지 그 자리에서 도므로, 시작됐다면 IsPlaying 이
            // 이미 참이다. 그래도 한 프레임 여유를 두고 본다.
            float startDeadline = Time.time + playbackStartTimeoutSec;
            while (!recordManager.IsPlaying && Time.time < startDeadline)
            {
                if (skipRequested || state != QueueState.Running) yield break;
                yield return null;
            }
            if (!recordManager.IsPlaying)
            {
                item.MarkFailed($"재생이 시작되지 않음 ({recordManager.StatusMessage})");
                yield break;
            }

            // 3. ⚠️ 실제 완료를 기다린다. 고정 시간 대기 금지 (FR-45).
            while (recordManager.IsPlaying)
            {
                if (skipRequested) yield break;

                if (EmergencyStopped)
                { StopAll("🛑 비상정지 — 큐 중단", item, "비상정지"); yield break; }

                if (startedConnected && !AnyLive)
                { StopAll("🔌 통신 끊김 — 큐 중단", item, "소켓 끊김"); yield break; }

                yield return null;
            }

            // 4. 재생이 실패로 끝났는지 본다 (명세 미결 O-1 의 결정)
            if (recordManager.LastPlaybackFailed)
                item.MarkFailed(recordManager.LastFailureReason);
        }

        /// <summary>일시정지 요청이 걸려 있는 동안 항목 경계에서 붙잡아 둔다.</summary>
        IEnumerator HoldWhilePaused()
        {
            if (!pauseRequested) yield break;

            state = QueueState.Paused;
            statusMessage = "⏸ 일시정지 — «재개» 를 누르세요";
            Debug.Log($"[큐] {statusMessage}");

            while (pauseRequested)
            {
                // 멈춰 있는 동안 비상정지가 걸리면 재개할 이유가 없다
                if (EmergencyStopped) { StopAll("🛑 비상정지 — 큐 중단", null, null); yield break; }
                yield return null;
            }

            state = QueueState.Running;
            statusMessage = "▶ 재개";
            Debug.Log($"[큐] {statusMessage}");
        }

        /// <summary>큐를 접고 팔을 세운다. 실행 중이던 항목이 있으면 사유를 남긴다.</summary>
        void StopAll(string message, QueueItem item, string reason)
        {
            recordManager?.StopPlayback();
            if (item != null && !string.IsNullOrEmpty(reason)) item.MarkFailed(reason);
            if (Current != null) Current.currentIndex = -1;
            state = QueueState.Idle;
            runCoroutine = null;
            statusMessage = message;
            Debug.LogWarning($"[큐] {statusMessage}");
        }

        // ── 상태 읽기 ───────────────────────────────────────────

        bool EmergencyStopped => dualManager != null && dualManager.EmergencyStopped;

        /// <summary>둘 중 하나라도 실물과 붙어 있는가.</summary>
        bool AnyLive => IsLive(dualManager?.robot1) || IsLive(dualManager?.robot2);

        static bool IsLive(SOArmManager m) => m != null && m.real != null && m.real.IsConnected;

        /// <summary>화면 아래에 찍는 «현재 n/m 스텝 · 남은 k건».</summary>
        public string ProgressText
        {
            get
            {
                if (Current == null) return "";
                int remain = Current.RemainingCount;

                if (!IsBusy)
                    return Current.Count == 0 ? "" : $"{Current.Count}건 · {Current.EnabledCount}건 실행 예정";

                var proj = recordManager?.CurrentProject;
                int total = proj?.waypoints?.Count ?? 0;
                int now = recordManager != null ? recordManager.CurrentStepIndex + 1 : 0;

                string steps = total > 0 ? $"현재 {now}/{total} 스텝" : "현재 --";
                return $"{steps} · 남은 {remain}건";
            }
        }
    }
}
