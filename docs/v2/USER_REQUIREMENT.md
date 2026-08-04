# User Requirement

| **과정명** | **Unity 활용 DT 로봇 분야 개발자 양성과정 1기** |
| --- | --- |
| **프로젝트명** | SO-ARM101 스마트팩토리 로봇팔 디지털 트윈 | **문서 버전** | v2.0 |
| **팀명** | SO-ARM101 (개인 프로젝트) | **작성일** | 2026-08-01 |
| **작성자** | 김애리 | **최종 수정** | 2026-08-01 |
| **문서 종류** | User Requirement / System Requirement / Scenario / Validation |

---

## 변경 이력

```
8/1 - v2.0 최초 작성 (Confluence 팀 문서 체계 적용)
8/1 - UR/SR ID 체계 신규 부여, 기존 REQUIREMENTS.md 의 FR/NFR 을 SR 로 재편
8/1 - Validation 체크리스트 신설 (코드 실측 기준)
8/1 - PincOpen 손가락 가동범위 -69.9° → -48.5° 로 정정 (URDF/씬 실측 반영)
```

## 범례

> * ✅ 구현 + 코드 경로 검증 완료
> * 🟡 코드 구현 확인됨, **실행/실물 검증 기록 없음** → 실기 테스트 필요
> * ⬜ 미구현
> * 🔺 **문서와 실제 구현이 불일치** — 문서 갱신 또는 구현 정렬 필요
> * ⚠️ 미확인 — 확인하지 못함. 추측으로 채우지 않았음

---

# 1. Project Information

**프로젝트명 :** SO-ARM101 스마트팩토리 로봇팔 디지털 트윈

**프로젝트 주제 :** Unity 기반 6축 로봇팔 2대의 실시간 동기화 제어 및 동작 티칭 시스템

**프로젝트 추진 배경 :**

"실물 로봇을 만지지 않고도 검증할 수 있는 디지털 트윈 제어 환경"

* 스마트팩토리 산업 확대에 따른 협동로봇·소형 매니퓰레이터 수요 증가
* 실물 로봇 시험 시 파손·안전 사고 위험이 크고, 재현이 어려움
* 시뮬레이션과 실물이 어긋나면 검증 자체가 무의미해지므로 **양방향 동기화**가 필수
* 반복 작업(pick & place)을 코드 없이 저장·재생할 수 있는 **간이 티칭펜던트** 수요
* Hugging Face LeRobot 생태계 확산으로 저가 로봇팔의 SW 제어 진입장벽 하락
* 펌웨어 ~ 애플리케이션 전 계층을 관통하는 시스템 엔지니어링 역량 확보 필요

**시스템 범위 :**

| 구분 | 내용 |
| --- | --- |
| 대상 로봇 | SO-ARM101 6-DOF 로봇팔 **2대** (Feetech STS3215 × 12) |
| 시각화/조작 | Unity 6.4 (Windows PC) — `F:\UNITY\LeRobot` |
| 제어 서버 | Raspberry Pi 4 / Ubuntu 24.04 — `robot_server_dual.py` |
| 통신 | TCP Socket 포트 5000, NDJSON |
| 엔드이펙터 | PincOpen 평행 4절 링크 그리퍼 (Pollen Robotics, CC BY-SA 4.0) |

---

# 2. User Requirement

| **UR ID** | **UR Description** | **Priority** | **Reporter** |
| --- | --- | --- | --- |
| UR_01 | 사용자는 실물 로봇 없이 화면 속 로봇팔을 조작할 수 있어야 한다. | R | 개발자 |
| UR_02 | 사용자는 화면에서 조작한 대로 실물 로봇팔이 움직이게 할 수 있어야 한다. | R | 개발자 |
| UR_03 | 화면 속 로봇은 실물 로봇의 현재 자세를 실시간으로 따라가야 한다. | R | 개발자 |
| UR_04 | 사용자는 로봇 2대를 동시에 또는 따로따로 제어할 수 있어야 한다. | R | 개발자 |
| UR_05 | 사용자는 로봇의 동작을 순서대로 저장하고 반복 재생할 수 있어야 한다. | R | 조작자 |
| UR_06 | 사용자는 그리퍼를 열고 닫아 물체를 집을 수 있어야 한다. | R | 조작자 |
| UR_07 | 로봇은 기계적 한계를 넘는 명령을 받지 않아야 한다. | R | 하드웨어 |
| UR_08 | 사용자는 현재 자세를 새 기준점(홈)으로 지정할 수 있어야 한다. | O | 조작자 |
| UR_09 | 사용자는 로봇의 이동 속도를 상황에 맞게 바꿀 수 있어야 한다. | O | 조작자 |
| UR_10 | 사용자는 이상 상황에서 로봇을 즉시 정지시킬 수 있어야 한다. | R | 조작자 |
| UR_11 | 사용자는 코드를 몰라도 GUI만으로 전체 기능을 쓸 수 있어야 한다. | R | 조작자 |
| UR_12 | 사용자는 로봇 손목 카메라 영상을 화면에서 볼 수 있어야 한다. | O | 조작자 |
| UR_13 | 사용자는 저장된 루틴 여러 개를 순서대로 묶어 연속으로 실행할 수 있어야 한다. | O | 조작자 |

> Priority — **R** (Required, 필수) / **O** (Optional, 선택)

---

# 3. System Requirement

