# 사용자 요구사항 명세서 (SRS) — SO-ARM101 스마트팩토리

> **작성일: 2026-08-01**
> 이 문서는 **"이 시스템이 무엇을 해야 하는가"** 를 ID를 붙여 정리하고, 각 항목이 지금 실제로 어디까지 구현됐는지 코드로 확인해 표시한 문서다.

---

## 0. 이 문서를 읽는 법

### 0.1 구현 상태 표기

| 기호 | 뜻 | 판정 기준 |
|:---:|---|---|
| ✅ | 완료 | 해당 기능의 코드가 존재하고, 코드 경로가 끊김 없이 이어짐을 확인함 |
| 🔵 | 진행중 | 코드는 있으나 잠겨 있거나, 한쪽(Unity 또는 서버)만 확인됨 |
| ⬜ | 미착수 | 코드가 없음 |
| 🔴 | 결함 | 구현은 됐으나 의도와 다르게 동작하는 지점을 코드에서 발견함 |

### 0.2 근거 표기 원칙

이 문서의 모든 수치·동작은 **실제 파일에서 읽은 것만** 적었다.
확인하지 못한 것은 **⚠️ 미확인** 으로 명시했고, 추측으로 채우지 않았다.

주요 근거 파일:

| 약칭 | 실제 경로 |
|---|---|
| `URDF` | `F:\UNITY\LeRobot\Assets\SO101_unity\so101.urdf` |
| `SCENE` | `F:\UNITY\LeRobot\Assets\Scenes\LeRobot.unity` |
| `Script/*` | `F:\UNITY\LeRobot\Assets\Script\*.cs` |
| `Editor/*` | `F:\UNITY\LeRobot\Assets\Editor\*.cs` |
| `PINCOPEN_INTEGRATION` | `F:\UNITY\LeRobot\docs\PINCOPEN_INTEGRATION.md` |
| `PROJECT_NOTES.md` | `F:\UNITY\LeRobot\PROJECT_NOTES.md` |
| `HANDOFF` | `C:\Users\snbco\Desktop\HANDOFF.md` |

---

## 1. 시스템 목적

### 1.1 한 문장으로

> **컴퓨터 화면 속 로봇팔(시뮬레이션)과 책상 위 진짜 로봇팔이 똑같이 움직이게 만들고,
> 그 움직임을 녹화·재생할 수 있게 하는 시스템.**

### 1.2 조금 더 정확하게

Hugging Face **LeRobot** 프로젝트 기반의 **SO-ARM101 6축 로봇팔 2대**를,
Unity로 만든 **디지털 트윈(Digital Twin)** 환경에서 시각화하고 실시간 동기화 제어한다.
(출처: `PROJECT_NOTES.md` 프로젝트 개요)

**디지털 트윈**이란, 실물 장비와 똑같이 생기고 똑같이 움직이는 "쌍둥이"를 컴퓨터 안에 만들어 두고
둘을 계속 붙여놓는 기술이다. 실물을 만지지 않고도 미리 시험해 볼 수 있고,
반대로 실물이 지금 어떤 자세인지 화면으로 볼 수 있다.

### 1.3 목표 시스템 (To-Be)

```
사람이 Unity 화면에서 슬라이더를 움직인다
        ↓  (실시간)
화면 속 로봇 + 책상 위 진짜 로봇이 동시에 같은 자세가 된다
        ↓  (녹화)
동작을 스텝 단위로 저장했다가 반복 재생한다  =  간이 티칭펜던트
```

### 1.4 현재 달성도

`HANDOFF` 기준 자체 추정 **약 40%** 였고, 그 이후 이 저장소에서 다음이 추가로 확인된다.

- 양방향 통신 코드 (`SOArmSocketClient` v3, `SOArmRealController` 폴링) — Unity 측 ✅
- Record/Play 기능 (`RecordManager` 외 3파일) ✅
- PincOpen 그리퍼 통합 (URDF + 커플링 + 안전장치) ✅

---

## 2. 이해관계자 (Stakeholder)

| ID | 이해관계자 | 이 시스템에 바라는 것 | 관련 요구사항 |
|---|---|---|---|
| ST-01 | **개발자 본인** (시스템 엔지니어 지망) | 펌웨어~애플리케이션 전 계층을 직접 만져보고 포트폴리오로 남긴다 | 전 항목 |
| ST-02 | **로봇 조작자** (교육/시연 시 사용자) | 코드를 몰라도 슬라이더와 버튼만으로 로봇을 안전하게 움직인다 | FR-01~FR-20, SR-* |
| ST-03 | **실물 하드웨어** (STS3215 모터 12개) | 자기 기계적 한계를 넘는 명령을 받지 않는다 | SR-03~SR-08 |
| ST-04 | **LeRobot 커뮤니티 / 오픈소스** | 라이선스(CC BY-SA 4.0) 준수, 출처 표기 | NFR-14 |
| ST-05 | **향후 협업자 / 채용 평가자** | 문서만 읽고도 구조를 파악하고 이어받을 수 있다 | NFR-13, 본 문서 |

---

## 3. 사용 시나리오 (Use Case)

### UC-01. 시뮬레이션 단독 조작 — "로봇 없이 연습하기"

| 항목 | 내용 |
|---|---|
| 행위자 | ST-02 조작자 |
| 사전조건 | Unity 프로젝트 실행. 라즈베리파이 불필요 |
| 흐름 | ① Play → ② 좌측 Robot 1 패널의 J1~J6 슬라이더 조작 → ③ 화면 속 로봇이 움직임 |
| 근거 | `SOArmManager.Mode.SimOnly`, `SOArmSimController` — 실로봇 없이 동작하도록 설계됨 (클래스 주석) |
| 상태 | ✅ |

### UC-02. 실로봇 원격 조작 — "화면에서 진짜 팔 움직이기"

