# Smart Factory: Dual SO-ARM101

SO-ARM101 6축 로봇팔 두 대를 Unity 디지털 트윈과 실시간 동기화 제어하는 분산 시스템입니다.
Hugging Face LeRobot 프레임워크 기반이며, Unity(Windows)와 라즈베리파이 서버가
직접 설계한 TCP/JSON 프로토콜로 통신합니다.

| 항목 | 사양 |
|---|---|
| 로봇 | SO-ARM101 (3D 프린팅, 6-DOF) × 2대 |
| 모터 | Feetech STS3215 × 12개 (12V, 1:345 감속) |
| 그리퍼 | **PincOpen** (Pollen Robotics, 평행 4절링크) × 2 |
| 컨트롤러 보드 | Waveshare Bus Servo Adapter (A) × 2 |
| 메인 컴퓨터 | Raspberry Pi 4 (4GB, Ubuntu 24.04) |
| Unity 호스트 | Windows PC / Unity 6000.4.3f1 |
| 통신 | TCP Socket (JSON, Port 5000) |

---

## 기능

**제어 모드** — "어떻게 제어하나"와 "어느 팔을 쓰나"를 분리했습니다.

| 축 | 값 |
|---|---|
| 제어 방식 | `Independent` (독립) / `Mirror` (동시 동작) |
| 채널 on/off | R1 사용 / R2 사용 (각각 토글) |
| 녹화 | 위 두 축과 **직교** — 어느 모드에서든 켤 수 있음 |

> 이전 버전은 `Robot1Only`/`Robot2Only`가 제어 방식과 연결 대상을 겸했습니다.
> "미러로 한 대만 쓰기" 같은 조합이 표현되지 않아 축을 나눴습니다.

**수동 모드 (직접교시)** — 손으로 팔을 끌어다 자세를 가르칩니다.

- `1번 수동` / `2번 수동` — 해당 로봇의 토크를 풀어 손으로 밀 수 있게 함
- `미러 수동` — **1번을 손으로 가르치면 2번이 실시간으로 따라 합니다**

**모션 녹화 / 재생** — 가르친 동작을 기억했다가 되돌려 줍니다.

- 실물 폴링 각도를 시각과 함께 기록 → 원래 속도로 재생
- 재생 속도 0.1~1.5배, 반복재생, JSON 저장/불러오기

**Sim ↔ Real 양방향 동기화**

- 실로봇 각도를 30 Hz로 폴링해 Unity 모델에 반영
- Play 직후에는 **실물이 진실** — 실물 자세를 읽기 전까지 명령을 내보내지 않음

**안전장치**

- 비상정지 (ESC 키 / UI 버튼) — 두 로봇 모두 현재 자세로 고정
- 속도 제한 (관절 40°/s, 그리퍼 60%/s) — 슬라이더를 확 움직여도 급가속하지 않음
- 소프트 리밋 — 정규화 범위와 분리된 별도 필드
- 그리퍼 펌웨어 위치 리밋 — 소프트웨어가 틀려도 모터가 차단

---

## 구조

```
Unity UI → DualManager → Manager A/B ─┬→ SimController  (ArticulationBody)
                                      └→ RealController → TCP/JSON
                                                            ↓
                                      Pi Server → LeRobot SDK → SO-ARM101 A/B
```

**Unity Client** — `SmartFactoryUI_v3_4`가 입력을 받아 `SOArmDualManager`로 넘기면,
매니저가 제어 모드에 따라 두 `SOArmManager`로 라우팅합니다.
각 매니저는 Sim(Unity)과 Real(실로봇)을 함께 제어하고, 실물에서 올라온 각도를 Sim에 되먹입니다.

**Network Bridge** — TCP 소켓 위 JSON. 한 줄에 한 메시지(`\n` 구분).

**Raspberry Pi Server** — `robot_server_dual.py`가 LeRobot의 `FeetechMotorsBus`로
두 대를 제어합니다. 모터는 USB `serial-by-id` 경로로 식별해 재연결에도 흔들리지 않습니다.

---

## 파일 구성

**Unity** (`Assets/Script/`)

| 파일 | 역할 |
|---|---|
| `SOArmJointConfig.cs` / `SOArmPresets.cs` | 관절 설정·프리셋 |
| `ISOArmController.cs` | Sim/Real 공통 인터페이스 |
| `SOArmMotorMapper.cs` | 각도 ↔ 서버값(-100~100) 변환 |
| `SOArmSocketClient.cs` | TCP 소켓 + 응답 콜백 큐 |
| `SOArmSimController.cs` / `SOArmRealController.cs` | 시뮬 / 실로봇 제어 |
| `SOArmManager.cs` / `SOArmDualManager.cs` | 단일 / 이중 로봇 통합 |
| `SOArmMotionRecorder.cs` | 직접교시 녹화·재생 |
| `PincOpenCoupling.cs` / `PincOpenSafety.cs` | 그리퍼 4절링크 연동·안전 |
| `SmartFactoryUI_v3_4.cs` | UI |