| **SR ID** | **SR Name** | **Description** | **Priority** | **UR** |
| --- | --- | --- | --- | --- |
| SR_01 | 시뮬레이션 관절 제어 기능 | 시스템은 6개 관절을 개별 각도로 제어한다. 슬라이더 조작 / 스텝 버튼(−, 0°, +) 조작 / 스텝 각도 변경(0.5° · 1° · 5° · 10° · 직접 입력). 지정 각도를 Unity `ArticulationBody.xDrive.target` 에 반영한다. 실물 로봇이 없어도 단독 동작한다. | R | UR_01 |
| SR_02 | 관절 범위 제한 기능 | 시스템은 관절별 최소/최대 각도를 벗어나는 명령을 잘라낸다. 클램프 지점 3중: 시뮬 컨트롤러 · 실로봇 컨트롤러 · 정규화 변환기. 관절별 부호 반전(`invertSign`)과 기준점 보정(`angleOffset`)을 지원한다. | R | UR_07 |
| SR_03 | 실로봇 통신 기능 | 시스템은 TCP 소켓으로 라즈베리파이 서버와 통신한다. 접속 / 해제 / 자동 재접속(2초 주기). 명령은 한 줄 JSON(개행 구분)으로 송신한다. 각도는 −100~100 정규화값으로 변환해 보낸다. | R | UR_02 |
| SR_04 | 전송률 제한 기능 | 시스템은 실로봇 명령 전송량에 상한을 둔다. 전송 주기 10 Hz. 변화량 0.5° 이하는 미전송. 서버 JSON 파서 보호가 목적이다. | R | UR_02 |
| SR_05 | 실로봇 상태 폴링 기능 | 시스템은 실로봇의 현재 각도를 주기적으로 읽는다. 폴링 30 Hz. 동시 요청 1건 제한(in-flight 1). 응답 타임아웃 1초 — 초과 시 포기하고 재요청. | R | UR_03 |
| SR_06 | 양방향 동기화 기능 | 시스템은 읽어온 실물 각도를 시뮬 관절에 반영한다(디지털 트윈). Play 시작 후 1회 초기 동기화를 수행한다. **첫 수신 전까지 쓰기를 보류**해 실물이 시뮬 홈 자세로 끌려가지 않게 한다. | R | UR_03 |
| SR_07 | 2대 통합 제어 기능 | 시스템은 로봇 2대를 통합 관리한다. 제어 방식: Independent(독립) / Mirror(미러). 채널 on/off: R1 사용 / R2 사용. 제어 방식과 채널은 서로 독립된 축이다. | R | UR_04 |
| SR_08 | 동작 녹화 기능 | 시스템은 현재 자세를 스텝으로 저장한다. 스텝 대상: robot1 / robot2 / both. 스텝 종류: motion / wait(대기) / loop_start(반복 시작) / loop_end(반복 끝). 순서 변경 · 이름 변경 · 삭제가 가능하다. | R | UR_05 |
| SR_09 | 동작 재생 기능 | 시스템은 저장된 시퀀스를 순차 재생한다. 반복 구간은 스택으로 중첩 처리한다. 재생 중인 스텝을 UI에서 하이라이트한다. 재생 중 정지가 가능하다. | R | UR_05 |
| SR_10 | 프로젝트 저장/불러오기 기능 | 시스템은 녹화 프로젝트를 JSON 파일로 저장·복원한다. 저장 위치: `<프로젝트루트>/Recordings/*.json`. 불러오기 시 스텝 번호를 재정렬한다. | R | UR_05 |
| SR_11 | 그리퍼 개폐 기능 | 시스템은 그리퍼를 0~100 %로 여닫는다. 0 %=닫힘, 100 %=열림. 평행 4절 링크의 종동축 3개가 구동축을 따라 움직인다. 배율 ×1.0, 부호 (−1, −1, +1). | R | UR_06 |
| SR_12 | 그리퍼 실물 명령 차단 기능 | 시스템은 검증되지 않은 상태에서 그리퍼 실물 명령을 차단한다. 기본값 잠금. 차단 사유를 로그로 1회 출력한다. 잠금 해제는 4단계 절차 완료 후에만 수동으로 한다. | R | UR_07 |
| SR_13 | 홈포즈 관리 기능 | 시스템은 홈 자세를 관리한다. 홈으로 이동 명령 전송. 현재 자세를 새 0점으로 저장(`set_home`). 되돌리기 어려운 조작이므로 확인 다이얼로그를 거친다. | O | UR_08 |
| SR_14 | 속도/가속도 설정 기능 | 시스템은 모터 속도·가속도를 서버에 적용한다. 속도 0~3000 / 가속도 1~254. 프리셋: 정밀(400/30) · 일반(800/50) · 빠름(1500/100). 0.5초 이내 중복 전송을 차단한다. | O | UR_09 |
| SR_15 | 비상 정지 기능 | 시스템은 이상 상황에서 로봇을 즉시 정지시킨다. 로봇별 정지 / 전체 정지. 정지 시 현재 위치를 유지한다. | R | UR_10 |
| SR_16 | GUI 제공 기능 | 시스템은 코드 없이 조작 가능한 화면을 제공한다. 제어 모드 표시 · 관절 슬라이더 · 그리퍼 슬라이더 · 실물 각도 병기 · 하단 전체 제어. 화면 크기에 따라 패널 높이를 자동 조정한다. | R | UR_11 |
| SR_17 | 서보 토크 제어 기능 | 시스템은 서보 토크를 ON/OFF 할 수 있다. 12 V 인가 상태에서 토크를 끄면 중력으로 팔이 낙하하므로 사용에 주의한다. | O | UR_07 |
| SR_18 | 카메라 영상 표시 기능 | 시스템은 로봇 손목 카메라 영상을 화면에 표시한다. | O | UR_12 |
| SR_19 | 협동 작업 기능 | 두 로봇이 역할을 나누어 하나의 작업을 수행한다. | O | UR_04 |
| SR_20 | 역기구학(IK) 제어 기능 | 사용자가 XYZ 좌표를 지정하면 시스템이 관절 각도를 역산한다. | O | UR_01 |
| SR_21 | 작업 큐 관리 기능 | 시스템은 저장된 루틴을 순서대로 줄 세워 연속 실행한다. 작업 1개는 `Recordings/*.json` 루틴 1개다. 항목별 반복 횟수·사용 여부·실행 상태(대기/진행/완료/실패/건너뜀)를 관리한다. 항목 추가·삭제·순서 변경이 가능하다. 다음 항목은 이전 재생이 실제로 끝난 뒤에 시작한다. 큐는 `Recordings/Queues/*.json` 에 저장한다. 상세 명세는 `docs/TASK_QUEUE.md`. | O | UR_13 |