| 항목 | 내용 |
|---|---|
| 행위자 | ST-02 조작자 |
| 사전조건 | 라즈베리파이에서 `robot_server_dual.py` 실행 중, 12V 전원 ON |
| 흐름 | ① 「🔌 재연결」 → ② 슬라이더 조작 → ③ 10 Hz로 JSON 명령 전송 → ④ 실물 모터 회전 |
| 근거 | `SOArmRealController.Update()` 의 전송 루프, `SOArmSocketClient.SendMotorCommand` |
| 상태 | ✅ (`HANDOFF` §7 "작동" 항목에서 실물 검증 기록됨) |

### UC-03. 디지털 트윈 동기화 — "진짜 팔 자세를 화면에 그대로"

| 항목 | 내용 |
|---|---|
| 행위자 | ST-01 개발자 |
| 흐름 | ① 30 Hz 폴링으로 `{"type":"get"}` 전송 → ② 서버가 엔코더 값 응답 → ③ 정규화값→각도 변환 → ④ 시뮬 관절에 반영 |
| 근거 | `SOArmRealController.RequestAnglesOnce` → `OnAnglesReceived` → `SOArmManager.HandleRealAngles` |
| 상태 | 🔵 Unity 측 코드 ✅ / 서버 측 `get` 핸들러 **⚠️ 미확인** (`robot_server_dual.py` 가 이 저장소에 없음) |

### UC-04. 두 팔 동시 제어 — "미러 모드"

| 항목 | 내용 |
|---|---|
| 흐름 | ① 상단 「미러」 버튼 → ② 한쪽 슬라이더 조작 → ③ 두 로봇이 같은 각도로 움직이고 반대편 슬라이더도 따라 움직임 |
| 근거 | `SOArmDualManager.ControlMode.Mirror` → `SetJointBoth()`, UI 쪽 `r2Sliders[i] = newVal` 동기화 |
| 상태 | ✅ |

### UC-05. 동작 녹화 및 재생 — "간이 티칭"

| 항목 | 내용 |
|---|---|
| 흐름 | ① 「🎬 Record」 토글 ON → ② 자세 잡고 「+ R1 / + R2 / + 둘 다」로 스텝 추가 → ③ 「+ ⏱」 대기, 「+ 🔁」 반복 삽입 → ④ 「💾 저장」 (`Recordings/*.json`) → ⑤ 「▶ 재생」 |
| 근거 | `SmartFactoryRecordUI`, `RecordManager.PlaybackRoutine()`, `RecordProject`, `Waypoint` |
| 상태 | ✅ (`F:\UNITY\LeRobot\Recordings\Untitled.json` 존재 확인) |

### UC-06. 홈 포즈 재지정 — "지금 자세를 0°로 삼기"

| 항목 | 내용 |
|---|---|
| 흐름 | ① 「⚙️ 현재 자세를 홈으로 지정」 → ② 확인 다이얼로그 → ③ `{"type":"set_home"}` 전송 → ④ 슬라이더/홈포즈 캐시 0으로 리셋 |
| 근거 | `SmartFactoryUI_v3_4.DrawSetHomeDialog()`, `SOArmRealController.SaveHomePose()` |
| 상태 | 🔵 Unity 측 ✅ / 서버 측 백업·autocorrect 동작 **⚠️ 미확인** (다이얼로그 문구에만 존재) |

### UC-07. PincOpen 그리퍼 개폐

| 항목 | 내용 |
|---|---|
| 흐름 | ① 「🤏 닫기 / 🖐 반 / 🖐 열기」 → ② 구동축 1개 각도 결정 → ③ 종동축 3개가 배율대로 따라 움직임 |
| 근거 | `PincOpenCoupling.SetGripperPercent()` → `ApplyCoupling()` |
| 상태 | ✅ 시뮬 / ⬜ 실물 (SR-03 안전 게이트로 차단 중) |

### UC-08. PincOpen 하드웨어 검증 (개발자 전용)

| 항목 | 내용 |
|---|---|
| 흐름 | `Tools ▸ SO-ARM ▸ 그리퍼 구동 자체검증` / `대칭 검증` / `장착 상태 캡처` 실행 |
| 근거 | `Editor/PincOpenCapture.cs` (`SelfTest`, `SymmetryTest`, `CaptureAll`, `GravityTest`, `CompareRatios`, `EndToEndGripperTest`) |
| 상태 | ✅ |

---

## 4. 기능 요구사항 (Functional Requirements)

### 4.1 시뮬레이션 제어

| ID | 요구사항 | 상태 | 구현 근거 |
|---|---|:---:|---|
| FR-01 | 6개 관절 각도를 슬라이더/버튼(−, 0°, +)으로 개별 지정할 수 있어야 한다 | ✅ | `SmartFactoryUI_v3_4.DrawRobotPanel()` |
| FR-02 | 스텝 각도를 0.5° / 1° / 5° / 10° 또는 직접 입력으로 바꿀 수 있어야 한다 | ✅ | `SmartFactoryUI_v3_4.DrawTopBar()` |
| FR-03 | 지정한 각도를 Unity `ArticulationBody.xDrive.target` 에 반영해야 한다 | ✅ | `SOArmSimController.ApplyToArticulationBodies()` |
| FR-04 | 관절별 최소/최대 각도를 벗어나는 값은 잘라내야 한다 | ✅ | `Mathf.Clamp` — `SOArmSimController.SetJointTarget()` |
| FR-05 | 관절별 부호 반전(`invertSign`)과 기준점 보정(`angleOffset`)을 지원해야 한다 | ✅ | `SOArmJointConfig`, `SOArmSimController.ApplyToArticulationBodies()` |
| FR-06 | 실로봇 없이 시뮬레이션만으로 동작할 수 있어야 한다 | ✅ | `SOArmManager.Mode.SimOnly` |

### 4.2 실로봇 통신