**에디터 도구** (`Assets/Editor/`) — 그리퍼 이식·검증 자동화

`PincOpenSetupMenu` / `PincOpenCapture`(헤드리스 렌더·스윕·기하 리포트) /
`PincOpenMainSceneMigrator`(배선 보존 서브트리 이식) / `JointLimitFixer` / `GripperInvertFixer`

**라즈베리파이** (`raspberry_pi/`)

| 파일 | 역할 |
|---|---|
| `arm_set_home.py` | 팔 관절 0점 재정의 |
| `teach_torque.py` | 직접교시용 토크 조절 |
| `pincopen_apply_manual.py` | 눈으로 확인한 그리퍼 양 끝을 캘리브+펌웨어에 기록 |
| `pincopen_calibrate_gripper.py` / `pincopen_find_limits.py` | 그리퍼 가동범위 탐색 |

**문서** (`docs/`) — 요구사항 / HW·SW 아키텍처 / PincOpen 이식기, `docs/v2/`는 Confluence 형식

---

## 통신 프로토콜

한 줄 = 한 JSON 메시지.

| `type` | 용도 | 필드 |
|---|---|---|
| *(없음)* | 모터 이동 | `mode`, `motor`, `value` (-100~100) |
| `get` | 현재 각도 조회 | `mode` |
| `torque` | 토크 ON/OFF | `mode`, `enable` |
| `teach` | **수동 모드** | `mode`, `enable` |
| `set_speed` | 속도/가속도 | `mode`, `velocity`, `acceleration` |
| `home` | 홈 이동 | `mode` |
| `set_home` | 현재 자세를 새 0점으로 | `mode`, `confirm` |

`mode`: `robot1` / `robot2` / `both` / `mirror`
`motor`: `shoulder_pan` / `shoulder_lift` / `elbow_flex` / `wrist_flex` / `wrist_roll` / `gripper`

---

## 실행

**라즈베리파이 (서버)**

```bash
source ~/lerobot-env/bin/activate
pkill -9 -f robot_server_dual.py     # ⚠️ -9 여야 함. 아래 참고
python robot_server_dual.py
```

> ⚠️ `pkill`(SIGTERM)은 서버의 종료 핸들러를 깨워 `disable_torque()`를 호출합니다.
> 그러면 **12V 팔이 중력으로 주저앉습니다.** 반드시 `-9`로 죽여 토크를 유지하세요.

**Unity (클라이언트)**

1. URDF Importer 설치 → `Assets/SO101_unity/so101.urdf` 임포트
2. `Robot1_Group` / `Robot2_Group` 구성 후 Play
3. Player Settings의 **Run In Background 를 반드시 켤 것**
   (꺼져 있으면 창이 비활성일 때 Editor가 멈춰 통신이 끊긴 것처럼 보입니다)

---

## 직접교시 사용법

```
1. 수동 모드 ON   →  회전축·손목 토크가 풀립니다
2. 🔴 녹화
3. 손으로 동작을 보여줍니다
4. ⏹ 정지  →  수동 모드 OFF (토크 복구)
5. ▶ 재생
```

**왜 토크를 "낮추는" 게 아니라 "끄는" 가**

STS3215는 **1:345 감속기**라 역구동이 구조적으로 안 됩니다.
`Torque_Limit`을 500 → 192(38%)까지 내려도 손으로 꺾이지 않았습니다.
모터가 힘을 안 줘도 기어 마찰 자체가 남기 때문입니다. **끄는 것 말고는 방법이 없습니다.**

**그런데 다 끄면 팔이 주저앉습니다.** 그래서 중력 모멘트가 없는 관절만 끕니다.

| 관절 | 수동 모드 | 이유 |
|---|---|---|
| `shoulder_pan` | 🔓 토크 OFF | 수직축 회전 → 중력 토크 0 |
| `wrist_roll` | 🔓 토크 OFF | 툴 축 회전 → 중력 토크 ≈ 0 |
| `wrist_flex` | 🔓 토크 OFF | 그리퍼만 듦 |
| `shoulder_lift` | 🔒 유지 | **팔 전체 무게를 듦** |
| `elbow_flex` | 🔒 유지 | 전완 + 그리퍼 |