---

# 4. Scenario

---

## **Scenario #1 — 시뮬레이션 단독 조작**

1. **사용자가 Unity 에디터에서 Play 를 누른다.**
2. **화면 좌측에 Robot 1 / Robot 2 제어 패널이 표시된다.**
3. **사용자가 J1~J6 슬라이더를 조작한다.**
   * a. 슬라이더를 직접 드래그한다.
   * b. 또는 −/0°/+ 버튼으로 스텝 단위 이동한다.
   * c. 스텝 각도는 0.5° / 1° / 5° / 10° 또는 직접 입력으로 바꾼다.
4. **화면 속 로봇 모델이 즉시 해당 자세로 움직인다.**
5. **관절 한계를 넘는 값은 자동으로 잘린다.**
6. **라즈베리파이 서버가 꺼져 있어도 위 과정이 모두 동작한다.**

---

## **Scenario #2 — 실로봇 원격 조작**

1. **라즈베리파이에서 `robot_server_dual.py` 를 실행하고 12 V 전원을 인가한다.**
2. **Unity 에서 Play 를 누르면 소켓이 자동 접속한다(`connectOnStart`).**
   * a. 접속 실패 시 2초 주기로 자동 재시도한다.
3. **시스템은 실물의 현재 자세를 1회 읽어 목표값으로 채택한다.**
   * a. 채택 전까지 **쓰기는 보류된다** — 실물이 시뮬 홈(0°)으로 끌려가는 것을 막는다.
   * b. 채택 시 `targetAngles` 와 `lastSentAngles` 를 같은 값으로 채워 첫 프레임 움찔거림을 막는다.
4. **사용자가 슬라이더를 조작한다.**
5. **시스템은 10 Hz 주기로, 변화량이 0.5° 를 넘을 때만 명령을 전송한다.**
6. **서버가 `mode` 필드를 보고 대상 로봇을 고른 뒤 LeRobot SDK 로 모터에 전달한다.**
7. **실물 모터가 회전한다.**

---

## **Scenario #3 — 디지털 트윈 양방향 동기화**

1. **시스템은 30 Hz 주기로 `{"type":"get"}` 를 서버에 보낸다.**
2. **직전 요청의 응답을 못 받았으면 새 요청을 보내지 않는다(in-flight 1건).**
   * a. 1초 안에 응답이 없으면 포기하고 다음 주기에 재요청한다.
   * b. 타임아웃이 없으면 응답 1건 유실만으로 폴링이 영구 정지한다.
3. **서버가 각 모터의 −100~100 정규화값을 응답한다.**
4. **시스템이 관절별 min/max 로 역정규화해 각도(deg)로 되돌린다.**
5. **변환된 각도를 시뮬 관절 목표값에 반영한다.**
6. **UI 슬라이더 옆에 실물 각도가 `[실:○○°]` 로 함께 표시된다.**

---

## **Scenario #4 — 두 팔 동시 제어 (미러)**

1. **사용자가 상단 바에서 「미러」 버튼을 누른다.**
2. **사용자가 Robot 1 패널의 슬라이더를 조작한다.**
3. **시스템이 같은 각도를 두 로봇 모두에 라우팅한다.**
   * a. 채널이 꺼진 로봇에는 명령을 보내지 않는다.
4. **반대편(Robot 2) 슬라이더 표시도 같은 값으로 따라간다.**
5. **「독립」 버튼을 누르면 각 패널이 각자의 로봇만 제어한다.**

---

## **Scenario #5 — 동작 녹화 및 재생 (간이 티칭)**

1. **사용자가 상단 「🎬 Record」 토글을 켠다.**
   * a. 녹화 모드는 제어 방식(독립/미러)과 무관하게 켤 수 있다.
2. **사용자가 원하는 자세를 슬라이더로 만든다.**
3. **「+ R1」 / 「+ R2」 / 「+ 둘 다」 로 현재 자세를 스텝으로 추가한다.**
4. **필요 시 대기 스텝(`⏱`)과 반복 구간(`🔁`)을 삽입한다.**
5. **스텝 순서를 위/아래로 옮기거나 이름을 바꾸거나 삭제한다.**
6. **「💾 저장」 으로 `Recordings/<이름>.json` 에 기록한다.**
7. **「▶ 재생」 을 누르면 스텝을 순서대로 실행한다.**
   * a. 반복 구간은 스택으로 처리해 중첩 반복이 가능하다.
   * b. 재생 중인 스텝이 UI에서 강조 표시된다.
   * c. 「⏹ 정지」 로 중단할 수 있다.

---

## **Scenario #6 — 그리퍼 개폐**

1. **사용자가 그리퍼 슬라이더 또는 「🤏 닫기 / 🖐 반 / 🖐 열기」 버튼을 조작한다.**
2. **시스템이 0~100 % 를 구동축 각도로 환산한다 (0 % = −48.5°, 100 % = 0°).**
3. **시뮬 경로**
   * a. 구동축(`pincopen_left_proximal_link`)의 목표각을 설정한다.
   * b. 매 프레임 종동축 3개에 배율을 곱해 복사한다 (−1, −1, +1).
   * c. 좌우 손가락이 평행하게 맞물린다.