| ID | 요구사항 | 상태 | 구현 근거 |
|---|---|:---:|---|
| FR-07 | TCP 소켓으로 라즈베리파이 서버에 접속/해제/재접속할 수 있어야 한다 | ✅ | `SOArmSocketClient.Connect/Disconnect` |
| FR-08 | 관절 각도를 −100~100 정규화 값으로 변환해 전송해야 한다 | ✅ | `SOArmMotorMapper.AngleToServerValue()` |
| FR-09 | 명령은 한 줄 JSON(개행 구분)으로 보내야 한다 | ✅ | `SOArmSocketClient.SendMotorCommand()` |
| FR-10 | 서버 응답을 읽어 요청한 쪽 콜백으로 되돌려줘야 한다 | ✅ | `ReceiveLoop()` + `pendingCallbacks` FIFO 큐 |
| FR-11 | 실로봇의 현재 각도를 주기적으로 읽어야 한다 | 🔵 | `SOArmRealController` `pollHz=30`, `pollEnabled=true` (`SCENE` 확인). 서버 응답부 **⚠️ 미확인** |
| FR-12 | 읽은 각도를 시뮬 관절에 반영해야 한다 (디지털 트윈) | 🔵 | `SOArmManager.HandleRealAngles()`, `realToSimSync=1` (`SCENE`) |
| FR-13 | Play 시작 후 1회 실로봇 자세를 읽어 시뮬 초기 자세를 맞춰야 한다 | ✅ | `SOArmManager.InitialSyncCoroutine()`, `syncOnStart=1` (`SCENE`) |
| FR-14 | 현재 자세를 새 0점(홈)으로 서버에 저장할 수 있어야 한다 | 🔵 | `RequestSetHome()` / `SaveHomePose()` |
| FR-15 | 홈 자세로 이동 명령을 보낼 수 있어야 한다 | ✅ | `SmartFactoryUI_v3_4.SendGoHome()` → `{"type":"home"}` |
| FR-16 | 모터 속도/가속도를 UI에서 바꿔 서버에 적용할 수 있어야 한다 | ✅ | `ApplySpeedToServer()` → `{"type":"set_speed"}` |
| FR-17 | 서보 토크를 ON/OFF 할 수 있어야 한다 | 🔵 | `RequestTorque()` / `SetServoTorque()` API 존재. **UI 노출 없음** (호출처 미발견) |

### 4.3 2대 통합 제어

| ID | 요구사항 | 상태 | 구현 근거 |
|---|---|:---:|---|
| FR-18 | 두 로봇을 독립(Independent) 제어할 수 있어야 한다 | ✅ | `SOArmDualManager.RouteJointCommand()` |
| FR-19 | 두 로봇을 미러(Mirror) 제어할 수 있어야 한다 | ✅ | `SetJointBoth()` / `SetGripperBoth()` |
| FR-20 | 로봇별로 사용 여부(채널 on/off)를 켜고 끌 수 있어야 한다 | ✅ | `robot1Enabled` / `robot2Enabled` |
| FR-21 | 전체 홈 이동 / 전체 정지 / 전체 재연결 버튼이 있어야 한다 | ✅ | 전체 정지(`StopAll`)는 채널 on/off 와 무관하게 항상 두 로봇 모두 세운다 — 꺼둔 채널이라고 안 세우면 그 로봇이 이미 움직이는 중일 때 못 멈춘다 |
| FR-22 | 두 로봇 협동 작업 시퀀스 (Cooperative) | ⬜ | 현재 `ControlMode` 는 `Independent`/`Mirror` 2개뿐. `PROJECT_NOTES.md` 의 5개 모드 기술은 **구버전** |

### 4.4 동작 녹화·재생 (Record / Play)

| ID | 요구사항 | 상태 | 구현 근거 |
|---|---|:---:|---|
| FR-23 | 현재 자세를 스텝으로 저장할 수 있어야 한다 (robot1 / robot2 / both) | ✅ | `RecordManager.AddMotionStepFromUI()` |
| FR-24 | 대기(wait) 스텝을 넣을 수 있어야 한다 | ✅ | `AddWaitStep()` |
| FR-25 | 반복(loop_start / loop_end) 구간을 넣을 수 있어야 한다 | ✅ | `AddLoopStartStep()`, `AddLoopEndStep()` |
| FR-26 | 스텝 순서 변경 / 이름 변경 / 삭제가 가능해야 한다 | ✅ | `MoveStepUp/Down`, `RenameStep`, `RemoveStep` |
| FR-27 | 프로젝트를 JSON 파일로 저장·불러오기 할 수 있어야 한다 | ✅ | `SaveProject()`/`LoadProject()` → `<프로젝트루트>/Recordings/*.json` |
| FR-28 | 저장된 시퀀스를 순차 재생하고, 중간에 정지할 수 있어야 한다 | ✅ | `PlaybackRoutine()` — 반복은 `Stack<(index, remaining)>` 으로 처리 |
| FR-29 | 재생 중인 스텝을 UI에서 하이라이트해야 한다 | ✅ | `SmartFactoryRecordUI.DrawStepRow()` |
| FR-30 | Record 모드는 제어 모드(독립/미러)와 무관하게 켤 수 있어야 한다 | ✅ | `isRecordModeActive` 를 `ControlMode` 와 분리 |

### 4.5 PincOpen 그리퍼

