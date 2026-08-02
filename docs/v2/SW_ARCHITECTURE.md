# S/W Architecture

| **과정명** | **Unity 활용 DT 로봇 분야 개발자 양성과정 1기** |
| --- | --- |
| **프로젝트명** | SO-ARM101 스마트팩토리 로봇팔 디지털 트윈 | **문서 버전** | v2.0 |
| **팀명** | SO-ARM101 (개인 프로젝트) | **작성일** | 2026-08-01 |
| **작성자** | 김애리 | **최종 수정** | 2026-08-01 |
| **문서 종류** | S/W Architecture / Interface Specification / State Diagram / Sequence Diagram / Data Structure |

---

## 변경 이력

```
8/1 - v2.0 최초 작성 (Confluence 팀 문서 체계 적용)
8/1 - Interface Specification 표 신설 (TCP Socket / JSON 명령 규격)
8/1 - 그리퍼 안전 게이트 이중화 반영 (SetGripperTarget + 송신 루프)
8/1 - 쓰기 보류(holdWritesUntilSynced) · 폴링 타임아웃 · 자동 재접속 반영
8/1 - PincOpen 손가락 리밋 -48.5° 로 정정 (구 문서 -69.9° 는 폐기)
```

## 범례

> * ✅ 구현 + 코드 경로 검증 완료
> * 🟡 코드 구현 확인됨, 실행 검증 기록 없음
> * ⬜ 미구현
> * 🔺 문서와 실제 구현이 불일치
> * ⚠️ 미확인 — 확인하지 못함. 추측으로 채우지 않았음

## 근거 파일

```
F:\UNITY\LeRobot\Assets\Script\*.cs          (16개)
F:\UNITY\LeRobot\Assets\Editor\*.cs          (3개)
F:\UNITY\LeRobot\Assets\Scenes\LeRobot.unity (직렬화 값 실측)
F:\UNITY\LeRobot\Assets\SO101_unity\so101.urdf
```

---

# 1. S/W Architecture — 계층 구조

```
┌──────────────────────────────────────────────────────────────────┐
│  사용자 (조작자)                                                  │
└───────────────────────────────┬──────────────────────────────────┘
                                │ 슬라이더 · 버튼
┌───────────────────────────────▼──────────────────────────────────┐
│  Layer 1 — Unity C# 애플리케이션 (Windows PC / Unity 6.4)         │
│                                                                  │
│   [Presentation]  SmartFactoryUI_v3_4 · SmartFactoryRecordUI     │
│         │                                                        │
│   [Orchestration] SOArmDualManager · SOArmManager · RecordManager│
│         │                                                        │
│   [Controller]    SOArmSimController · SOArmRealController       │
│                   (공통 계약: ISOArmController)                   │
│         ├────────────────────────┐                               │
│   [Physics/Visual]          [Transport]                          │
│   ArticulationBody          SOArmSocketClient                    │
│   URDF Importer             SOArmMotorMapper                     │
│   PincOpenCoupling          PincOpenSafety                       │
└───────────────────────────────┬──────────────────────────────────┘
                                │ 송신 10 Hz / 폴링 30 Hz
┌───────────────────────────────▼──────────────────────────────────┐
│  Layer 2 — 통신                                                   │
│   TCP Socket · 포트 5000 · NDJSON (한 줄 = JSON 1개, \n 구분)     │
└───────────────────────────────┬──────────────────────────────────┘
┌───────────────────────────────▼──────────────────────────────────┐
│  Layer 3 — Python 서버 (Raspberry Pi 4 / Ubuntu 24.04)           │
│   robot_server_dual.py — mode 라우팅 · 캘리브 적용 · 토크 관리    │
│   ⚠️ 소스가 저장소에 없어 이 계층의 서술은 전부 미확인            │
└───────────────────────────────┬──────────────────────────────────┘
┌───────────────────────────────▼──────────────────────────────────┐
│  Layer 4 — LeRobot SDK (Hugging Face, 차용)                      │
│   FeetechMotorsBus · 정규화값 ↔ 엔코더 카운트 변환                │
└───────────────────────────────┬──────────────────────────────────┘
                                │ USB 시리얼 · TTL 1 Mbps
┌───────────────────────────────▼──────────────────────────────────┐
│  Layer 5 — 펌웨어 (Feetech STS3215, 불가침)                       │
│   내부 MCU PID 위치 제어 루프 · 자기식 엔코더                     │
└───────────────────────────────┬──────────────────────────────────┘
                                ▼
                    ⚙️ 실제 모터 회전 (STS3215 × 12)
```

## 1.1 자작 / 차용 / 불가침 구분

| **구분** | **항목** | **근거** |
| --- | --- | --- |
| ✍️ 자작 | `Assets/Script/*.cs` 16개 (`SOArmControl` 네임스페이스) | 파일 실측 |
| ✍️ 자작 | `Assets/Editor/*.cs` 3개 | 파일 실측 |
| ✍️ 자작 | `robot_server_dual.py` | ⚠️ 라파에만 존재, 저장소에 없음 |
| ✍️ 자작 | `so101.urdf` 의 PincOpen 통합부 (L317~L511) | URDF 주석 |
| ✍️ 자작 | STL → DAE 변환 + `collision` 주석 처리 | CON_07 |
| 📦 차용 | LeRobot SDK (`huggingface/lerobot`) | — |
| 📦 차용 | `Unity.Robotics.UrdfImporter` | `PincOpenSetupMenu.cs` `using` |
| 📦 차용 | PincOpen 메시 6종 (Pollen Robotics) | URDF L320~322, CC BY-SA 4.0 |
| 📦 차용 | 원본 `so101_new_calib.urdf` (onshape-to-robot) | URDF L2~4 |
| 🚫 불가침 | STS3215 펌웨어 (PID 루프, 엔코더 처리) | — |
| 🚫 불가침 | Unity PhysX `ArticulationBody` solver | — |

---

# 2. 모듈 명세 (Component Specification)

## 2.1 런타임 스크립트 (`Assets/Script/`)