4. **실물 경로**
   * a. 안전 게이트(`PincOpenSafety.TryApprove`)를 통과해야 한다.
   * b. 기본값이 잠금이므로 **차단되고 경고를 1회 출력한 뒤 전송하지 않는다.**
   * c. J6 각도 슬라이더 경로에서도 송신 직전 같은 게이트를 통과시킨다.
   * d. 통과 시 양 끝 5 % 여유를 남긴 정규화값으로 변환해 전송한다.

---

## **Scenario #7 — 홈포즈 재지정**

1. **사용자가 「⚙️ 현재 자세를 홈으로 지정」 버튼을 누른다.**
2. **확인 다이얼로그가 화면 중앙에 표시된다.**
   * a. 배경이 어두워지고 다른 조작이 막힌다.
3. **사용자가 「✅ 예, 저장하기」 를 누른다.**
4. **시스템이 `{"type":"set_home","mode":"robotN"}` 를 전송한다.**
5. **응답에 `"ok":true` 가 포함되면 슬라이더·홈포즈 캐시를 0으로 리셋한다.**
6. **「❌ 취소」 를 누르면 아무것도 전송하지 않는다.**

---

## **Scenario #8 — 작업 큐 연속 운전** (미구현)

1. **사용자가 관제 화면의 「작업 큐」 패널에서 「항목 추가」 를 누른다.**
   * a. 저장된 루틴 목록(`Recordings/*.json`)이 표시된다.
2. **사용자가 루틴을 골라 큐에 넣는다. 필요한 만큼 반복한다.**
   * a. 같은 루틴을 여러 번 넣을 수 있다.
   * b. 항목별로 반복 횟수를 지정한다.
   * c. 당장 빼고 싶은 항목은 지우지 않고 꺼둔다.
3. **사용자가 위/아래 버튼으로 실행 순서를 맞춘다.**
4. **사용자가 「시작」 을 누른다.**
   * a. 큐 실행 중에는 관절 슬라이더 입력이 막힌다.
5. **시스템이 첫 항목의 루틴을 불러와 재생한다.**
   * a. 실행 중인 항목이 하이라이트되고 `현재 n/m 스텝` 이 표시된다.
6. **재생이 실제로 끝나면 시스템이 다음 항목으로 넘어간다.**
   * a. 고정 시간으로 기다리지 않는다.
   * b. 반복 횟수가 남았으면 같은 항목을 다시 재생한다.
   * c. 꺼둔 항목은 건너뛴 것으로 표시하고 지나간다.
7. **마지막 항목이 끝나면 큐가 완료 상태가 된다.**
   * a. `loopQueue` 가 켜져 있으면 처음으로 돌아간다.
8. **사용자가 「일시정지」 를 누르면 현재 루틴을 끝까지 재생하고 다음 항목 앞에서 멈춘다.**
9. **「건너뛰기」 는 현재 항목을 멈추고 다음 항목으로, 「중단」 은 큐 전체를 멈춘다.**
   * a. 어느 쪽이든 팔은 현재 자세를 유지한다.
10. **소켓이 끊기면 시스템이 큐를 즉시 중단한다.**

---

# 5. Validation — 시나리오 기반 기능 구현 체크리스트

> 기준: `Assets/Script/*.cs` 16개 · `Assets/Editor/*.cs` 3개 · `Assets/Scenes/LeRobot.unity` · `Assets/SO101_unity/so101.urdf` 직접 읽기 (2026-08-01)

---

## Scenario #1 — 시뮬레이션 단독 조작

| # | 기능 | 담당 | 상태 |
| --- | --- | --- | --- |
| 1-1 | 6관절 슬라이더 / 스텝 버튼 제어 (`DrawRobotPanel`) | Unity UI | ✅ |
| 1-2 | 스텝 각도 변경 0.5/1/5/10° + 직접 입력 (`DrawTopBar`) | Unity UI | ✅ |
| 1-3 | 목표각을 `ArticulationBody.xDrive.target` 에 반영 (`ApplyToArticulationBodies`) | Sim | ✅ |
| 1-4 | 관절 min/max 클램프 (`SetJointTarget` 의 `Mathf.Clamp`) | Sim | ✅ |
| 1-5 | `invertSign` / `angleOffset` 보정 | Sim | ✅ |
| 1-6 | 실로봇 없이 단독 동작 (`Mode.SimOnly`) | Sim | ✅ |
| 1-7 | 드라이브 파라미터 자동 설정 (stiffness 10000 / damping 1000 / forceLimit 1000) | Sim | ✅ |

---

## Scenario #2 — 실로봇 원격 조작

| # | 기능 | 담당 | 상태 |
| --- | --- | --- | --- |
| 2-1 | TCP 접속 / 해제 (`Connect` / `Disconnect`) | 통신 | ✅ |
| 2-2 | 이미 살아있는 연결 재사용 (중복 Connect 방어) | 통신 | ✅ |
| 2-3 | 자동 재접속 (`autoReconnect`, 기본 2초 주기) | 통신 | ✅ 씬 `autoReconnect: 1` |
| 2-4 | 각도 → 정규화값 변환 (`AngleToServerValue`) | 통신 | ✅ |
| 2-5 | NDJSON 한 줄 송신 + `InvariantCulture` 고정 | 통신 | ✅ |
| 2-6 | 10 Hz 전송 게이트 (`sendRateHz`) | Real | ✅ 씬 `sendRateHz: 10` |
| 2-7 | 0.5° 미만 미전송 (`minChangeToSend`) | Real | ✅ |
| 2-8 | 첫 전송 강제 (`lastSentAngles[i] = NaN`) | Real | ✅ |
| 2-9 | **첫 실물 수신 전 쓰기 보류** (`holdWritesUntilSynced`) | Real | ✅ 씬 `holdWritesUntilSynced: 1` |
| 2-10 | 실물 자세 채택 시 `lastSentAngles` 동시 갱신 (움찔 방지) | Real | ✅ `AdoptRealPose` |
| 2-11 | 서버 측 명령 수신·모터 구동 | 서버 | ⚠️ 미확인 — `robot_server_dual.py` 가 저장소에 없음 |