| ID | 요구사항 | 상태 | 구현 근거 |
|---|---|:---:|---|
| FR-31 | 평행 4절 링크의 종동축 3개가 구동축을 따라 움직여야 한다 | ✅ | `PincOpenCoupling.ApplyCoupling()` — 배율 ×1.0, 부호 (−1, −1, +1) |
| FR-32 | 그리퍼를 0~100 % 로 여닫을 수 있어야 한다 | ✅ | `SetGripperPercent()` — 0 %=닫힘, 100 %=열림 |
| FR-33 | URDF 임포트 시 비어 있는 드라이브 파라미터를 자동으로 채워야 한다 | ✅ | `ConfigureDrives()` (`stiffness=10000`) |
| FR-34 | 순정 그리퍼를 PincOpen 으로 교체하는 작업을 자동화해야 한다 | ✅ | `Editor/PincOpenMainSceneMigrator.cs` — 재실행해도 안전 |
| FR-35 | 그리퍼 장착 정확도를 수치로 검증할 수 있어야 한다 | ✅ | `Editor/PincOpenCapture.cs` — 자체검증/대칭/중력/렌더 비교 |
| FR-36 | 실물 PincOpen 을 명령할 수 있어야 한다 | ⬜ | **SR-03 에 의해 의도적으로 잠금** |

### 4.6 카티시안 좌표 제어 (역기구학)

| ID | 요구사항 | 상태 | 구현 근거 |
|---|---|:---:|---|
| FR-38 | 역기구학(IK)으로 XYZ 좌표 직접 제어 | ✅ | 서버 `handle_ik()` (placo) + `Script/SOArmIKController.cs` + `ControlTowerCanvas.CartFace()` |

`HANDOFF` §9 는 DLS(Damped Least Squares) 이식을 검토 중이라고 적고 있으나, 실제로는
LeRobot 본체(`lerobot/model/kinematics.py`)의 placo 솔버를 서버에서 쓰는 쪽으로 정해졌다.
이 하드웨어에 맞춰 유지보수되는 구현을 쓰는 편이 낫다고 판단했다.

서버의 `ik` 명령은 **계산만** 한다. 나온 관절 각도를 적용하는 것은 Unity 가
`SOArmManager` 를 거쳐서 하므로 SR-06(가동범위 제한)·SR-07(소프트 리밋 클램프)·
비상정지가 그대로 걸린다. 서버가 직접 모터를 돌리면 그 방어선을 우회하게 된다.

5축(J1~J5, J6 은 그리퍼)이라 임의의 6D 자세는 만들 수 없다. J2·J3·J4 가 서로 평행한
pitch 축이어서 공구의 yaw 가 J1 에 묶인다. 자세 가중치를 0.01 로 낮춰 위치를 맞추고
자세는 근사한다. 회전 요청으로 TCP 가 5mm 넘게 밀리면 서버가 결과를 버린다
(`IK_ROT_MAX_DRIFT_MM`).

기구학 전용 URDF `so101_kin.urdf` 와 그 생성기 `make_kin_urdf.py` 는 `raspberry_pi/ik/`
에 있다. 라파의 `/home/sw/ik/` 와 같은 내용이며, 생성 원본은 저장소의
`Assets/SO101_unity/so101.urdf` 다.

### 4.7 미착수 기능 (로드맵)

| ID | 요구사항 | 상태 | 비고 |
|---|---|:---:|---|
| FR-37 | 손목 카메라 영상을 Unity 화면에 표시 | ⬜ | `HANDOFF` §1: 카메라는 물리적으로 부착됨. 개수·연결 위치 **⚠️ 미확인** |
| FR-39 | TCP(공구 중심점)를 PincOpen 손끝으로 이동 | ⬜ | `PINCOPEN_INTEGRATION` §10: 현재 `gripper_frame_link` 는 순정 조 끝 기준. FR-38 이 이 기준점을 그대로 쓴다 |
| FR-40 | 리더 암 2대 추가 (텔레오퍼레이션) | ⬜ | `HANDOFF` §9 |
| FR-41 | AI 비전 기반 pick & place | ⬜ | `HANDOFF` §9 |

### 4.8 작업 큐 (Task Queue)

전부 미착수. 상세 명세는 [`docs/TASK_QUEUE.md`](TASK_QUEUE.md) (2026-08-03).
작업 1개는 `Recordings/*.json` 루틴 1개다. 스텝 단위가 아니다 — 그쪽은 FR-23~FR-30 이 덮는다.

| ID | 요구사항 | 상태 | 비고 |
|---|---|:---:|---|
| FR-42 | 저장된 루틴 여러 개를 큐에 줄 세워 연속 실행할 수 있어야 한다 | ⬜ | `LoadProject` → `StartPlayback` → `IsPlaying` 감시를 항목마다 반복 |
| FR-43 | 큐 항목을 추가 / 삭제 / 순서 변경할 수 있어야 한다 | ⬜ | 같은 루틴을 여러 번 넣는 것을 허용 |
| FR-44 | 항목별 반복 횟수와 사용 여부(on/off)를 지정할 수 있어야 한다 | ⬜ | 끈 항목은 `skipped` 로 지나간다 |
| FR-45 | 다음 항목은 이전 재생이 실제로 끝난 뒤에 시작해야 한다 | ⬜ | 고정 시간 대기 금지. 2026-08-02 "스텝 건너뜀" 과 같은 부류 |
| FR-46 | 큐를 JSON 으로 저장·복원할 수 있어야 한다 | ⬜ | `Recordings/Queues/*.json`. 루틴과 같은 폴더에 두면 `ListSavedFiles()` 가 큐를 루틴으로 읽는다 |
| FR-47 | 실행 중 항목을 하이라이트하고 진행(`n/m` 스텝)을 표시해야 한다 | ⬜ | `RecordManager.currentStepIndex` 를 읽어 표시 |
| FR-48 | 일시정지(항목 경계) / 건너뛰기 / 중단이 가능해야 한다 | ⬜ | `StopPlayback()` 은 재개 지점을 남기지 않아 스텝 중간 일시정지가 불가. 미결 O-2 |

**구현 전 선결 조건 2가지** (`docs/TASK_QUEUE.md` §9)