| **#** | **파일** | **종류** | **역할** | **핵심 심볼** |
| --- | --- | --- | --- | --- |
| 1 | `SOArmJointConfig.cs` | 데이터 | 관절 1개 설정 | `displayName`, `motorName`, `minAngle`, `maxAngle`, `homeAngle`, `articulationBody`, `invertSign`, `angleOffset` |
| 2 | `SOArmPresets.cs` | 정적 | 6축 기본 프리셋 | `GetDefault6Axis()` 🔺 GAP_04 |
| 3 | `ISOArmController.cs` | 인터페이스 | 시뮬/실물 공통 계약 (19 멤버) | `SetJointTarget`, `SetAllJointTargets`, `SetGripperTarget`, `StopMotion`, `GoToHome`, `GetHomePose` … |
| 4 | `SOArmMotorMapper.cs` | 정적 | 각도 ↔ 정규화값 변환 | `AngleToServerValue`, `ServerValueToAngle`, `PercentToGripperValue` |
| 5 | `SOArmSocketClient.cs` | MonoBehaviour | TCP 송수신 · 자동 재접속 | `SendMotorCommand`, `RequestAngles`, `RequestSetHome`, `RequestTorque`, `SendRaw`, `ReceiveLoop` |
| 6 | `SOArmSimController.cs` | MonoBehaviour | 시뮬 제어 | `ConfigureArticulationBodies`, `ApplyToArticulationBodies` |
| 7 | `SOArmRealController.cs` | MonoBehaviour | 실로봇 제어 · 폴링 | `RequestAnglesOnce`, `AdoptRealPose`, `SaveHomePose`, `SetServoTorque`, `ParseAndConvertAngles` |
| 8 | `SOArmManager.cs` | MonoBehaviour | 1대 통합 (Sim+Real) | `Mode{SimOnly,RealOnly,Mirror}`, `PrimaryReader`, `HandleRealAngles`, `InitialSyncCoroutine` |
| 9 | `SOArmDualManager.cs` | MonoBehaviour | 2대 통합 | `ControlMode{Independent,Mirror}`, `robot1Enabled/2Enabled`, `isRecordModeActive`, `RouteJointCommand`, `RouteGripperCommand` |
| 10 | `SmartFactoryUI_v3_4.cs` | MonoBehaviour | 메인 OnGUI | `DrawTopBar`, `DrawRobotPanel`, `DrawBottomBar`, `DrawSetHomeDialog`, `ApplySpeedToServer`, `SendGoHome` |
| 11 | `SmartFactoryRecordUI.cs` | MonoBehaviour | 녹화 UI | `DrawStepList`, `DrawPlaybackControls`, `DrawLoadDialog` |
| 12 | `RecordManager.cs` | MonoBehaviour | 녹화/재생 로직 | `AddMotionStepFromUI`, `AddWaitStep`, `AddLoopStartStep`, `PlaybackRoutine`, `SaveProject`, `LoadProject` |
| 13 | `RecordProject.cs` | 데이터 | 프로젝트 직렬화 | `waypoints`, `RenumberSteps`, `Touch` 🔺 주석 인코딩 깨짐 |
| 14 | `Waypoint.cs` | 데이터 | 스텝 1개 | `type{motion,wait,loop_start,loop_end}`, `target`, `joints[6]`, `joints2[6]`, `loopCount` |
| 15 | `PincOpenCoupling.cs` | MonoBehaviour `[ExecuteAlways]` | 4절 링크 커플링 | `CouplingPreset`, `ApplyCoupling`, `SetGripperPercent`, `SetDriveAngle`, `ConfigureDrives`, `AutoBind` |
| 16 | `PincOpenSafety.cs` | 정적 | 실물 명령 안전장치 | `RealGripperEnabled`, `TryApprove`, `PercentToServerValue`, `GetFirmwareSetupSnippet` |

## 2.2 에디터 도구 (`Assets/Editor/`)

| **파일** | **메뉴 경로** | **역할** |
| --- | --- | --- |
| `PincOpenSetupMenu.cs` | `Tools ▸ SO-ARM ▸ PincOpen 로봇 재임포트`<br>`Tools ▸ SO-ARM ▸ 선택한 로봇에 PincOpenCoupling 설정`<br>`Tools ▸ SO-ARM ▸ J6 슬롯 점검 (그리퍼 연결 확인)`<br>`Tools ▸ SO-ARM ▸ PincOpen 미리보기 씬 만들기` | URDF 재임포트 (vHACD 회피, `convexMethod = unity`), 커플링 자동 연결, 잘못된 J6 배선 감지 |
| `PincOpenMainSceneMigrator.cs` | `Tools ▸ SO-ARM ▸ 메인 씬에 PincOpen 이식` | 메인 씬의 순정 그리퍼 subtree 만 교체, J6 재배선, `xDrive` 직렬화 영속화. **재실행해도 안전(멱등)** |
| `PincOpenCapture.cs` | `Tools ▸ SO-ARM ▸ 그리퍼 장착 상태 캡처`<br>`Tools ▸ SO-ARM ▸ 그리퍼 구동 자체검증` | 헤드리스 렌더링 검증, 자체검증/대칭/중력/배율비교/E2E 테스트 |

## 2.3 설계 패턴

| **패턴** | **적용 위치** | **효과** |
| --- | --- | --- |
| Strategy / 공통 인터페이스 | `ISOArmController` | UI가 시뮬인지 실물인지 몰라도 동일하게 명령 |
| Composite | `SOArmManager` 가 `ISOArmController` 를 구현하면서 내부에 `sim`+`real` 보유 | 1대 = 부품 2개짜리 합성체. `SOArmDualManager` 가 다시 2대를 합성 |
| Facade | `SOArmDualManager.RouteJointCommand()` | UI는 모드 분기를 몰라도 됨 |
| Observer | `SOArmRealController.OnAnglesReceived` → `SOArmManager.HandleRealAngles` | 폴링 결과를 느슨하게 전파 |
| Producer-Consumer | `ReceiveLoop`(백그라운드) → `ConcurrentQueue` → `Update()`(메인) | Unity API 스레드 제약 회피 |
| Command / Memento | `Waypoint` + `RecordProject` | 자세를 데이터로 굳혀 저장·재생 |
| Guard / Gatekeeper | `PincOpenSafety.TryApprove()` | 위험 명령 차단. **2개 경로 모두에 적용** |

---

# 3. 씬 배선도 (`LeRobot.unity` 직렬화 실측)

```
SmartFactoryManager (GameObject)
 ├─ SOArmDualManager      controlMode = 1 (Mirror), robot1Enabled/2Enabled = 1
 ├─ SmartFactoryUI_v3_4
 ├─ SmartFactoryRecordUI
 └─ RecordManager

SOArmSocketClient  ★ 씬 전체에 딱 1개
   serverIP   = 192.168.75.245
   serverPort = 5000
   autoReconnect = 1

Robot1_Group
 ├─ SOArmManager          autoConnectReal=1, realToSimSync=1, syncOnStart=1
 └─ SOArmRealController   robotServerMode = robot1
                          sendRateHz=10, pollHz=30,
                          getResponseTimeout=1, holdWritesUntilSynced=1
     └─ (Awake 폴백) FindAnyObjectByType<SOArmSocketClient>() ──┐
                                                                │
Robot_1 (URDF 임포트)                                           │
 ├─ SOArmSimController    joints[0..5] (범위는 §3.1)             │
 ├─ PincOpenCoupling      preset=0 (MJCF_Full), applyMountOffset=0│
 └─ ArticulationBody 체인                                        │
     base → shoulder → upper_arm → lower_arm → wrist →          │
     gripper_link → pincopen_adapter → pincopen_base →          │
     left_proximal(구동) + 종동 3개                              │
                                                                │
Robot2_Group  (Robot1과 동일 구조, robotServerMode = robot2) ────┘
Robot_2       (Robot_1과 동일 구조)
```