---

## Scenario #3 — 디지털 트윈 양방향 동기화

| # | 기능 | 담당 | 상태 |
| --- | --- | --- | --- |
| 3-1 | 30 Hz 각도 폴링 (`pollHz`) | Real | ✅ 씬 `pollHz: 30` |
| 3-2 | in-flight 1건 제한 (`waitingForGetResponse`) | Real | ✅ |
| 3-3 | 응답 타임아웃 1초 후 폴링 재개 (`getResponseTimeout`) | Real | ✅ 씬 `getResponseTimeout: 1` |
| 3-4 | 백그라운드 수신 → `ConcurrentQueue` → 메인 스레드 콜백 | 통신 | ✅ |
| 3-5 | 부분 수신 대응 (`lineBuffer` 개행 분할) | 통신 | ✅ |
| 3-6 | 콜백 큐 어긋남 복구 (16개 초과 시 전량 폐기) | 통신 | ✅ |
| 3-7 | 수신 스레드 종료 시 `isConnected=false` 반영 | 통신 | ✅ |
| 3-8 | 정규화값 → 각도 역변환 (`ServerValueToAngle`) | Real | ✅ |
| 3-9 | 시뮬 관절에 반영 (`HandleRealAngles`) | Manager | ✅ 씬 `realToSimSync: 1` |
| 3-10 | Play 시작 후 1회 초기 동기화 (`InitialSyncCoroutine`) | Manager | ✅ 씬 `syncOnStart: 1` |
| 3-11 | UI에 실물 각도 병기 `[실:○○°]` | Unity UI | ✅ |
| 3-12 | **실물-시뮬 자세 일치 실측** | 통합 | 🟡 코드 경로 완결, 실행 검증 기록 없음 |

---

## Scenario #4 — 두 팔 동시 제어

| # | 기능 | 담당 | 상태 |
| --- | --- | --- | --- |
| 4-1 | Independent 라우팅 (`RouteJointCommand`) | DualManager | ✅ |
| 4-2 | Mirror 라우팅 (`SetJointBoth` / `SetGripperBoth`) | DualManager | ✅ 씬 `controlMode: 1` (Mirror) |
| 4-3 | 채널 on/off (`robot1Enabled` / `robot2Enabled`) | DualManager | ✅ |
| 4-4 | 꺼진 채널에 명령 미전송 | DualManager | ✅ |
| 4-5 | 미러 시 반대편 UI 슬라이더 동기 | Unity UI | ✅ |
| 4-6 | 전체 홈 / 전체 정지 / 전체 재연결 | Unity UI | ✅ 전체 정지는 채널 on/off 와 무관하게 항상 두 로봇 모두 세운다 (7-2 참조) |
| 4-7 | 협동 작업 (SR_19) | DualManager | ⬜ `ControlMode` 는 2개뿐. `PROJECT_NOTES.md` 의 5모드 기술은 🔺 **구버전** |

---

## Scenario #5 — 동작 녹화 및 재생

| # | 기능 | 담당 | 상태 |
| --- | --- | --- | --- |
| 5-1 | 자세 스텝 추가 robot1 / robot2 / both (`AddMotionStepFromUI`) | Record | ✅ |
| 5-2 | 대기 스텝 (`AddWaitStep`) | Record | ✅ |
| 5-3 | 반복 시작/끝 스텝 (`AddLoopStartStep` / `AddLoopEndStep`) | Record | ✅ |
| 5-4 | 스텝 순서 변경 / 이름 변경 / 삭제 | Record | ✅ |
| 5-5 | JSON 저장 (`Recordings/*.json`) | Record | ✅ `Recordings\Untitled.json` 존재 확인 |
| 5-6 | JSON 불러오기 + 스텝 재번호 | Record | ✅ |
| 5-7 | 순차 재생 + 중첩 반복 (`Stack<(index, remaining)>`) | Record | ✅ |
| 5-8 | 재생 중 스텝 하이라이트 | Record UI | ✅ |
| 5-9 | 재생 중 정지 (`StopPlayback`) | Record | ✅ |
| 5-10 | 녹화 모드를 제어 모드와 분리 (`isRecordModeActive`) | DualManager | ✅ |
| 5-11 | `CaptureJoints()` / `CaptureGripper()` | Record | 🔺 **죽은 코드** — 항상 0 / 50 반환. 실제 경로는 `AddMotionStepFromUI` |

---

## Scenario #6 — 그리퍼 개폐

| # | 기능 | 담당 | 상태 |
| --- | --- | --- | --- |
| 6-1 | 0~100 % → 구동축 각도 환산 (`SetGripperPercent`) | PincOpen | ✅ |
| 6-2 | 종동축 3개 커플링 (−1, −1, +1) | PincOpen | ✅ 씬 `preset: 0` (MJCF_Full) |
| 6-3 | 구동각 범위 클램프 + 초과 시 경고 (`SetDriveAngle`) | PincOpen | ✅ |
| 6-4 | URDF 임포트 후 빈 드라이브 자동 충전 (`ConfigureDrives`) | PincOpen | ✅ |
| 6-5 | 실물 명령 기본 잠금 (`RealGripperEnabled = false`) | 안전 | ✅ |
| 6-6 | `SetGripperTarget` 경로 안전 게이트 | 안전 | ✅ |
| 6-7 | **J6 각도 슬라이더 경로 안전 게이트** (송신 루프 내 재검사) | 안전 | ✅ 2026-08-01 보강됨 |
| 6-8 | 끝단 여유 5 % 적용 변환 (`PercentToServerValue`) | 안전 | ✅ |
| 6-9 | 펌웨어 각도 리밋 / 과부하 보호 굽기 | 안전 | ⬜ 코드 출력만 함. 라파에서 미실행 |
| 6-10 | 실물 그리퍼 캘리브레이션 | 안전 | ⬜ 미수행 — 잠금 유지 사유 |