| | 내용 |
|---|---|
| O-1 | `PlaybackRoutine()` 에 실패라는 개념이 없다. 끝까지 돌면 항상 `"✅ 재생 완료"` 다. 재생이 실패를 알리지 않으면 큐도 `failed` 를 못 만들고 `stopOnError` 가 죽은 옵션이 된다 |
| 안전 | ~~비상 정지가 실효 없는 상태다~~ → **2026-08-03 해소.** SR-10 참조. 다만 서버 `{"type":"stop"}` 이 없어 소켓이 끊기면 정지 명령이 못 나간다. 무인 연속 운전을 실제로 돌리기 전에 이건 마저 막는 편이 좋다 |

---

## 5. 비기능 요구사항 (Non-Functional Requirements)

### 5.1 성능 / 실시간성

| ID | 요구사항 | 값 | 상태 | 근거 |
|---|---|---|:---:|---|
| NFR-01 | 실로봇 명령 전송률에 상한을 둔다 | **10 Hz** | ✅ | `sendRateHz=10` (`SCENE` L1029, L9896). 상한을 두는 이유는 §5.5 참조 |
| NFR-02 | 변화량이 작으면 전송하지 않는다 (대역폭 절약) | **0.5°** | ✅ | `minChangeToSend` — `SOArmRealController.Update()` |
| NFR-03 | 실로봇 각도 폴링 주기 | **30 Hz** | ✅ | `pollHz=30` (`SCENE` L1031, L9898) |
| NFR-04 | 폴링 요청은 동시에 1건만 날린다 (응답 대기 중 재요청 금지) | in-flight 1 | ✅ | `waitingForGetResponse` 플래그 |
| NFR-05 | 소켓 수신이 Unity 메인 스레드를 막지 않아야 한다 | — | ✅ | 백그라운드 `ReceiveLoop()` → `ConcurrentQueue` → `Update()` 에서 콜백 실행 |

### 5.2 신뢰성 / 견고성

| ID | 요구사항 | 상태 | 근거 |
|---|---|:---:|---|
| NFR-06 | 숫자 직렬화는 로케일에 영향받지 않아야 한다 | ✅ | `value.ToString("F2", CultureInfo.InvariantCulture)` — 과거 `{value:F2}` 보간이 문자열 "F2"로 나가 서버 파싱이 깨진 이력 (`HANDOFF` §10) |
| NFR-07 | 연결이 끊기면 대기 중이던 콜백을 실패로 정리해야 한다 | ✅ | `Disconnect()` 내 `{"ok":false,"error":"disconnected"}` 주입 |
| NFR-08 | 수신 데이터는 개행 단위로 잘라 부분 수신을 견뎌야 한다 | ✅ | `lineBuffer` 누적 후 `'\n'` 분할 |
| NFR-09 | 에디터 마이그레이션 도구는 여러 번 실행해도 안전해야 한다 (멱등성) | ✅ | `PincOpenMainSceneMigrator.MigrateOne()` — 기존 `pincopen_adapter_link` 감지 시 이식 생략 |
| NFR-10 | 로봇 교체 시 인스펙터 연결이 끊기면 안 된다 | ✅ | 통째 재임포트 대신 **그리퍼 subtree 만 교체** |

### 5.3 유지보수성 / 구조

| ID | 요구사항 | 상태 | 근거 |
|---|---|:---:|---|
| NFR-11 | 스크립트는 기능별로 분리한다 (모놀리식 금지) | ✅ | `Assets/Script/` 14개 파일 + `Assets/Editor/` 3개 파일, 단일 네임스페이스 `SOArmControl` |
| NFR-12 | 시뮬과 실로봇은 동일한 인터페이스 계약을 따른다 | ✅ | `ISOArmController` — `SOArmSimController` / `SOArmRealController` / `SOArmManager` 3개가 구현 |
| NFR-13 | 기구학 정보의 단일 진실 공급원(SSoT)은 URDF 하나여야 한다 | ✅ | `URDF` L357 주석 + `applyMountOffset=0` (`SCENE` L1171) |
| NFR-14 | 코드 주석·UI·로그는 한국어로 작성한다 | ✅ | 전 파일 확인 |
| NFR-15 | 외부 오픈소스 라이선스를 준수한다 | 🔵 | PincOpen 메시 = **CC BY-SA 4.0** (`URDF` L322, `PINCOPEN_INTEGRATION` §8). GitHub 공개 시 출처 표기 + 동일 라이선스 유지 **필요** |

### 5.4 사용성

| ID | 요구사항 | 상태 | 근거 |
|---|---|:---:|---|
| NFR-16 | 코드를 모르는 사람도 GUI만으로 조작 가능해야 한다 | ✅ | `OnGUI` 기반 슬라이더 + 버튼 UI |
| NFR-17 | 실로봇의 실제 각도를 슬라이더 옆에 함께 표시해야 한다 | ✅ | `DrawRobotPanel()` 의 `[실:○○°]` 라벨 (`LastReadAngles`) |
| NFR-18 | 위험한 조작에는 확인 절차를 둔다 | ✅ | 홈포즈 지정 확인 다이얼로그 (SR-06) |
| NFR-19 | 화면 크기에 따라 패널 높이가 자동 조정돼야 한다 | ✅ | `CalculateSizes()` |

### 5.5 통신 설계 근거 — 왜 10 Hz 인가

`HANDOFF` §10 트러블슈팅에 기록된 **실제 장애 이력**이 근거다.

> `JSON 파싱 오류 (value: F2, Extra data)` → 원인: C# 보간 `{value:F2}` 문제 **+ 전송 과속** → 해결: `InvariantCulture` 지정 **+ 전송 10 Hz 제한**

즉 10 Hz는 성능 목표가 아니라 **서버 파서 보호를 위한 상한**이다.
서버가 개행 단위로 JSON을 자르는데, 60 Hz(매 프레임)로 6개 관절 × 2대 = 초당 720줄을 밀어 넣으면
TCP 세그먼트 경계에서 줄이 깨지고 `Extra data` 예외가 발생했다.