> ⭐ **핵심 구조:** 씬에 `SOArmSocketClient` 는 **1개뿐**이다 (`serverIP` 직렬화 항목이 파일 전체에 1회만 등장).
> 두 `SOArmRealController` 가 이 하나를 공유하며, 구분은 **JSON의 `mode` 필드로만** 한다.
> `Awake()` 의 3단 폴백(인스펙터 지정 → `GetComponent` → `FindAnyObjectByType`)으로 자동 연결된다.

## 3.1 관절 범위 (씬 4개 배열 전부 일치)

`SOArmSimController × 2` + `SOArmRealController × 2` = **6관절 × 4 = 24개 슬롯**을 확인했고 전부 동일하다.

| **Index** | **motorName** | **minAngle (°)** | **maxAngle (°)** | **URDF 원본 (rad)** | **환산 (°)** | **일치** |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | `shoulder_pan` | −110 | 110 | −1.91986 ~ 1.91986 | −110.00 ~ 110.00 | ✅ |
| 1 | `shoulder_lift` | −100 | 100 | −1.74533 ~ 1.74533 | −100.00 ~ 100.00 | ✅ |
| 2 | `elbow_flex` | −96.8 | 96.8 | −1.69 ~ 1.69 | −96.83 ~ 96.83 | ✅ |
| 3 | `wrist_flex` | −95 | 95 | −1.65806 ~ 1.65806 | −95.00 ~ 95.00 | ✅ |
| 4 | `wrist_roll` | −157.2 | 162.8 | −2.74385 ~ 2.84121 | −157.21 ~ 162.79 | ✅ (비대칭) |
| 5 | `gripper` | **−48.5** | **0** | −0.8465 ~ 0 | −48.50 ~ 0 | ✅ |

> ⚠️ index 5 는 **손가락 각도**다. 모터 각도가 아니다 (H/W Architecture §4.6 참조).
> 🔺 코드 폴백 `SOArmPresets.GetDefault6Axis()` 는 J1~J5 가 전부 ±110° 로 위와 다르다 (GAP_04).
> 씬에 값이 저장돼 있으면 프리셋은 쓰이지 않으므로 현재는 무해하나, 3번째 로봇 추가 시 잘못된 범위가 유입된다.

---

# 4. Interface Specification

## 4.1 전송 규격 (Transport)

| **항목** | **값** |
| --- | --- |
| 방식 | TCP Socket (클라이언트 = Unity, 서버 = 라즈베리파이) |
| 포트 | **5000** |
| 인코딩 | UTF-8 |
| 프레이밍 | **NDJSON** — 한 줄에 JSON 객체 하나, `\n` 구분 |
| 수신 처리 | `StringBuilder lineBuffer` 누적 후 `'\n'` 단위 분할 (부분 수신 대응) |
| 응답 매칭 | **FIFO 큐** (`pendingCallbacks`). 요청 ID 없음 |
| 큐 복구 | 대기 콜백이 **16개** 초과 시 전량 폐기해 정렬 복원 |
| 동시성 | `lock (writeLock)` 으로 송신 직렬화, 콜백 등록 → send 순서 보장 |
| 자동 재접속 | `autoReconnect` = true, 기본 간격 **2초** (0.5~10초 조절) |
| 숫자 포맷 | `value.ToString("F2", CultureInfo.InvariantCulture)` |

> **왜 NDJSON 인가:** TCP는 바이트 스트림이라 `{"a":1}{"b":2}` 가 한 번에 올 수도, 쪼개져 올 수도 있다.
> 개행 하나를 구분자로 약속하면 받는 쪽은 개행이 나올 때까지 모았다 자르기만 하면 된다.
> 길이 헤더보다 단순하고, `nc`/`telnet` 으로 손으로 찔러볼 수 있어 디버깅이 압도적으로 쉽다.

## 4.2 Command Specification (Unity → Server)

| **명령명** | **type** | **송신 데이터 구조** | **응답 콜백** | **생성 위치** | **비고** |
| --- | --- | --- | --- | --- | --- |
| 모터 위치 명령 | *(없음)* | `{"mode": "robot1", "motor": "shoulder_lift", "value": 45.00}` | ✗ 없음 | `SOArmSocketClient.SendMotorCommand()` | 구버전 호환을 위해 `type` 필드 없음 |
| 현재 각도 요청 | `get` | `{"type":"get","mode":"robot1"}` | ✓ 있음 | `SOArmSocketClient.RequestAngles()` | 30 Hz 폴링, in-flight 1건 |
| 홈포즈 저장 | `set_home` | `{"type":"set_home","mode":"robot1"}` | ✓ 있음 | `SOArmSocketClient.RequestSetHome()` | 확인 다이얼로그 필수 |
| 토크 ON/OFF | `torque` | `{"type":"torque","mode":"robot1","enable":false}` | ✓ 있음 | `SOArmSocketClient.RequestTorque()` | 🟡 UI 노출 없음 |
| 홈으로 이동 | `home` | `{"type":"home","mode":"both"}` | ✗ 없음 | `SmartFactoryUI_v3_4.SendGoHome()` → `SendRaw()` | — |
| 속도/가속도 설정 | `set_speed` | `{"type":"set_speed","mode":"both","velocity":800,"acceleration":50}` | ✗ 없음 | `SmartFactoryUI_v3_4.ApplySpeedToServer()` | 0.5초 디바운스 |

### 4.2.1 필드 정의

| **필드** | **타입** | **값 / 범위** | **설명** |
| --- | --- | --- | --- |
| `mode` | string | `robot1` / `robot2` / `mirror` / `both` | 제어 대상. 서버가 이 필드로 라우팅 |
| `motor` | string | `shoulder_pan`, `shoulder_lift`, `elbow_flex`, `wrist_flex`, `wrist_roll`, `gripper` | 모터 이름 |
| `value` | float | −100.00 ~ 100.00 | 정규화 위치값, **소수점 2자리 고정** |
| `enable` | bool | `true` / `false` | 토크 ON/OFF |
| `velocity` | int | 0 ~ 3000 | 프리셋 — 🐢 정밀 400 / 🚶 일반 800 / 🏃 빠름 1500 |
| `acceleration` | int | 1 ~ 254 | 프리셋 — 🐢 30 / 🚶 50 / 🏃 100 |

## 4.3 Response Specification (Server → Unity)