---

## Scenario #7 — 홈포즈 재지정 · 기타 운용

| # | 기능 | 담당 | 상태 |
| --- | --- | --- | --- |
| 7-1 | 홈으로 이동 (`{"type":"home"}`) | Unity UI | ✅ Unity 측 / ⚠️ 서버 측 미확인 |
| 7-2 | **비상 정지** (`StopAll` / `StopMotion`) | Sim / Real | ✅ 2026-08-02~03 구현. 아래 7-2a~7-2f 참조 (이전 판의 "실효 없음" 은 2026-08-01 기준이라 낡은 기술이었다) |
| 7-2a | 정지 시 토크를 끄지 않고 **현재 위치로 고정** | 안전 | ✅ 12V 팔은 토크를 끄면 중력으로 떨어진다 |
| 7-2b | Sim — 물리 각도(`jointPosition`)를 읽어 `xDrive.target` 에 고정 | Sim | ✅ 내부 목표도 같이 맞춰 `Update()` 가 되돌리지 않게 함 |
| 7-2c | Real — 마지막 폴링 값(≤33ms)을 `Goal_Position` 으로 송신 | Real | ✅ 못 읽은 관절은 건드리지 않는다 |
| 7-2d | 정지 중 송신 전면 차단, 읽기(폴링)는 유지 | Real | ✅ UI 가 현재 상태를 계속 보여줘야 한다 |
| 7-2e | 정지 중 UI 슬라이더·목표 설정 차단 | DualManager / Sim / Real | ✅ 라우팅 게이트 + 두 컨트롤러의 setter 게이트 |
| 7-2f | **정지 시 루틴 재생 중단** | Record | ✅ 2026-08-03. `RecordManager` 에 검사가 없어 재생이 계속 돌던 것을 고침 |
| 7-2g | ESC 키 단축키 | DualManager | ✅ |
| 7-2h | 해제 시 실물 자세를 채택해 튐 방지 (`AdoptRealPose`) | Real | ✅ |
| 7-2i | 서버 측 `{"type":"stop"}` 명령 | 서버 | ⬜ 미구현. 현재는 관절별 `Goal_Position` 송신으로 대신한다. 소켓이 끊긴 상태에서는 정지 명령이 못 나간다 |
| 7-3 | 홈포즈 저장 (`{"type":"set_home"}`) + 확인 다이얼로그 | Unity UI | ✅ Unity 측 / ⚠️ 서버 측 미확인 |
| 7-4 | 속도·가속도 설정 (`{"type":"set_speed"}`) + 0.5초 디바운스 | Unity UI | ✅ Unity 측 / ⚠️ 서버 측 미확인 |
| 7-5 | 서보 토크 ON/OFF (`{"type":"torque"}`) | Real | 🟡 API 존재. **UI 노출 없음** (호출처 미발견) |
| 7-6 | 연결 끊김 시 대기 콜백 실패 처리 | 통신 | ✅ |
| 7-7 | 손목 카메라 영상 표시 (SR_18) | Unity UI | ⬜ 미구현 |
| 7-8 | 역기구학 IK (SR_20) | 서버 + Unity | ✅ 서버 `handle_ik()`(placo)가 계산하고 `SOArmIKController` 가 매니저를 거쳐 적용. 관제 카드의 스위치로 조인트 ↔ 카티시안 전환 |
| 7-8a | 회전 조그 (공구 축 기준 증분) | 서버 + Unity | ✅ `Rz` 안정. `Ry` 는 자세를 탐. `Rx` 는 홈 근처에서만 된다 — J1 에 묶여 팔이 펴지면 TCP 가 크게 밀린다 |
| 7-8b | 카티시안 안전선 (회전 드리프트 5mm / 관절 도약 20°) | 서버 + Unity | ✅ 한계를 넘으면 서버는 결과를 버리고 Unity 는 해를 적용하지 않는다 |
| 7-8c | TCP 를 PincOpen 손끝으로 (FR-39) | — | ⬜ 미구현. 현재 `gripper_frame_link`(순정 조 끝) 기준 |

---

## Scenario #8 — 작업 큐 연속 운전

> 전부 미구현. 명세는 `docs/TASK_QUEUE.md` (2026-08-03).