관절 6개 × 10 Hz × 2대 = **최대 초당 120 줄**, 여기에 NFR-02(0.5° 미만 미전송) 필터가 더해져
실사용에서는 훨씬 적다.

---

## 6. 안전 요구사항 (Safety Requirements)

> ⚠️ 이 절은 **실물 하드웨어 파손과 직결**된다. 다른 절보다 우선한다.

### 6.1 근본 위험 요인

| 위험 | 설명 | 근거 |
|---|---|---|
| **STS3215는 위치 제어 모드에서 토크 제한이 없다** | 물체를 물면 계속 힘을 준다 → 모터가 타거나 3D 프린팅 플라스틱이 부러진다 | `PincOpenSafety.cs` 클래스 주석 (PincOpen 저장소 원문 경고 인용) |
| **캘리브레이션 불일치** | 순정 그리퍼(−10°~100°) 기준 캘리브레이션이 남은 채 정규화값 −100을 보내면 PincOpen 기계적 한계를 넘는다 | `PincOpenSafety.RealGripperEnabled` 주석 |
| **12V 인가 상태에서 토크 OFF** | 중력으로 팔이 자유낙하 → 링크/기어 손상, 협착 위험 | ⚠️ **사용자 지정 운영 규정** — 코드/문서에서 근거 미발견 |

### 6.2 안전 요구사항 목록

| ID | 요구사항 | 상태 | 구현/근거 |
|---|---|:---:|---|
| **SR-01** | **12V 전원이 인가된 상태에서 서보 토크를 OFF 하지 않는다** | 🔵 | ⚠️ **출처: 사용자 지정 규정. 코드 근거 없음.**<br>현재 `SetServoTorque(false)` API는 존재하나 **UI에 노출되지 않아** 조작자가 실수로 끌 수는 없음(호출처 미발견). 다만 소프트웨어적 방어(전원 상태 확인 후 거부)는 **미구현** |
| **SR-02** | **직접교시(Teach) 모드에서는 토크를 30 %로 낮춘다** | ⬜ | ⚠️ **출처: 사용자 지정 규정. 코드 근거 없음.** Teach 모드 자체가 미구현 |
| **SR-03** | **PincOpen 실물 명령은 기본 잠금 상태여야 한다** | ✅ | `PincOpenSafety.RealGripperEnabled = false` (기본값). `SOArmRealController.SetGripperTarget()` 이 `TryApprove()` 게이트 통과 필수, 차단 시 경고 1회 후 무시 |
| **SR-04** | 잠금 해제는 정해진 4단계 절차를 마친 뒤에만 가능하다 | 🔵 | 절차: ① 토크 OFF로 손으로 열고 닫으며 위치 읽기 → ② 열림 −140° / 닫힘 0° 확인, 아니면 **중단** → ③ LeRobot 재캘리브레이션(`--robot.id` 필수) → ④ 펌웨어 각도 리밋 굽기.<br>⚠️ 절차 상세를 담은 `docs/PINCOPEN.md` 가 **존재하지 않음** (TD-01 참조) |
| **SR-05** | 손가락 각도 명령은 검증된 범위(−69.9°~0°) 밖으로 나갈 수 없다 | ✅ | `PincOpenCoupling.SetDriveAngle()` — 범위 초과 시 클램프 + 경고 로그 |
| **SR-06** | 홈포즈 재지정 같은 되돌리기 어려운 조작에는 확인 다이얼로그를 둔다 | ✅ | `SmartFactoryUI_v3_4.DrawSetHomeDialog()` |
| **SR-07** | 모든 관절 명령은 소프트 리밋으로 클램프한다 | ✅ | `SOArmSimController.SetJointTarget()`, `SOArmRealController.SetJointTarget()`, `SOArmMotorMapper.AngleToServerValue()` 3중 |
| **SR-08** | 모터 펌웨어에 하드 각도 리밋과 과부하 보호를 굽는다 | ⬜ | `PincOpenSafety.GetFirmwareSetupSnippet()` 이 코드를 출력만 함. **라파에서 실행 안 됨**<br>값: `min −147° / max 0° / torque_limit 1000 / overload 40 / protective 5 / protection_time 7(70 ms) / accel 200` |
| **SR-09** | 그리퍼로 나가는 **모든** 명령 경로가 안전 게이트를 거쳐야 한다 | 🔴 | **결함 발견.** §6.3 참조 |
| **SR-10** | 비상 정지 시 로봇이 즉시 멈춰야 한다 | ✅ | 2026-08-02~03 구현. 토크는 끄지 않고 **현재 위치로 고정**한다 — 12V 팔은 토크를 끄면 떨어진다. Sim 은 물리 각도를 `xDrive.target` 에, Real 은 마지막 폴링 값을 `Goal_Position` 에 쓴다. 정지 중 송신·목표설정·슬라이더·**루틴 재생**을 모두 막고, 읽기는 유지한다. ESC 단축키. 해제는 `AdoptRealPose` 로 튐 없이 재개. ⚠️ 남은 것: 서버 `{"type":"stop"}` 미구현 — **소켓이 끊긴 상태에서는 정지 명령이 못 나간다** |
| **SR-11** | 장착 오프셋 덮어쓰기(`applyMountOffset`)는 기본 OFF여야 한다 | ✅ | 기본값 `false`, `SCENE` L1171 `applyMountOffset: 0`. 켠 채 값이 0이면 그리퍼가 손목 원점으로 튐 |
| **SR-12** | 커플링 배율이 잘못 저장돼 좌우 비대칭으로 닫히지 않아야 한다 | ✅ | 마이그레이터가 `preset = MJCF_Full` 강제 (`PincOpenMainSceneMigrator.cs` L127). `SCENE` L1165 `preset: 0` (= MJCF_Full) 확인 |

