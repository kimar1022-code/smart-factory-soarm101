using System;
using System.Collections.Generic;

namespace SOArmControl
{
    /// <summary>
    /// 작업 큐 하나. 루틴 파일 여러 개를 실행 순서대로 담는다.
    ///
    /// `RecordProject` 와 같은 방식으로 `[Serializable]` + `JsonUtility` 를 쓴다.
    /// 새 직렬화 라이브러리를 들이지 않는다 — 저장 형식이 두 가지가 되면
    /// 마이그레이션 규칙도 두 벌이 된다.
    ///
    /// 명세: `docs/TASK_QUEUE.md` 3절, 요구사항 `FR-46`.
    /// </summary>
    [Serializable]
    public class TaskQueue
    {
        public string queueName;
        public string createdAt;          // ISO 8601. RecordProject 와 같은 규칙
        public string lastModifiedAt;
        public string version;            // 데이터 버전 (마이그레이션용)

        public List<QueueItem> items;     // 실행 순서대로

        /// <summary>실행 중인 항목. 비실행 시 -1.</summary>
        public int currentIndex = -1;

        /// <summary>큐 전체를 무한 반복할지.</summary>
        public bool loopQueue = false;

        /// <summary>
        /// 한 항목이 실패하면 큐를 멈출지. 기본 `true`.
        ///
        /// 이 옵션이 뜻을 가지려면 재생이 실패를 알려줘야 한다.
        /// `RecordManager.LastPlaybackFailed` 가 그 신호다 (명세 미결 O-1 의 결정).
        /// </summary>
        public bool stopOnError = true;

        public TaskQueue()
        {
            queueName = "Untitled";
            createdAt = DateTime.Now.ToString("o");
            lastModifiedAt = createdAt;
            version = "1.0";
            items = new List<QueueItem>();
        }

        public static TaskQueue NewQueue(string name)
        {
            var q = new TaskQueue();
            if (!string.IsNullOrWhiteSpace(name)) q.queueName = name;
            return q;
        }

        public void Touch() => lastModifiedAt = DateTime.Now.ToString("o");

        public int Count => items != null ? items.Count : 0;

        /// <summary>실행에 실제로 들어가는 항목 수. 꺼둔 것은 세지 않는다.</summary>
        public int EnabledCount
        {
            get
            {
                if (items == null) return 0;
                int n = 0;
                foreach (var it in items) if (it != null && it.enabled) n++;
                return n;
            }
        }

        /// <summary>아직 안 돈 항목 수. 화면의 «남은 n건» 에 쓴다.</summary>
        public int RemainingCount
        {
            get
            {
                if (items == null) return 0;
                int n = 0;
                foreach (var it in items) if (it != null && it.enabled && it.IsPending) n++;
                return n;
            }
        }

        public bool InRange(int i) => items != null && i >= 0 && i < items.Count;

        public QueueItem At(int i) => InRange(i) ? items[i] : null;

        // ════════════════════════════════════════
        //              항목 조작 (FR-43)
        // ════════════════════════════════════════

        /// <summary>
        /// 큐 끝에 붙인다. 같은 루틴을 여러 번 넣는 것을 **허용한다** —
        /// `fileName` 이 중복돼도 `QueueItem` 은 별개이고, "집기 3회 → 적재 → 집기 2회"
        /// 같은 순서가 실제로 필요하다.
        /// </summary>
        public QueueItem Add(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            if (items == null) items = new List<QueueItem>();
            var it = new QueueItem(fileName);
            items.Add(it);
            Touch();
            return it;
        }

        /// <summary>목록에서 뺀다. **루틴 파일 자체는 지우지 않는다.**</summary>
        public void Remove(int i)
        {
            if (!InRange(i)) return;
            items.RemoveAt(i);
            Touch();
        }

        public void MoveUp(int i)
        {
            if (!InRange(i) || i == 0) return;
            var t = items[i]; items[i] = items[i - 1]; items[i - 1] = t;
            Touch();
        }

        public void MoveDown(int i)
        {
            if (!InRange(i) || i >= items.Count - 1) return;
            var t = items[i]; items[i] = items[i + 1]; items[i + 1] = t;
            Touch();
        }

        /// <summary>반복 횟수. 0 이하로 내려가지 않게 잡아 둔다 — 0 이면 안 도는 것이나 마찬가지인데,
        /// 그건 `enabled` 를 끄는 쪽이 뜻이 분명하다.</summary>
        public void SetRepeat(int i, int count)
        {
            if (!InRange(i)) return;
            items[i].repeatCount = Math.Max(1, count);
            Touch();
        }

        public void ToggleEnabled(int i)
        {
            if (!InRange(i)) return;
            items[i].enabled = !items[i].enabled;
            Touch();
        }

        // ════════════════════════════════════════
        //                실행 상태
        // ════════════════════════════════════════

        /// <summary>
        /// 모든 항목을 대기로 되돌린다.
        ///
        /// 큐를 시작할 때와 파일에서 불러올 때 부른다. 불러올 때도 지우는 이유는,
        /// Unity Play 가 끝났다 다시 시작되면 팔이 실제로 어느 자세인지 알 수 없기
        /// 때문이다. 저장된 `running` 을 믿고 중간부터 이어 돌면 이전 항목이
        /// 만들어 놨어야 할 자세 없이 다음 동작이 나간다. 큐는 언제나 처음부터 돈다.
        /// </summary>
        public void ResetRunState()
        {
            currentIndex = -1;
            if (items == null) return;
            foreach (var it in items) it?.ResetRunState();
        }
    }
}