| **응답 종류** | **데이터 구조** | **파싱 방식** | **비고** |
| --- | --- | --- | --- |
| 각도 응답 | `{"ok":true,"robot1":{"shoulder_pan":12.3,"shoulder_lift":45.0,"elbow_flex":-3.1,"wrist_flex":0.0,"wrist_roll":8.8,"gripper":-90.0}}` | `mode` 문자열을 키로 찾아 그 뒤 첫 `{` 부터 **중괄호 깊이를 세어** 짝 맞는 `}` 까지 잘라냄. 내부를 `,` 로 분할해 `키:값` 파싱 후 `float.TryParse(InvariantCulture)` | 값은 −100~100 정규화값 → `ServerValueToAngle()` 로 각도 복원 |
| 성공 | `{"ok":true}` | 문자열에 `"ok":true` 포함 여부로 판정 | `SaveHomePose()` / `SetServoTorque()` |
| 실패 | `{"ok":false,"error":"..."}` | 위와 동일 | — |
| 연결 끊김 (클라이언트 자체 주입) | `{"ok":false,"error":"disconnected"}` | `Disconnect()` 가 대기 콜백에 주입 | 콜백 누락 방지 |
| 미연결 (클라이언트 자체 주입) | `{"ok":false,"error":"not connected"}` | 송신 전 검사 | — |
| 송신 실패 (클라이언트 자체 주입) | `{"ok":false,"error":"send failed"}` | 예외 처리 | — |

> ⚠️ 서버가 실제로 위 형식으로 응답하는지는 **미확인**이다 (OPEN_01). Unity 측 파서가 기대하는 형식만 확정됐다.

## 4.4 전송률 정책

| **방향** | **주기** | **조건** | **근거** |
| --- | --- | --- | --- |
| Unity → 서버 (위치 명령) | **10 Hz** | 변화량 > 0.5° | 과속 시 서버 JSON 파서 `Extra data` 예외 발생 이력 |
| Unity → 서버 (`get` 폴링) | **30 Hz** | 이전 응답 도착 후에만 (in-flight 1건), 1초 타임아웃 | `pollHz=30`, `getResponseTimeout=1` |
| Unity → 서버 (`set_speed`) | 최대 2 Hz | 0.5초 디바운스 | `lastSpeedSendTime` |
| 소켓 재접속 | 0.5 Hz | 연결 끊김 감지 시 | `reconnectInterval = 2f` |

**부하 계산 (최악):**

```
위치 명령 : 6 관절 × 10 Hz × 2 대 = 120 줄/s   (0.5° 필터로 실제는 훨씬 적음)
각도 폴링 :          30 Hz × 2 대 =  60 줄/s
──────────────────────────────────────────────
합계                              ≈ 180 줄/s
한 줄 약 60~80 B → 약 12~15 KB/s
```

병목은 대역폭이 아니라 **1 Mbps TTL 시리얼 버스**와 **서버의 순차 처리**다.

## 4.5 값 변환 규격 (`SOArmMotorMapper`)

| **함수** | **수식** | **비고** |
| --- | --- | --- |
| `AngleToServerValue(angle, joint)` | `n = (angle − min) / (max − min)`<br>`v = n × 200 − 100`<br>`invertSign` 이면 `v = −v`<br>`Clamp(v, −100, 100)` | range < 0.01 이면 0 반환 |
| `ServerValueToAngle(v, joint)` | `invertSign` 이면 `v = −v`<br>`n = (v + 100) / 200`<br>`Lerp(min, max, n)` | 위의 역변환 |
| `PercentToGripperValue(percent)` | `Clamp(percent, 0, 100)` → `(percent − 50) × 2` | ⚠️ 순정 매퍼. **PincOpen 경로에서는 사용하지 않는다** |
| `PincOpenSafety.PercentToServerValue(percent)` | `m = Clamp(TravelMarginPercent, 0, 40)` (기본 5)<br>`safePct = Lerp(m, 100 − m, percent/100)`<br>`v = safePct × 2 − 100`<br>`InvertDirection` 이면 `v = −v` | 양 끝 5 % 여유. 끝단에서 모터가 계속 밀어붙이는 것을 방지 |

**예시** — J2 `shoulder_lift` (범위 −100 ~ 100) 에 45° 지정 시
`n = (45 − (−100)) / 200 = 0.725` → `v = 0.725 × 200 − 100 = +45.00`

---

# 5. State Diagram

## 5.1 소켓 연결 상태

```
        ┌──────────────┐
        │ DISCONNECTED │◀────────────────────────────┐
        └──────┬───────┘                             │
               │ Connect() / autoReconnect(2초)      │
               ▼                                     │
        ┌──────────────┐   실패                      │
        │  CONNECTING  ├─────────────────────────────┤
        └──────┬───────┘                             │
               │ 성공 → 수신 스레드 기동              │
               ▼                                     │
        ┌──────────────┐  ReceiveLoop 종료 /         │
        │  CONNECTED   ├──Disconnect()/송신 예외─────┘
        └──────────────┘
```

| **상태** | **판정 필드** | **진입 조건** | **비고** |
| --- | --- | --- | --- |
| `DISCONNECTED` | `isConnected = false` | 초기 / `Disconnect()` / 수신 스레드 종료 / 송신 예외 | 대기 콜백에 `disconnected` 응답 주입 |
| `CONNECTING` | — | `Connect()` 호출 | 이미 살아있는 연결이 있으면 **아무것도 하지 않음** |
| `CONNECTED` | `isConnected = true` | `TcpClient.Connect()` 성공 | `ReceiveLoop` 백그라운드 스레드 기동 |

## 5.2 실로봇 컨트롤러 쓰기 게이트 상태

```
   [Play 시작]
        │
        ▼
┌───────────────────┐   실물 각도 첫 수신 (AdoptRealPose)   ┌──────────────┐
│  HOLD (쓰기 보류)  ├───────────────────────────────────▶│ WRITE ENABLED │
│ WritesEnabled=false│                                    │WritesEnabled  │
└───────────────────┘                                    │      =true    │
        │                                                 └──────────────┘
        └─ holdWritesUntilSynced = false 이면 즉시 WRITE ENABLED
```

> **왜 필요한가:** Play 직후 시뮬은 홈(0°)에서 시작한다. 그대로 전송하면 실물이 홈으로 **끌려간다.**
> 디지털 트윈의 올바른 방향은 "실물이 진실" 이므로, 실물 자세를 한 번 읽어 채택할 때까지 쓰기를 막는다.

## 5.3 각도 폴링 상태

```
┌──────────┐  pollTimer ≥ 1/pollHz   ┌───────────────┐
│   IDLE   ├────────────────────────▶│    WAITING    │
│          │◀────────────────────────┤ (응답 대기)    │
└──────────┘  응답 수신 or 타임아웃   └───────────────┘
                                            │
                                     getWaitTimer ≥ 1.0s
                                     → getTimeoutCount++
                                     → 경고(1~3회, 이후 100회마다)
```