| # | 기능 | 담당 | 상태 |
| --- | --- | --- | --- |
| 8-1 | `QueueItem` / `TaskQueue` 자료구조 | Queue | ⬜ |
| 8-2 | 큐 JSON 저장·복원 (`Recordings/Queues/*.json`) | Queue | ⬜ 루틴과 같은 폴더에 두면 `ListSavedFiles()` 가 큐를 루틴으로 읽는다 |
| 8-3 | 항목 추가 / 삭제 / 순서 변경 | Queue UI | ⬜ |
| 8-4 | 항목별 반복 횟수 · 사용 여부(on/off) | Queue | ⬜ |
| 8-5 | 순차 실행 — `LoadProject` → `StartPlayback` → `IsPlaying` 감시 | Queue | ⬜ |
| 8-6 | **이전 재생의 실제 완료 후 다음 항목 시작** (고정 시간 대기 금지) | Queue | ⬜ 2026-08-02 "스텝 건너뜀" 과 같은 부류 |
| 8-7 | 실행 중 항목 하이라이트 + `현재 n/m 스텝` 표시 | Queue UI | ⬜ |
| 8-8 | 일시정지(항목 경계) / 건너뛰기 / 중단 | Queue | ⬜ 스텝 중간 일시정지는 미결 (O-2) |
| 8-9 | 큐 실행 중 관절 슬라이더 입력 차단 | 안전 | ⬜ 안 막으면 2026-08-02 J2/J3 와 같은 덮어쓰기 발생 |
| 8-10 | 소켓 끊김 시 즉시 큐 중단 | 안전 | ⬜ |
| 8-11 | 항목 실패 판정 (`stopOnError`) | Queue | ⬜ **선행 미결 (O-1)** — `PlaybackRoutine()` 에 실패 개념이 없어 항상 "재생 완료" 로 끝난다 |
| 8-12 | **비상 정지 실효성 확보** | 안전 | ✅ 2026-08-03 완료 (7-2). 남은 것은 서버 `{"type":"stop"}` (7-2i) — 소켓이 끊기면 정지 명령이 못 나간다 |

---

# 6. Constraints

| **ID** | **제약** | **결과적으로 강제되는 설계** |
| --- | --- | --- |
| CON_01 | Unity 런타임 스크립트는 C# 만 실행 가능하다 (Mono/IL2CPP) | 제어 UI·시뮬 전부 C# 작성 |
| CON_02 | LeRobot SDK는 Python 전용이며 C# 바인딩이 없다 | 한 프로세스에 못 넣음 → 별도 프로세스 + IPC → **TCP + JSON** 채택 |
| CON_03 | 라즈베리파이 4는 ARM 아키텍처다. 일반 PyTorch 휠은 `Illegal Instruction` 으로 죽는다 | `torch==2.7.0` + `torchvision==0.22.0` ARM CPU 빌드 고정 |
| CON_04 | **URDF는 폐루프(closed-loop) 구속을 표현할 수 없다.** PincOpen은 평행 4절 링크다 | 종동축 3개를 스크립트로 미러링 (`PincOpenCoupling`). Unity URDF Importer는 ROS2 `mimic` 태그를 반영하지 않는다 |
| CON_05 | **STS3215는 위치 제어 모드에서 토크 제한 기능이 없다** | 소프트웨어 2중 방어 + 펌웨어 보호 파라미터 필요 (SR_12) |
| CON_06 | Unity URP 사용 | URDF 임포트 직후 모델이 분홍색 → Material 수동 적용 필요 |
| CON_07 | Unity 6 URDF Importer의 기본 볼록분해기 vHACD가 `NullReferenceException` 으로 크래시한다 | ① STL → DAE 변환 ② URDF `collision` 섹션 주석 처리 ③ 임포트 시 `convexMethod = unity` 강제 |
| CON_08 | 서버는 **1 프로세스 / 1 포트(5000)** 에서 로봇 2대를 모두 관리한다 | 씬에도 `SOArmSocketClient` 가 **1개만** 존재. Real 컨트롤러 2개가 공유하고 `mode` 필드로 라우팅한다 |
| CON_09 | USB 포트 번호(`ttyACM*`)는 꽂는 순서·재부팅에 따라 바뀐다 | `/dev/serial/by-id/...` 고정 경로 사용 |
| CON_10 | Waveshare Bus Servo Adapter의 점퍼는 반드시 **B(USB-SERVO)** 위치여야 한다 | A 위치면 통신 자체가 불가 |
| CON_11 | 라즈베리파이 IP가 자주 바뀐다 | 접속 실패 시 `hostname -I` 재확인. 🔺 문서 3곳의 IP 기록이 서로 다름 |
| CON_12 | Unity 프로젝트가 외장하드 `F:\UNITY\LeRobot` 에 있다 | 외장하드 미연결 시 전체 작업 불가 |
| CON_13 | PincOpen 메시는 **CC BY-SA 4.0** 이다 | 공개 시 출처 표기 + 동일 라이선스 유지 의무 |

---

# 7. 🔺 문서–구현 불일치 (정정 필요)

| **ID** | **항목** | **문서/코드에 적힌 값** | **실제 확인값** | **조치** |
| --- | --- | --- | --- | --- |
| GAP_01 | PincOpen 손가락 닫힘 각 | `docs/PINCOPEN_INTEGRATION.md` §7 · 기존 `HW/SW_ARCHITECTURE.md` = **−69.9°** | `PincOpenCoupling.FingerClosedDeg = -48.5f`, URDF `lower="-0.8465"` (= −48.5°), 씬 4개 배열 전부 `minAngle: -48.5` | 구 문서 정정 |
| GAP_02 | URDF 주석 내부 모순 | `so101.urdf` L348 주석 `(-69.9° ~ 0°)` | 바로 다음 줄 L349 는 `0.8465 rad = 48.5°` 로 옳게 적혀 있음 | L348 주석 수정 |
| GAP_03 | URDF 상단 임시값 주석 | `so101.urdf` L325~330 `⚠️ 임시값 … limit ±1.25 rad — 커플링 배율 미확정` | 리밋은 `-0.8465 ~ 0` 으로 확정, 배율도 ×1.0 확정 | 주석 삭제/갱신 |
| GAP_04 | `SOArmPresets` 관절 범위 | J1~J5 전부 `±110°` | 씬/URDF = ±110 / ±100 / ±96.8 / ±95 / −157.2~162.8 | 프리셋을 URDF 값으로 수정 (J6은 이미 상수 참조로 정상) |
| GAP_05 | 제어 모드 개수 | `PROJECT_NOTES.md` = Robot1Only / Robot2Only / Independent / Mirror / Cooperative **5개** | `ControlMode { Independent, Mirror }` **2개** + `robot1Enabled`/`robot2Enabled` 분리 | `PROJECT_NOTES.md` 갱신 |
| GAP_06 | `SOArmManager.autoConnectReal` | 기존 `SW_ARCHITECTURE.md` = `0` | 씬 실제값 = **`1`** (2대 모두) | 구 문서 정정 |
| GAP_07 | 그리퍼 안전 게이트 우회 | 기존 `REQUIREMENTS.md` §6.3 = 🔴 J6 각도 슬라이더가 게이트 우회 | `SOArmRealController.Update()` 송신 루프에 `motorName == "gripper"` 검사 **추가됨** | 결함 해소, 문서 정정 |
| GAP_08 | 라즈베리파이 IP | `PROJECT_NOTES.md` `192.168.75.245` / `SOArmSocketClient.cs` 기본값 `192.168.45.18` | 씬 직렬화값 = **`192.168.75.245`** | 설정 일원화 |