### 6.3 🔴 SR-09 결함 상세 — 그리퍼 안전 게이트 우회 경로

**증상 (코드에서 확인된 사실):**

`SmartFactoryUI_v3_4.DrawRobotPanel()` 은 관절 슬라이더를 다음 범위로 그린다.

```csharp
for (int i = 0; i < Mathf.Min(sliders.Length, robot.JointCount); i++)
```

`sliders.Length` = 6 (`r1Sliders = new float[6]`), `robot.JointCount` = 6 이므로
**J6(그리퍼, index 5)까지 각도 슬라이더로 그려진다.** 그 아래에 별도의 `Gripper 0~100 %` 슬라이더가 또 있다.

두 슬라이더는 서로 다른 경로를 탄다.

| 경로 | 흐름 | 안전 게이트 |
|---|---|:---:|
| **A. `Gripper %` 슬라이더** | `RouteGripperCommand` → `SOArmManager.SetGripperTarget` → `SOArmRealController.SetGripperTarget` → `PincOpenSafety.TryApprove()` | ✅ 통과 |
| **B. `J6` 각도 슬라이더** | `RouteJointCommand` → `SOArmManager.SetJointTarget` → `SOArmRealController.SetJointTarget` → `targetAngles[5]` → `Update()` 전송 루프가 `motorName="gripper"` 로 전송 | 🔴 **우회** |

경로 B는 `PincOpenSafety` 를 전혀 호출하지 않는다.
따라서 SR-03의 "기본 잠금"은 **경로 A에만 적용**된다.

**영향:** 실로봇이 연결된 상태에서 J6 슬라이더를 만지면, 캘리브레이션이 정리되지 않았더라도 그리퍼 모터로 명령이 나간다.

**권고 조치 (미적용):**
`SOArmRealController.Update()` 의 전송 루프에서 `joints[i].motorName == "gripper"` 인 경우
`PincOpenSafety.TryApprove()` 를 거치게 하거나, UI 쪽에서 J6 각도 슬라이더를 그리지 않도록 `JointCount − 1` 로 제한한다.

---

## 7. 제약사항 (Constraints)

### 7.1 기술 스택 제약

| ID | 제약 | 결과적으로 강제되는 설계 |
|---|---|---|
| **CON-01** | **Unity 런타임 스크립트는 C# 만 가능하다.** Unity는 Mono/IL2CPP 기반이라 Python을 그대로 실행할 수 없다 | 로봇 제어 UI·시뮬 전부 C#으로 작성 |
| **CON-02** | **LeRobot SDK는 Python 전용이다.** C#용 바인딩이 없다 | 두 언어를 **한 프로세스에 못 넣음** → 별도 프로세스 + IPC(프로세스 간 통신) 필요 → **TCP 소켓 + JSON** 선택 |
| **CON-03** | 라즈베리파이 4는 **ARM 아키텍처**다. 일반 PyTorch 휠은 `Illegal Instruction` 으로 죽는다 | `torch==2.7.0` + `torchvision==0.22.0` ARM CPU 빌드 고정 (`HANDOFF` §3) |
| **CON-04** | **URDF는 폐루프(closed-loop) 구속을 표현할 수 없다.** PincOpen은 평행 4절 링크(폐루프) | 종동축 3개를 **스크립트로 미러링** (`PincOpenCoupling`). ROS2 진영은 `mimic` 태그를 쓰지만 Unity URDF Importer는 이를 반영하지 않음 |
| **CON-05** | **STS3215는 위치 제어 모드에서 토크 제한 기능이 없다** | 소프트웨어 2중 방어 + 펌웨어 보호 파라미터 필요 (SR-03~SR-08) |
| **CON-06** | Unity URP(Universal Render Pipeline) 사용 | URDF 임포트 직후 모델이 분홍색 → Material 수동 적용 필요 (`HANDOFF` §10) |
| **CON-07** | Unity 6 URDF Importer의 기본 볼록분해기 **vHACD가 NullReferenceException으로 크래시**한다 | ① STL → DAE(Collada) 변환, ② URDF의 `collision` 섹션 전부 주석 처리, ③ 임포트 시 `convexMethod = unity` 강제 (`PincOpenSetupMenu.cs` L31~37) |

### 7.2 하드웨어·운영 제약

| ID | 제약 | 근거 |
|---|---|---|
| **CON-08** | 서버는 **1개 프로세스 / 1개 포트(5000)** 에서 로봇 2대를 모두 관리한다 | `HANDOFF` §5. 씬에도 `SOArmSocketClient` 가 **딱 1개**만 존재 (`SCENE` L10623) — Real 컨트롤러 2개가 이 하나를 공유 |
| **CON-09** | USB 포트 경로는 `ttyACM0` 이 아니라 `/dev/serial/by-id/...` 를 써야 한다 | 꽂는 순서·재부팅에 따라 번호가 바뀜 (`HANDOFF` §2) |
| **CON-10** | Waveshare Bus Servo Adapter의 **점퍼는 반드시 B 위치(USB-SERVO)** | A 위치면 통신 자체가 안 됨 (`HANDOFF` §1) |
| **CON-11** | 라즈베리파이 IP가 자주 바뀐다 | 안 되면 `hostname -I` 로 재확인 (`PROJECT_NOTES.md`). ⚠️ **현재 3곳의 IP 기록이 서로 다름** — TD-03 참조 |
| **CON-12** | Unity 프로젝트는 **외장하드 `F:\UNITY\LeRobot`** 에 있다 | 외장하드 미연결 시 전체 작업 불가 |
| **CON-13** | PincOpen 메시는 **CC BY-SA 4.0** 이다 | 공개 시 출처 표기 + **동일 라이선스 유지 의무** (`URDF` L322) |

---

## 8. ⚠️ 미확인 항목 (Open Items)