## 5.4 제어 모드 (직교 3축)

| **축** | **값** | **의미** |
| --- | --- | --- |
| `ControlMode` | `Independent` / `Mirror` | 제어 **방식** |
| `robot1Enabled` / `robot2Enabled` | on / off | 채널 **활성화** |
| `isRecordModeActive` | on / off | 녹화 **여부** |

> 2 × 4 × 2 = **16가지 조합**을 enum 5개가 아니라 3개 필드로 표현한다.
> 초기 설계의 `Robot1Only`/`Robot2Only` 는 "제어 방식"이 아니라 "어느 팔을 쓰나"였는데 같은 enum에 섞여 있었다.
> 🔺 `PROJECT_NOTES.md` 의 5모드 기술은 구버전이다 (GAP_05).

## 5.5 재생(Playback) 상태

```
┌──────┐  StartPlayback()   ┌──────────┐  마지막 스텝 완료  ┌──────────┐
│ IDLE ├───────────────────▶│ PLAYING  ├──────────────────▶│ COMPLETE │
└──────┘                    └────┬─────┘                   └────┬─────┘
   ▲                             │ StopPlayback()               │
   └─────────────────────────────┴──────────────────────────────┘
```

| **스텝 타입** | **동작** |
| --- | --- |
| `motion` | 관절/그리퍼 목표 적용 후 `WaitForSeconds(max(0.1, delayAfter))` |
| `wait` | `WaitForSeconds(duration)` |
| `loop_start` | `loopStack.Push((i, loopCount − 1))` |
| `loop_end` | 스택 top의 `remaining > 0` 이면 `remaining−1` 로 다시 push 후 `i = 시작 인덱스` 로 점프 |

---

# 6. Sequence Diagram

## 6.1 Scenario #2 — 슬라이더 → 실로봇 명령

```
사용자   UI          DualMgr    Manager   Sim      Real     Mapper   Socket   Server   SDK    FW
  │      │             │          │        │        │         │        │        │      │      │
  ├─드래그▶             │          │        │        │         │        │        │      │      │
  │      ├ |Δ|>0.001 확인          │        │        │         │        │        │      │      │
  │      ├─RouteJointCommand(true,1,45)     │        │         │        │        │      │      │
  │      │             ├─(Mirror) robot1.SetJointTarget(1,45)  │        │        │      │      │
  │      │             ├─(Mirror) robot2.SetJointTarget(1,45)  │        │        │      │      │
  │      │             │          ├────────▶│ targetAngles[1]=Clamp(45,−100,100) │      │      │
  │      │             │          ├──────────────────▶│ targetAngles[1]=Clamp    │      │      │
  │      │             │          │        │        │         │        │        │      │      │
  │      │  ┌── 시뮬 경로 (매 프레임 Update) ──┐    │         │        │        │      │      │
  │      │  │ angle = target + angleOffset  │      │         │        │        │      │      │
  │      │  │ (invertSign 이면 부호 반전)     │      │         │        │        │      │      │
  │      │  │ xDrive.target = Clamp(angle)  │      │         │        │        │      │      │
  │      │  └───────────────────────────────┘      │         │        │        │      │      │
  │      │                                          │         │        │        │      │      │
  │      │  ┌── 실물 경로 (10 Hz 게이트) ─────────────┐        │        │      │      │
  │      │  │ WritesEnabled 확인 (미확인 시 보류)     │        │        │      │      │
  │      │  │ Time.time − lastSendTime ≥ 1/10 ?     │        │        │      │      │
  │      │  │ diff = |target − lastSent| > 0.5° ?   │        │        │      │      │
  │      │  │ motorName == "gripper" ?              │        │        │      │      │
  │      │  │   → PincOpenSafety.TryApprove() 필수  │        │        │      │      │
  │      │  └──────────────────┬────────────────────┘        │        │      │      │
  │      │                     ├─AngleToServerValue(45,J2)──▶│        │      │      │
  │      │                     │◀────────── +45.00 ──────────┤        │      │      │
  │      │                     ├─SendMotorCommand("robot1","shoulder_lift",45.0)  │  │
  │      │                     │                             ├─{"mode":"robot1",   │  │
  │      │                     │                             │  "motor":"shoulder_lift",
  │      │                     │                             │  "value":45.00}\n ─▶│  │
  │      │                     ├─lastSentAngles[1] = 45.0    │        │      │      │
  │      │                     │                             │        ├─json.loads │
  │      │                     │                             │        ├─mode 라우팅│
  │      │                     │                             │        ├─bus.write ▶│
  │      │                     │                             │        │      ├─TTL ▶│
  │      │                     │                             │        │      │  PID루프
```

**이 경로에서 확인된 수치**

| **항목** | **값** | **출처** |
| --- | --- | --- |
| UI 변화 감지 임계값 | 0.001° | `DrawRobotPanel()` |
| 그리퍼 UI 변화 임계값 | 0.5 % | `DrawRobotPanel()` |
| 전송 주기 | 10 Hz | 씬 `sendRateHz: 10` |
| 전송 최소 변화량 | 0.5° | `minChangeToSend` |
| 첫 전송 강제 | `lastSentAngles[i] = NaN` → `diff = float.MaxValue` | `Awake()` |
| 숫자 포맷 | `"F2"` + `InvariantCulture` | `SendMotorCommand()` |
| 시뮬 드라이브 | stiffness 10000 / damping 1000 / forceLimit 1000 | 씬 실측 |

## 6.2 Scenario #3 — 양방향 동기화 (폴링)

```
Real          Socket        Server        Manager        Sim
 │              │             │              │            │
 ├─(30Hz) RequestAnglesOnce   │              │            │
 ├─waitingForGetResponse=true │              │            │
 ├─RequestAngles(mode, cb)───▶│              │            │
 │              ├─{"type":"get","mode":"robot1"}\n ──────▶│
 │              │             ├─엔코더 읽기   │            │
 │              │◀─{"ok":true,"robot1":{...}}\n ──────────┤
 │              ├─(백그라운드) lineBuffer 개행 분할        │
 │              ├─incomingResponses.Enqueue()             │
 │              │                                          │
 │              ├─(메인 Update) pendingCallbacks 매칭 → cb 실행
 │◀─────────────┤                                          │
 ├─waitingForGetResponse=false                             │
 ├─ParseAndConvertAngles(resp)  ※중괄호 깊이 카운트         │
 ├─ServerValueToAngle(각 모터)                              │
 ├─LastReadAngles = angles                                 │
 ├─(첫 수신이면) AdoptRealPose() → WritesEnabled = true     │
 ├─OnAnglesReceived ─────────▶│                            │
 │              │             │  ├─HandleRealAngles()      │
 │              │             │  ├─realToSimSync 확인       │
 │              │             │  ├─mode != SimOnly 확인     │
 │              │             │  ├─motorName 매핑 (못 받은 값은 현 상태 유지)
 │              │             │  ├─sim.SetAllJointTargets()▶│
 │              │             │  │                          ├─xDrive.target
```