---

# 8. ⚠️ 미확인 항목 (Open Items)

| **ID** | **항목** | **왜 미확인인가** | **확인 방법** |
| --- | --- | --- | --- |
| OPEN_01 | `robot_server_dual.py` 의 실제 동작 | 라즈베리파이 `/home/sw/` 에만 존재. Unity 저장소에 `.py` 파일 0개 | 서버 소스를 저장소에 포함 후 대조 |
| OPEN_02 | 서버가 `get`/`set_home`/`torque`/`set_speed`/`home` 을 처리하는지 | OPEN_01 과 동일 | 위와 동일 |
| OPEN_03 | `mirror` 모드를 서버가 어떻게 처리하는지 | 프로토콜 명세에는 있으나 서버 구현 미확인 | 위와 동일 |
| OPEN_04 | 홈포즈 다이얼로그가 안내하는 "캘리브 파일 자동 백업 / autocorrect / 자동 복구" | UI 문구에만 존재 | 서버 구현 확인 |
| OPEN_05 | 양방향 동기화(SR_06)의 **실물 검증** | 코드 경로는 완결. 실행 기록 없음 | 실물 연결 후 자세 대조 |
| OPEN_06 | 손목 카메라 개수 · 연결 위치 · 작동 여부 | 실물 확인 필요 | 라파에서 `ls /dev/video*`, `v4l2-ctl --list-devices` |
| OPEN_07 | PincOpen 실물 장착 대수 (1대? 2대?) | 씬에는 2대 모두 이식됨. 실물 미확인 | 육안 확인 |
| OPEN_08 | 실물 그리퍼 캘리브레이션 상태 | 실물 접근 필요 | SR_12 잠금해제 절차 ①②단계 수행 |
| OPEN_09 | `docs/PINCOPEN.md` 부재 | `PincOpenSafety.cs` · `PINCOPEN_INTEGRATION.md` · `so101.urdf` 5곳이 참조하지만 파일이 없음 | 복구 또는 재작성 — **안전 절차 원본이라 공백이 위험** |
| OPEN_10 | 12 V 전원 전류 용량 / 어댑터 모델 / GND 공통 여부 | 실물 확인 필요 | 어댑터 라벨 확인 + 도통 시험 |

---

# 9. 추적성 매트릭스 (SR ↔ 구현 파일)

| **구현 파일** | **담당 SR** |
| --- | --- |
| `Script/SOArmJointConfig.cs` | SR_02 |
| `Script/SOArmPresets.cs` | SR_02 (🔺 GAP_04) |
| `Script/ISOArmController.cs` | SR_01, SR_03, SR_07 (공통 계약) |
| `Script/SOArmMotorMapper.cs` | SR_03, SR_02 |
| `Script/SOArmSocketClient.cs` | SR_03, SR_05, SR_13, SR_14, SR_17 |
| `Script/SOArmSimController.cs` | SR_01, SR_02, SR_11, SR_15 |
| `Script/SOArmRealController.cs` | SR_03, SR_04, SR_05, SR_06, SR_12, SR_13, SR_15, SR_17 |
| `Script/SOArmManager.cs` | SR_06, SR_07 |
| `Script/SOArmDualManager.cs` | SR_07, SR_15 |
| `Script/SmartFactoryUI_v3_4.cs` | SR_01, SR_13, SR_14, SR_16 |
| `Script/SmartFactoryRecordUI.cs` | SR_08, SR_09, SR_10 |
| `Script/RecordManager.cs` | SR_08, SR_09, SR_10 |
| `Script/RecordProject.cs`, `Waypoint.cs` | SR_10 |
| `Script/PincOpenCoupling.cs` | SR_11, SR_02 |
| `Script/PincOpenSafety.cs` | SR_12 |
| `Editor/PincOpenSetupMenu.cs` | CON_07 |
| `Editor/PincOpenMainSceneMigrator.cs` | SR_11 (씬 이식) |
| `Editor/PincOpenCapture.cs` | SR_11 (검증) |
| `Assets/SO101_unity/so101.urdf` | SR_02, SR_11, CON_04, CON_13 |

---

# 10. 관련 문서

| **문서** | **내용** |
| --- | --- |
| `docs/v2/SW_ARCHITECTURE.md` | 소프트웨어 계층·모듈·인터페이스 명세·상태/시퀀스 다이어그램·데이터 구조 |
| `docs/v2/HW_ARCHITECTURE.md` | 물리 구성·BOM·기구 치수·전원/배선·안전 한계 |
| `docs/PINCOPEN_INTEGRATION.md` | PincOpen 통합 확정 기록 (🔺 §7 수치는 GAP_01 참조) |
| `docs/PINCOPEN.md` | ⚠️ **부재** (OPEN_09) — 실물 그리퍼 안전 절차 원본 |
