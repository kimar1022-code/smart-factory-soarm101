# 작업 큐 (Task Queue)

> 상태: **명세만 있음. 구현 없음.** (2026-08-03 작성)
> 요구사항 ID: `UR_13` / `SR_21` (`docs/v2/USER_REQUIREMENT.md`), `FR-42`~`FR-48` (`docs/REQUIREMENTS.md`)

---

## 1. 왜 필요한가

지금 재생은 루틴 하나가 끝이다. `RecordManager.StartPlayback()` 은 `CurrentProject`
한 개의 스텝을 끝까지 돌고 `isPlaying = false` 로 끝난다.

그래서 "박스 집기 → 전달 → 적재 → 홈 복귀" 를 이어서 돌리려면 사람이 매번
다음 루틴을 불러오고 재생 버튼을 눌러야 한다. 무인 연속 운전이 안 된다.

작업 큐는 **저장된 루틴 여러 개를 줄 세워 연속 실행하는 계층**이다.

---

## 2. 작업 1개의 정의

**작업(Job) 1개 = `Recordings/*.json` 루틴 파일 1개.**

스텝(`Waypoint`)이 아니다. 스텝 단위의 순서·반복 관리는 `SR_08`/`SR_09` 가 이미 한다.
큐는 그 위에 얹히는 계층이고, 아래 계층을 다시 만들지 않는다.

```
작업 큐          ← 신규. 루틴 여러 개를 순서대로
 └ 루틴          ← RecordProject (기존)
    └ 스텝       ← Waypoint (기존)
```

---

## 3. 자료구조 (신규)

### `QueueItem.cs`

| 필드 | 타입 | 설명 |
|---|---|---|
| `fileName` | `string` | `Recordings/` 안의 루틴 파일명. 큐가 참조하는 유일한 키 |
| `displayName` | `string` | 목록에 보일 이름. 비면 `fileName` 에서 확장자를 뗀 값 |
| `repeatCount` | `int` | 이 항목을 몇 번 반복할지. 기본 1 |
| `enabled` | `bool` | 꺼두면 실행 시 건너뛴다. 지우지 않고 잠깐 빼둘 때 쓴다 |
| `state` | `string` | `pending` / `running` / `done` / `failed` / `skipped` |
| `lastError` | `string` | `failed` 일 때 사유. 성공하면 비운다 |
| `startedAt` | `string` | ISO 8601. 실행 이력 확인용 |
| `finishedAt` | `string` | ISO 8601 |

### `TaskQueue.cs`

| 필드 | 타입 | 설명 |
|---|---|---|
| `queueName` | `string` | 큐 이름 |
| `createdAt` / `lastModifiedAt` | `string` | ISO 8601. `RecordProject` 와 같은 규칙 |
| `version` | `string` | 데이터 버전. 마이그레이션용 |
| `items` | `List<QueueItem>` | 실행 순서대로 |
| `currentIndex` | `int` | 실행 중인 항목. 비실행 시 `-1` |
| `loopQueue` | `bool` | 큐 전체를 무한 반복할지 |
| `stopOnError` | `bool` | 한 항목이 실패하면 큐를 멈출지. 기본 `true` |

`RecordProject` 와 같은 방식으로 `[Serializable]` + `JsonUtility` 를 쓴다.
새 직렬화 라이브러리를 들이지 않는다.

### 저장 위치 — `Recordings/Queues/*.json`

루틴과 **같은 폴더에 두면 안 된다.** `RecordManager.ListSavedFiles()` 는

```csharp
Directory.GetFiles(RecordingsFolder, "*.json")
```

로 `Recordings/` 의 `.json` 을 **전부 루틴으로 간주**한다. 큐 파일을 옆에 두면
"루틴 불러오기" 목록에 큐가 섞여 나오고, 고르면 `waypoints` 가 없어 빈 루틴이 열린다.

`GetFiles` 는 기본값이 비재귀이므로 하위 폴더 `Queues/` 로 내리면 기존 코드를
건드리지 않고 분리된다.