> 타임아웃 경로: 1초 안에 콜백이 안 오면 `waitingForGetResponse = false` 로 강제 해제하고
> `getTimeoutCount++`. 경고는 1~3회 그리고 이후 100회마다만 출력한다(로그 도배 방지).

## 6.3 Scenario #6 — 그리퍼 개폐 + 커플링 전파

```
사용자   UI       DualMgr   Manager    Sim      Coupling   PhysX    Real    Safety   Socket
  │      │          │         │         │          │         │       │       │        │
  ├「🤏닫기」▶       │         │         │          │         │       │       │        │
  │      ├ |Δ|>0.5 확인       │         │          │         │       │       │        │
  │      ├─RouteGripperCommand(true, 0.0)          │         │       │       │        │
  │      │          ├─robot1.SetGripperTarget(0.0) │         │       │       │        │
  │      │          │         │                     │         │       │       │        │
  │  ┌─ 시뮬 경로 ✅ ────────────────────────────────────────────────────────────────┐
  │  │   ├─gripperIdx = joints.Length − 1 = 5                                       │
  │  │   ├─angle = Lerp(min=−48.5, max=0, t=0.0) = −48.5°                          │
  │  │   ├─SetJointTarget(5, −48.5)                                                 │
  │  │   │  (매 프레임 Update) xDrive.target = −48.5  ← left_proximal               │
  │  │   │  (매 프레임 LateUpdate, PincOpenCoupling)                                │
  │  │   │     drive = driveJoint.xDrive.target = −48.5                            │
  │  │   │     leftDistal    = −48.5 × (−1.0) = +48.5 → 관절 리밋으로 재클램프       │
  │  │   │     rightProximal = −48.5 × (−1.0) = +48.5 → 재클램프                    │
  │  │   │     rightDistal   = −48.5 × (+1.0) = −48.5 → 재클램프                    │
  │  │   └─ 좌우 손가락이 평행하게 맞물림                                            │
  │  └──────────────────────────────────────────────────────────────────────────────┘
  │                                                                                  │
  │  ┌─ 실물 경로 🔒 ────────────────────────────────────────────────────────────────┐
  │  │   ├─gripperPercent = Clamp(0, 0, 100)                                        │
  │  │   ├─PincOpenSafety.TryApprove(0.0, out safePercent)                          │
  │  │   │   ├─RealGripperEnabled == false (기본값)                                  │
  │  │   │   │   → false + LastBlockReason 설정                                      │
  │  │   │   │   → Debug.LogWarning (최초 1회만, gripperBlockWarned)                 │
  │  │   │   │   → 🔒 전송 안 함                                                     │
  │  │   │   └─(잠금 해제 시) true, safePercent = 0.0                                │
  │  │   ├─PercentToServerValue(0) = 5×2−100 = −90.00  ※양 끝 5% 여유               │
  │  │   └─SendMotorCommand("robot1","gripper",−90.00)                              │
  │  └──────────────────────────────────────────────────────────────────────────────┘
```

> ⭐ **J6 각도 슬라이더 경로도 동일한 게이트를 통과한다.**
> `SOArmRealController.Update()` 송신 루프가 `joints[i].motorName == "gripper"` 를 검사해
> `TryApprove()` 실패 시 `continue` 로 건너뛴다. 2026-08-01 보강된 부분이다 (GAP_07).

---

# 7. Data Structure

## 7.1 `RecordProject` (JSON 루트)

| **필드** | **타입** | **설명** | **기본값** |
| --- | --- | --- | --- |
| `projectName` | string | 프로젝트 이름 | `"Untitled"` |
| `createdAt` | string | 생성 시각 (ISO 8601, `DateTime.Now.ToString("o")`) | 생성 시점 |
| `lastModifiedAt` | string | 마지막 수정 시각 | `createdAt` |
| `version` | string | 포맷 버전 (마이그레이션용) | `"1.0"` |
| `waypoints` | `List<Waypoint>` | 스텝 목록 | 빈 리스트 |

**메서드:** `NewProject(name)` · `RenumberSteps()` (1부터 재번호) · `Touch()` (수정 시각 갱신)

> 🔺 `RecordProject.cs` 의 한글 주석이 CP949 ↔ UTF-8 혼선으로 깨져 있다
> (`Record ������Ʈ ��ü�� ǥ��`). 코드 동작에는 영향 없음. UTF-8(BOM)로 재저장 권고.

## 7.2 `Waypoint` (스텝 1개)

| **필드** | **타입** | **적용 타입** | **설명** | **기본값** |
| --- | --- | --- | --- | --- |
| `stepNumber` | int | 공통 | 스텝 번호 (1부터) | — |
| `type` | string | 공통 | `motion` / `wait` / `loop_start` / `loop_end` | — |
| `name` | string | 공통 | 사용자 지정 이름 | 자동 생성 |
| `target` | string | motion | `robot1` / `robot2` / `both` | — |
| `joints` | float[6] | motion | 관절 각도. `both` 일 때는 robot1 | `new float[6]` |
| `gripper` | float | motion | 0~100 % | 50 |
| `joints2` | float[6] | motion(`both`) | robot2 의 관절 각도 | `new float[6]` |
| `gripper2` | float | motion(`both`) | robot2 의 그리퍼 % | 50 |
| `velocity` | int | motion | 이 스텝의 속도 | 800 |
| `acceleration` | int | motion | 가속도 | 50 |
| `delayAfter` | float | motion | 스텝 완료 후 대기(초) | 0.5 |
| `duration` | float | wait | 대기 시간(초) | 1.0 |
| `loopCount` | int | loop_start | 반복 횟수 | 1 |

**메서드:** `GetDisplayText()` — 타입별 축약 표시 문자열 반환

> ⚠️ `target == "robot2"` 단독일 때는 robot2 의 각도를 `joints2` 가 아니라 **`joints` 필드에 저장**한다
> (`AddMotionStepFromUI`). 재생 시 `ExecuteMotion` 도 같은 규칙으로 읽으므로 정합하다.

## 7.3 저장 경로 / 직렬화

