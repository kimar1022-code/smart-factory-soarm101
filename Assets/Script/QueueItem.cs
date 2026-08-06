using System;

namespace SOArmControl
{
    /// <summary>
    /// 작업 큐의 항목 하나. **작업 1개 = `Recordings/*.json` 루틴 파일 1개**다.
    ///
    /// 스텝(<see cref="Waypoint"/>)이 아니다. 스텝 단위의 순서·반복은 `RecordManager`
    /// 가 이미 하고 있고, 큐는 그 위에 얹히는 계층이다. 아래 계층을 다시 만들지 않는다.
    ///
    ///   작업 큐   ← 이 파일
    ///    └ 루틴   ← RecordProject
    ///       └ 스텝 ← Waypoint
    ///
    /// 명세: `docs/TASK_QUEUE.md` 3절, 요구사항 `FR-43` / `FR-44`.
    /// </summary>
    [Serializable]
    public class QueueItem
    {
        // ── 저장되는 것 ────────────────────────────────────────
        //
        // 여기까지가 "무엇을 어떤 순서로 돌릴지" 다. 큐 파일에 남는다.

        /// <summary>`Recordings/` 안의 루틴 파일명. 큐가 참조하는 유일한 키다.</summary>
        public string fileName;

        /// <summary>목록에 보일 이름. 비면 <see cref="fileName"/> 에서 확장자를 뗀 값을 쓴다.</summary>
        public string displayName;

        /// <summary>이 항목을 몇 번 반복할지. 기본 1.</summary>
        public int repeatCount = 1;

        /// <summary>꺼두면 실행 시 건너뛴다. 지우지 않고 잠깐 빼둘 때 쓴다.</summary>
        public bool enabled = true;

        // ── 실행 중에만 의미가 있는 것 ─────────────────────────
        //
        // ⚠️ 아래 네 칸은 저장은 되지만 **불러올 때 전부 초기화된다**
        //    (`TaskQueue.ResetRunState`). Unity Play 가 끝나면 팔이 실제로 어느
        //    자세인지 알 수 없어, 중간 항목부터 이어 도는 것이 위험하기 때문이다.
        //    큐는 언제나 처음부터 돈다 (명세 미결 O-3 의 결정).

        /// <summary>`pending` / `running` / `done` / `failed` / `skipped`</summary>
        public string state = StatePending;

        /// <summary><see cref="StateFailed"/> 일 때의 사유. 성공하면 비운다.</summary>
        public string lastError;

        /// <summary>ISO 8601. 실행 이력 확인용.</summary>
        public string startedAt;

        /// <summary>ISO 8601.</summary>
        public string finishedAt;

        // 상태 문자열을 코드 여기저기에 직접 적으면 오타가 조용히 통과한다.
        public const string StatePending = "pending";
        public const string StateRunning = "running";
        public const string StateDone    = "done";
        public const string StateFailed  = "failed";
        public const string StateSkipped = "skipped";

        public QueueItem() { }

        public QueueItem(string file)
        {
            fileName = file != null && file.EndsWith(".json") ? file : file + ".json";
            displayName = "";
        }

        /// <summary>목록에 찍을 이름. `displayName` 이 비어 있으면 파일명에서 뽑는다.</summary>
        public string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
                if (string.IsNullOrEmpty(fileName)) return "(이름 없음)";
                return fileName.EndsWith(".json")
                    ? fileName.Substring(0, fileName.Length - 5)
                    : fileName;
            }
        }

        public bool IsPending => state == StatePending;
        public bool IsRunning => state == StateRunning;
        public bool IsDone    => state == StateDone;
        public bool IsFailed  => state == StateFailed;
        public bool IsSkipped => state == StateSkipped;

        /// <summary>실행 흔적을 지우고 대기 상태로 되돌린다.</summary>
        public void ResetRunState()
        {
            state = StatePending;
            lastError = "";
            startedAt = "";
            finishedAt = "";
        }

        public void MarkRunning()
        {
            state = StateRunning;
            lastError = "";
            startedAt = DateTime.Now.ToString("o");
            finishedAt = "";
        }

        public void MarkDone()
        {
            state = StateDone;
            lastError = "";
            finishedAt = DateTime.Now.ToString("o");
        }

        public void MarkFailed(string reason)
        {
            state = StateFailed;
            lastError = reason ?? "";
            finishedAt = DateTime.Now.ToString("o");
        }

        public void MarkSkipped()
        {
            state = StateSkipped;
            finishedAt = DateTime.Now.ToString("o");
        }

        /// <summary>목록 왼쪽에 찍는 상태 표식. 명세 4절의 화면 그림과 같은 기호를 쓴다.</summary>
        public string StateMark
        {
            get
            {
                if (!enabled) return "⊘";
                switch (state)
                {
                    case StateRunning: return "●";
                    case StateDone:    return "✔";
                    case StateFailed:  return "✕";
                    case StateSkipped: return "⊘";
                    default:           return "○";
                }
            }
        }

        public string StateText
        {
            get
            {
                if (!enabled) return "꺼짐";
                switch (state)
                {
                    case StateRunning: return "진행중";
                    case StateDone:    return "완료";
                    case StateFailed:  return "실패";
                    case StateSkipped: return "건너뜀";
                    default:           return "대기";
                }
            }
        }
    }
}