---

## 4. 화면

관제 화면(`ControlTowerCanvas`)에 패널로 붙인다. 별도 창을 새로 만들지 않는다.

```
작업 큐
─────────────────────────
▶ 1. 박스집기_R1      3회  ●진행중
  2. 전달_both       1회  ○대기
  3. 적재_R2         5회  ○대기
  4. 홈복귀          1회  ⊘꺼짐
─────────────────────────
[시작] [일시정지] [건너뛰기] [중단]
현재: 3/12 스텝 · 남은 2건
```

| 조작 | 동작 |
|---|---|
| 항목 추가 | `ListSavedFiles()` 결과에서 루틴을 골라 큐 끝에 붙인다 |
| 항목 삭제 | 목록에서 뺀다. 루틴 파일 자체는 지우지 않는다 |
| 순서 변경 | 위/아래. `RecordManager.MoveStepUp/Down` 과 같은 조작감 |
| 반복 횟수 | 항목별 `repeatCount` |
| 켜기/끄기 | `enabled`. 끈 항목은 `skipped` 로 지나간다 |
| 진행 표시 | 실행 중 항목을 하이라이트. `현재 n/m 스텝` 은 `RecordManager.currentStepIndex` 를 읽어 표시 |

같은 루틴을 큐에 여러 번 넣는 것을 허용한다. `fileName` 이 중복돼도 `QueueItem` 은 별개다.

---

## 5. 실행 규칙

```
IDLE ──시작──▶ RUNNING ──마지막 항목 완료──▶ COMPLETE
                 │  ▲
        일시정지 │  │ 재개
                 ▼  │
              PAUSED
                 │
              중단 ▼
              ABORTED ──▶ IDLE
```

한 항목의 실행:

```
LoadProject(fileName)
  → StartPlayback()
  → IsPlaying == false 가 될 때까지 대기      ← 고정 시간 대기 금지
  → repeatCount 만큼 반복
  → 다음 항목
```

**다음 항목으로 넘어가기 전에 반드시 이전 재생의 실제 완료를 확인한다.**
2026-08-02 에 고친 "루틴 재생이 스텝을 건너뛰던 문제" 와 정확히 같은 부류다.
그때는 목표만 던지고 고정 0.5초를 기다려서 팔이 도착하기 전에 다음 목표가 덮어썼다.
큐에서 이 실수를 반복하면 이전 루틴이 도는 중에 `LoadProject` 가 `CurrentProject` 를
갈아치운다.

| 버튼 | 동작 |
|---|---|
| 시작 | `currentIndex = 0` 부터 실행 |
| 일시정지 | **현재 루틴은 끝까지 재생하고 다음 항목 앞에서 멈춘다** (아래 참조) |
| 건너뛰기 | 현재 항목 `StopPlayback()` → `skipped` 표시 → 다음 항목 |
| 중단 | `StopPlayback()` → 큐 `IDLE`. 팔은 현재 자세를 유지한다 |

### 일시정지를 "항목 경계"로 정의하는 이유

`RecordManager` 에 일시정지가 없다. `StopPlayback()` 은 코루틴을 죽이고
`currentStepIndex` 를 `-1` 로 되돌린다. 재개 지점이 남지 않으므로 스텝 중간에서
멈췄다가 이어서 재생할 수 없다.

스텝 중간 일시정지를 지원하려면 `PlaybackRoutine()` 을 뜯어 재개 가능한 형태로
바꿔야 하는데, 이건 큐와 별개의 변경이다. 큐 명세에서는 **항목 경계 일시정지**만
정의하고, 스텝 중간 일시정지는 미결(O-2)로 둔다.

---

## 6. 안전 규칙

1. **큐 실행 중에는 관절 슬라이더 입력을 막는다.**
   안 막으면 재생 목표와 사람 입력이 서로를 덮어쓴다. 2026-08-02 의 J2/J3 문제
   (Unity 가 관절 목표를 33ms 만에 덮어쓴 건)와 같은 구조다.