| **항목** | **값** |
| --- | --- |
| 저장 폴더 | `Path.Combine(Application.dataPath, "..", "Recordings")` = `F:\UNITY\LeRobot\Recordings\` |
| 파일명 | `<프로젝트명>.json` (확장자 자동 부착) |
| 직렬화 | `JsonUtility.ToJson(project, prettyPrint: true)` |
| 역직렬화 | `JsonUtility.FromJson<RecordProject>(json)` → `RenumberSteps()` 로 안전 재정렬 |
| 현재 파일 | `Recordings\Untitled.json` (897 B) 존재 확인 |

## 7.4 `SOArmJointConfig` (관절 설정)

| **필드** | **타입** | **설명** | **기본값** |
| --- | --- | --- | --- |
| `displayName` | string | UI 표시용 이름 (예: `J2 (Shoulder Lift)`) | `"Joint"` |
| `motorName` | string | 서버에 보낼 모터 이름 | `"shoulder_pan"` |
| `minAngle` / `maxAngle` | float | 관절 가동범위 (deg) | −110 / 110 |
| `homeAngle` | float | 홈 각도 (deg) | 0 |
| `articulationBody` | `ArticulationBody` | 회전시킬 URDF 관절 | null |
| `invertSign` | bool | 시뮬–실물 회전 방향이 반대일 때 | false |
| `angleOffset` | float | 시뮬 기준점 보정 (deg) | 0 |

## 7.5 런타임 상태 배열 (`SOArmRealController`)

| **필드** | **타입** | **역할** | **초기값** |
| --- | --- | --- | --- |
| `targetAngles` | float[6] | 현재 목표 각도 | `joints[i].homeAngle` |
| `lastSentAngles` | float[6] | 마지막 전송 각도 (변화량 판정용) | **`float.NaN`** (첫 전송 강제) |
| `homePose` | float[6] | 홈 자세 캐시 | `joints[i].homeAngle` |
| `LastReadAngles` | `Dictionary<string,float>` | 최근 수신 각도 (motorName → deg) | 빈 딕셔너리 |
| `WritesEnabled` | bool | 실물 자세 채택 완료 여부 | `false` |

---

# 8. Architecture Decision Record (ADR)

| **ID** | **결정** | **대안** | **근거** | **대가 / 한계** |
| --- | --- | --- | --- | --- |
| ADR_01 | **언어 경계를 프로세스 경계로 만든다 (TCP + NDJSON)** | ① Python.NET 임베딩 ② gRPC ③ ROS2 브리지 | Unity는 C#만, LeRobot SDK는 Python 전용이라 한 프로세스 불가. Python.NET은 ARM 빌드 리스크, gRPC는 스키마 컴파일 단계 추가, ROS2는 이 규모에 과함. TCP+JSON은 `nc` 로 손으로 찔러볼 수 있어 디버깅이 쉬움 | 스키마 검증이 없어 오타가 런타임에만 드러남. 실제로 `{value:F2}` 보간 사고 발생 → `InvariantCulture` 로 대응 |
| ADR_02 | **`so101.urdf` 를 기구학의 단일 진실 공급원(SSoT)으로 삼는다** | Unity 인스펙터에 장착 오프셋 보관 | 두 곳에 있으면 반드시 어긋남. `applyMountOffset` 기본값 `false`, 씬 저장값도 `0` | 켠 채로 값이 0이면 그리퍼가 손목 원점으로 튐 → 코드·URDF·문서 3곳에 경고 명시 |
| ADR_03 | **제어 모드를 2개로 줄이고 나머지를 직교 축으로 분리** | 5개 enum 유지 | `Robot1Only` 는 "제어 방식"이 아니라 "어느 팔을 쓰나". 다른 축을 같은 enum에 섞으면 조합 폭발 | `Cooperative` 소멸 → SR_19 미착수로 회귀. `PROJECT_NOTES.md` 갱신 필요 (GAP_05) |
| ADR_04 | **Sim / Real / Manager 가 모두 같은 인터페이스를 구현** | Manager를 별도 타입으로 | `SOArmManager` 가 인터페이스를 구현하면서 내부에 구현체 2개를 갖는 **Composite** 구조. UI는 "1대"인지 "합성체"인지 몰라도 됨 | 읽기 출처는 `PrimaryReader` 가 모드로 결정: `RealOnly`→real / `Mirror` && real 연결됨→real / 그 외→sim |
| ADR_05 | **소켓은 씬 전체에 하나만 둔다** | 로봇마다 소켓 1개 | 서버가 1 프로세스 / 1 포트에서 2대를 모두 관리하므로 연결도 하나면 충분. `mode` 필드가 라우팅 담당 | `SmartFactoryUI.SendToServer()` 가 항상 `robot1.real.socketClient` 를 씀. 소켓이 하나라 기능은 정상이나 **robot1이 null이면 robot2 명령까지 실패** (TD_02) |
| ADR_06 | **폐루프 구속은 스크립트로 대신 계산한다** | ① ROS2 `mimic` 태그 ② PhysX 관절 구속 | Unity URDF Importer가 `mimic` 을 반영하지 않고, PhysX 폐루프는 `ArticulationBody` 트리 제약과 충돌 | `LateUpdate()` 마다 3개 종동축에 복사 — 프레임당 고정 비용 |
| ADR_07 | **그리퍼 실물 명령은 기본 잠금(fail-safe)** | 기본 허용 + 사후 경고 | STS3215는 위치 제어 모드에서 토크 제한이 없어 물체를 물면 모터가 타거나 플라스틱이 부러짐 | 설정을 안 하면 위험한 게 아니라, 설정을 안 하면 **아무것도 안 나간다** |
| ADR_08 | **마이그레이션은 "부분 교체 + 멱등"으로** | 로봇 통째 재임포트 | 통째로 갈면 `SOArmManager`·`SocketClient`·UI 의 인스펙터 연결이 전부 끊어짐 | `xDrive` 는 코드로 넣어도 씬 파일에 안 남음(네이티브만 변경) → `SerializedObject` 로 `m_XDrive.stiffness` 직접 기록 |
| ADR_09 | **실물 자세 우선 (쓰기 보류)** | Play 즉시 시뮬 자세 전송 | 디지털 트윈의 진실은 실물이다. Play 직후 시뮬 홈(0°)을 보내면 실물이 끌려감 | 실물 미연결 시 `holdWritesUntilSynced` 를 꺼야 조작 가능 |
| ADR_10 | 🔜 **향후 제안 — HAL(하드웨어 추상화 계층) 분리** | 현행 유지 | `SOArmRealController` 안에 ① 상태 관리 ② 전송 정책 ③ **프로토콜 지식** 이 섞여 있어, 통신 방식을 바꾸면 컨트롤러를 통째로 고쳐야 함 | **미적용.** `IRobotTransport` 인터페이스 도입 시 `TcpJsonTransport` / `SerialTransport` / `Ros2Transport` / `MockTransport` 교체 가능 |

---

# 9. Technical Debt

> 우선순위: 🔴 안전/기능 영향 → 🟡 유지보수 영향 → 🟢 정리 수준

| **ID** | **심각도** | **항목** | **위치** | **상세** | **권고 조치** |
| --- | --- | --- | --- | --- | --- |
| TD_01 | 🔴 | **비상 정지가 아무것도 안 한다** | `SOArmRealController.StopMotion()`<br>`SOArmSimController.StopMotion()` | Real 쪽은 `Debug.Log` 한 줄, Sim 쪽은 빈 메서드. UI 「⏸ 정지」·「⏸ 전체 정지」 버튼이 실효 없음 | 서버에 `{"type":"stop"}` 추가 + 현재 위치를 목표로 고정(freeze) |
| TD_02 | 🔴 | **UI의 서버 명령이 항상 robot1의 소켓으로 나간다** | `SmartFactoryUI_v3_4.SendToServer()` L295<br>`SendSetHome()` L271 | `dualManager.robot1?.real` 하드코딩. `SendSetHome("robot2")` 도 robot1 유무를 검사. 소켓이 씬에 1개뿐이라 기능은 동작하지만, robot1 부재 시 robot2 명령까지 실패 | 소켓 참조를 `SOArmDualManager` 로 올리거나 `robotName` 에 맞는 매니저를 찾아 쓰도록 수정 |
| TD_03 | 🟡 | **응답 매칭이 FIFO 가정에 의존** | `SOArmSocketClient.pendingCallbacks` | 요청에 ID가 없다. `SendMotorCommand` 는 콜백을 등록하지 않는데 서버가 확인 응답을 보내면 뒤따르는 `get` 콜백에 잘못 매칭될 수 있다. **완화책은 있음** — 대기 콜백 16개 초과 시 전량 폐기해 정렬 복구 | 요청/응답에 `id` 필드 추가, 또는 서버 응답 정책 통일 |
| TD_04 | 🟡 | **`SOArmPresets` 관절 범위가 URDF와 불일치** | `SOArmPresets.cs` L16~40 | J1~J5가 전부 `±110°`. 씬 직렬화 값과 다름. 씬에 값이 있으면 프리셋은 안 쓰이므로 현재 무해하나, 새 로봇 추가 시 잘못된 범위 유입. J6은 `PincOpenCoupling` 상수를 참조해 정상 | J1~J5를 URDF 값으로 수정 |
| TD_05 | 🟡 | **`RecordManager.CaptureJoints()` 가 죽은 코드** | `RecordManager.cs` L384~404 | 항상 `0`(관절)과 `50f`(그리퍼)를 반환. 주석에도 "실제 캡처는 UI에서 슬라이더 값을 전달받는 방식이 더 정확" 이라 적혀 있고, 실제 경로는 `AddMotionStepFromUI()` 다. `AddMotionStep()` 을 호출하면 **전부 0인 스텝**이 저장된다 | `AddMotionStep()` / `CaptureJoints()` / `CaptureGripper()` 삭제 |
| TD_06 | 🟡 | **URDF 주석이 최신 값과 다름 (stale)** | `so101.urdf` L325~330, L348 | L325~330 은 "임시값 … limit ±1.25 rad — 커플링 배율 미확정" 이나 실제 리밋은 `-0.8465 ~ 0` 확정. L348 은 `(-69.9° ~ 0°)` 로 적혀 있으나 바로 다음 줄이 `0.8465 rad = 48.5°` | 주석 갱신 (GAP_02, GAP_03) |
| TD_07 | 🟡 | **문서가 존재하지 않는 파일을 참조** | `PincOpenSafety.cs` L43·L81<br>`PINCOPEN_INTEGRATION.md` L4·L110·L184<br>`so101.urdf` L335 | `docs/PINCOPEN.md` 를 5곳에서 참조하지만 파일이 없다. **실물 그리퍼 안전 절차의 원본**이라 공백이 위험 | 복구 또는 재작성 |
| TD_08 | 🟢 | **`RecordProject.cs` 주석 인코딩 깨짐** | `RecordProject.cs` 전체 | 한글 주석이 `Record ������Ʈ ��ü�� ǥ��` 처럼 깨짐 (CP949 ↔ UTF-8). 코드 동작에는 영향 없음 | UTF-8(BOM 포함)로 재저장 |
| TD_09 | 🟢 | **라즈베리파이 IP가 3곳에서 불일치** | `PROJECT_NOTES.md` / `SOArmSocketClient.cs` 기본값 / 씬 | `192.168.75.245` vs `192.168.45.18`. 씬 실제값은 `192.168.75.245` | 설정 파일 1곳으로 일원화, 또는 mDNS 사용 |
| TD_10 | 🟢 | **서버 소스가 저장소 밖에 있음** | — | Unity 저장소 전체에 `.py` 파일 0개. 서버 동작을 코드로 검증할 수 없어 이 문서의 서버 측 서술이 전부 ⚠️ 미확인 | `robot_server_dual.py` 를 저장소에 포함 |
| TD_11 | 🟢 | **OnGUI 즉시 모드 UI의 한계** | `SmartFactoryUI_v3_4`, `SmartFactoryRecordUI` | 좌표를 픽셀 상수로 직접 계산 (`GUI.Button(new Rect(x + dx*3 + 82, y, 78, h), …)`). 항목 추가 시마다 좌표 재계산 필요, 매 프레임 GC 할당 발생 | UI Toolkit(UXML/USS) 또는 uGUI 전환 |

---

# 10. ⚠️ 미확인 항목

| **항목** | **왜 확인 못 했나** |
| --- | --- |
| `robot_server_dual.py` 의 실제 동작 | 저장소에 소스 없음 (TD_10) |
| 서버가 `get`/`set_home`/`torque`/`set_speed`/`home` 을 처리하는지 | 위와 동일 |
| `mirror` 모드를 서버가 어떻게 처리하는지 | 프로토콜 명세에는 있으나 서버 구현 미확인 |
| 서버의 각도 응답 JSON 실제 형식 | Unity 파서가 기대하는 형식만 확정 |
| 서버가 모터 명령 후 확인 응답을 보내는지 | 보낸다면 TD_03 의 큐 어긋남 위험 |
| `set_home` 다이얼로그가 안내하는 "캘리브 파일 자동 백업 / autocorrect / 자동 복구" | UI 문구에만 존재 |
| 양방향 동기화(SR_06)의 **실물 검증** | 코드 경로는 완결됐으나 실행 기록 없음 |
| 손목 카메라 스트리밍 | 미구현 (SR_18) |

---

# 11. 관련 문서

| **문서** | **내용** |
| --- | --- |
| `docs/v2/USER_REQUIREMENT.md` | UR / SR / Scenario / Validation / Constraints |
| `docs/v2/HW_ARCHITECTURE.md` | 물리 구성, 기구 치수, 모터 사양, 전원·배선, 안전 한계 |
| `docs/PINCOPEN_INTEGRATION.md` | PincOpen 통합 확정 기록 (🔺 §7 수치는 GAP_01 참조) |
| `docs/PINCOPEN.md` | ⚠️ **부재** (TD_07) — 실물 그리퍼 안전 절차 원본 |