이 문서를 작성하며 **확인하지 못한** 항목이다. 추측으로 채우지 않았다.

| ID | 항목 | 왜 미확인인가 | 확인 방법 |
|---|---|---|---|
| TD-01 | **`docs/PINCOPEN.md` 가 존재하지 않는다** | `PincOpenSafety.cs` L43·L67, `PINCOPEN_INTEGRATION.md` L4·L110·L184, `URDF` L335 가 이 파일을 참조하지만 `F:\UNITY\LeRobot\docs\` 에는 `PINCOPEN_INTEGRATION.md` 하나뿐 | 파일 복구 또는 재작성. SR-04 절차의 원본이므로 **안전 문서 공백** |
| TD-02 | **`robot_server_dual.py` 소스가 이 저장소에 없다** | 라즈베리파이 `/home/sw/` 에만 존재. Unity 저장소 전체에 `.py` 파일 0개 | `scp sw@<IP>:/home/sw/robot_server_dual.py .` 후 저장소에 포함 |
| TD-03 | **라즈베리파이 IP 기록이 3곳에서 불일치** | `PROJECT_NOTES.md` = `192.168.75.245` / `HANDOFF` = `192.168.45.18` / `SOArmSocketClient.cs` 기본값 = `192.168.45.18` / **`SCENE` 실제값 = `192.168.75.245`** | 라파에서 `hostname -I` 확인 후 일원화 |
| TD-04 | 서버가 `get` / `set_home` / `torque` / `set_speed` / `home` 명령을 실제로 처리하는지 | TD-02 때문에 검증 불가. Unity 측 송신 코드만 확인됨 | 서버 소스 확보 후 대조 |
| TD-05 | 카메라 개수 / 연결 위치 / 작동 여부 | `HANDOFF` §1 에도 미확인으로 표기됨 | 라파에서 `ls /dev/video*`, `v4l2-ctl --list-devices` |
| TD-06 | 실물 SO-ARM101의 그리퍼 캘리브레이션 상태 | 실물 접근 필요 | SR-04 ① ② 단계 수행 |
| TD-07 | `pincopen_mount` 오프셋의 실측 대조 | 렌더링 기준 검증만 완료, 자로 재보지 않음 (`PINCOPEN_INTEGRATION` §10) | 버니어 캘리퍼스 실측 |
| TD-08 | `SOArmPresets.GetDefault6Axis()` 의 J1~J5 범위가 전부 ±110° 로 되어 있음 | `SOArmPresets.cs` L16~40 실제 코드. `SCENE` 의 직렬화 값(±110/±100/±96.8/±95/−157.2~162.8)과 **불일치**. 씬에 값이 저장돼 있으면 프리셋은 쓰이지 않으므로 현재 무해하나, 새 로봇을 추가하면 잘못된 범위가 들어감 | `SOArmPresets.cs` 를 URDF 값으로 수정 |

---

## 9. 추적성 매트릭스 (요구사항 ↔ 구현 파일)

| 파일 | 담당 요구사항 |
|---|---|
| `Script/SOArmJointConfig.cs` | FR-04, FR-05 |
| `Script/SOArmPresets.cs` | FR-04 (⚠️ TD-08) |
| `Script/ISOArmController.cs` | NFR-12 |
| `Script/SOArmMotorMapper.cs` | FR-08, SR-07 |
| `Script/SOArmSocketClient.cs` | FR-07, FR-09, FR-10, FR-14, FR-17, NFR-05~NFR-08 |
| `Script/SOArmSimController.cs` | FR-01, FR-03, FR-04, FR-06, SR-07 |
| `Script/SOArmRealController.cs` | FR-08, FR-11, FR-14, FR-17, NFR-01, NFR-02, SR-03, 🔴SR-09 |
| `Script/SOArmManager.cs` | FR-06, FR-12, FR-13, NFR-12 |
| `Script/SOArmDualManager.cs` | FR-18~FR-21, FR-30 |
| `Script/SmartFactoryUI_v3_4.cs` | FR-01, FR-02, FR-15, FR-16, NFR-16~NFR-19, SR-06 |
| `Script/SmartFactoryRecordUI.cs` | FR-23~FR-30 |
| `Script/RecordManager.cs` | FR-23~FR-28 |
| `Script/RecordProject.cs`, `Waypoint.cs` | FR-27 |
| `Script/SOArmIKController.cs` | FR-38, SR-06, SR-07 |
| `Script/ControlTowerCanvas.cs` | FR-38 (카티시안 면), NFR-16~NFR-19 |
| `raspberry_pi/robot_server_dual.py` | FR-08~FR-11, FR-38, SR-06 |
| `Script/PincOpenCoupling.cs` | FR-31~FR-33, SR-05, SR-11, SR-12, CON-04 |
| `Script/PincOpenSafety.cs` | SR-03, SR-04, SR-08 |
| `Editor/PincOpenSetupMenu.cs` | FR-34, CON-07 |
| `Editor/PincOpenMainSceneMigrator.cs` | FR-34, NFR-09, NFR-10, SR-12 |
| `Editor/PincOpenCapture.cs` | FR-35 |
| `Assets/SO101_unity/so101.urdf` | NFR-13, CON-04, CON-13 |

---

## 10. 관련 문서

| 문서 | 내용 |
|---|---|
| `docs/SW_ARCHITECTURE.md` | 소프트웨어 계층 구조, 클래스 관계, 통신 프로토콜, 설계 결정 |
| `docs/HW_ARCHITECTURE.md` | 물리 구성, 기구 치수, 모터 사양, 전원·배선, 안전 한계 |
| `docs/PINCOPEN_INTEGRATION.md` | PincOpen 그리퍼 통합 확정 기록 |
| `docs/PINCOPEN.md` | ⚠️ **부재** (TD-01) — 실물 그리퍼 안전 절차 원본 |