2. **항목 사이에 홈 복귀를 강제하지 않는다.**
   강제하면 "집어서 전달" 처럼 중간 자세를 유지해야 하는 작업이 깨진다.
   홈으로 보내고 싶으면 사용자가 홈 복귀 루틴을 큐 항목으로 넣는다.

3. **소켓이 끊기면 `stopOnError` 와 무관하게 즉시 큐를 중단한다.**
   통신이 없으면 다음 항목도 실패한다. 실패 항목을 쌓을 이유가 없다.

4. **비상 정지는 큐보다 위다.**
   ✅ 2026-08-03 에 정지 경로를 고쳤다 (`USER_REQUIREMENT` 7-2). 특히 `RecordManager`
   에 비상정지 검사가 없어 **정지를 걸어도 루틴 재생이 계속 돌던** 문제를 잡았다.
   큐는 그 위에 얹히므로, 큐 루프도 같은 방식으로 `EmergencyStopped` 를 봐야 한다.

   ⚠️ 남은 구멍: 서버에 `{"type":"stop"}` 이 없다. 정지는 관절별 `Goal_Position`
   송신으로 이뤄지므로 **소켓이 끊긴 상태에서는 정지 명령이 나가지 않는다.**
   무인 연속 운전을 실제로 돌리기 전에 막는 편이 좋다.

---

## 7. 서버 프로토콜

**변경 없음.** 큐는 Unity 쪽 오케스트레이션이고, 서버(`robot_server_dual.py`)는
기존 명령만 받는다. 큐 상태를 서버가 알 필요가 없다.

---

## 8. 기존 코드와의 접점

| 기존 | 쓰는 방식 |
|---|---|
| `RecordManager.ListSavedFiles()` | 큐에 넣을 루틴 후보 목록 |
| `RecordManager.LoadProject(fileName)` | 항목 실행 직전 루틴 적재 |
| `RecordManager.StartPlayback()` | 항목 실행 |
| `RecordManager.IsPlaying` | 완료 판정 |
| `RecordManager.StopPlayback()` | 건너뛰기 / 중단 |
| `ControlTowerCanvas` | 큐 패널 배치. `BuildUI` / `WireUp` 분리 규칙을 따른다 |

`RecordManager` 를 고치지 않고 위에 얹는 것을 원칙으로 한다. 유일한 예외는 O-1 이다.

---

## 9. 미결 — 구현 전에 정해야 하는 것

| ID | 내용 |
|---|---|
| **O-1** | **실패 판정 기준.** 현재 `PlaybackRoutine()` 은 실패라는 개념이 없다. 끝까지 돌면 무조건 `"✅ 재생 완료"` 다. 재생이 도달 실패·타임아웃을 알려주지 않으면 큐도 `failed` 를 만들 수 없고, `stopOnError` 가 죽은 옵션이 된다. 재생 쪽에 실패 신호를 추가할지 결정해야 한다 |
| **O-2** | 스텝 중간 일시정지가 필요한가. 필요하면 `PlaybackRoutine()` 을 재개 가능한 형태로 바꾸는 별도 작업이 선행된다 |
| **O-3** | 큐 진행 중 Unity Play 가 끝나면 어떻게 되는가. 다음 실행 때 이어서 돌지, 처음부터 돌지 |
| **O-4** | 로봇별 배분 큐(R1 큐 / R2 큐 분리)로 확장할지. 확장하면 랑데부 지점과 충돌 회피가 필요하고 `SR_19`(협동 작업)와 한 덩어리가 된다 |

---

## 10. 관련 문서

- `docs/v2/USER_REQUIREMENT.md` — `UR_13`, `SR_21`, Scenario #8
- `docs/REQUIREMENTS.md` — `FR-42`~`FR-48`
- `docs/TROUBLESHOOTING_2026-08-02.md` — 6절 안전 규칙의 근거가 된 두 버그