유니티 쪽 `teachMode`도 함께 켜집니다. 안 켜면 옛 목표가 계속 나가서 손을 뗀 순간 제자리로 튕겨 돌아갑니다.

---

## 캘리브레이션 노트

**LeRobot 0점 ≠ URDF 0점.** 이 차이가 "시뮬과 실물이 안 맞는" 문제의 근원이었습니다.
LeRobot의 `RANGE_M100_100`에서 norm 0은 **항상 가동범위의 중앙**입니다.

```
norm = ((raw − range_min) / (range_max − range_min)) × 200 − 100
```

`Present_Position = Actual − Homing_Offset` 은 **모터 펌웨어가** 적용합니다.
따라서 0점을 옮기려면 `Homing_Offset`을 쓰고, **직후에 `Goal_Position`을 현재 위치로
다시 써야 합니다.** 안 그러면 모터가 옛 목표를 새 좌표계로 재해석해 스스로 움직입니다.

**그리퍼**는 자동 탐색이 실패했습니다. STS3215의 **약 20틱 불감대**를 스톨 판정이
기계적 스토퍼로 오인해, 캠이 최대 닫힘을 지나 되벌어지는 구간까지 범위에 넣었습니다.
결국 눈으로 확인한 양 끝을 수동으로 기록하는 방식(`pincopen_apply_manual.py`)으로 갔습니다.

---

## 트러블슈팅

| 이슈 | 원인 | 해결 |
|---|---|---|
| 통신이 멈춤 / 트래픽 0 | **Run In Background 꺼짐** — 창이 비활성이면 Editor가 정지 | Player Settings에서 켜기 |
| 폴링이 영구 정지 | 각도 응답 유실 시 대기 타임아웃 없음 | 1초 타임아웃 + 재요청 |
| Editor 프리징 | `Debug.Log`가 초당 360회 | 로그를 플래그 뒤로 |
| 스크립트 종료만 해도 팔이 처짐 | `bus.disconnect()`가 `disable_torque()` 호출 | `bus.port_handler.closePort()` 사용 |
| 리밋을 좁혔더니 전 각도가 어긋남 | `maxAngle`이 정규화 스케일도 겸함 | 소프트 리밋 필드 분리 |
| 그리퍼가 닫혔다 다시 열림 | 두 곳에서 명령을 씀 | 전송 경로를 하나로 통합 |
| `Present_Load` 과부하 오판 | 10비트 **2의 보수** (964 = −60) | `if v > 511: v -= 1024` |
| URDF NullReferenceException | STL Convex Mesh 생성 실패 | STL → DAE + Collision 주석 |
| 모델이 분홍색 | URP 머티리얼 미적용 | Render Pipeline → URP 변환 |
| 임포트 후 관절이 안 움직임 | `stiffness = 0` | `ConfigureDrives()` |
| Address already in use | 이전 서버 잔존 | `pkill -9 -f robot_server_dual.py` |
| PyTorch Illegal Instruction | ARM 비호환 빌드 | ARM CPU 빌드 사용 |

---

## 진행 상태

- [x] 기본 제어 (독립 / 미러)
- [x] Sim ↔ Real 양방향 동기화
- [x] PincOpen 그리퍼 이식 + 실측 캘리브레이션
- [x] 비상정지 · 속도 제한 · 소프트 리밋
- [x] 관절 0점 재정의 (`set_home`)
- [x] 수동 모드 (직접교시)
- [x] 모션 녹화 / 재생 (JSON)
- [ ] 협동 작업 (Pick & Place 전달)
- [ ] 작업 큐 관리 UI
- [ ] 카메라 비전 통합

**알려진 이슈**

- 로봇2 그리퍼 행정이 112°로 로봇1(154°)보다 짧습니다 — 서보 혼 스플라인 또는 4절링크 조립 차이로 추정
- 로봇2 `wrist_flex` 0점에 약 10.5° 잔차

---

## 참고 자료

- [SO-ARM100/101](https://github.com/TheRobotStudio/SO-ARM100)
- [LeRobot (Hugging Face)](https://github.com/huggingface/lerobot)
- [PincOpen (Pollen Robotics)](https://github.com/pollen-robotics/PincOpen)
- [Unity URDF Importer](https://github.com/Unity-Technologies/URDF-Importer)

인터페이스 설계는 자매 프로젝트 [Fairino FR5 Digital Twin](https://github.com/kimar1022-code/fairino-fr5-digital-twin)을 참고했습니다.

## 라이선스

코드는 MIT License를 따릅니다.
SO-ARM100/101 하드웨어는 Apache 2.0 (TheRobotStudio), LeRobot은 Apache 2.0 (Hugging Face),
PincOpen은 Apache 2.0 (Pollen Robotics).
