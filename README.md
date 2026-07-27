# Smart Factory: Dual SO-ARM101 Robot Control System

SO-ARM101 6축 협동로봇 두 대를 Unity 시뮬레이터와 실시간 동기화 제어하는 분산 시스템입니다.
Hugging Face LeRobot 프레임워크 기반이며, Unity(Windows)와 라즈베리파이 서버가
직접 설계한 TCP/JSON 프로토콜로 통신합니다.

| 항목 | 사양 |
|---|---|
| 로봇 | SO-ARM101 (3D 프린팅, 6-DOF) × 2대 |
| 모터 | Feetech STS3215 × 12개 |
| 컨트롤러 보드 | Waveshare Bus Servo Adapter (A) × 2개 |
| 메인 컴퓨터 | Raspberry Pi 4 (4GB RAM, Ubuntu 24.04) |
| Unity 호스트 | Windows PC |
| 통신 | TCP Socket (JSON Protocol, Port 5000) |

## 기능

두 로봇 동시 제어 — 5가지 모드
- Robot1Only / Robot2Only: 단일 로봇 제어
- Independent: 두 로봇 독립 제어
- Mirror: 두 로봇 동기 동작
- Cooperative: 협동 작업

Sim ↔ Real 실시간 동기화
- Unity URDF 임포트 3D 모델 + 공식 SO-ARM101 관절 범위 적용
- 슬라이더 조작 → 시뮬 + 실로봇 동시 동작

분산 아키텍처
- TCP/JSON 프로토콜 직접 설계
- USB serial-by-id 기반 모터 식별 (재연결에도 안정)

## 구조

3계층으로 나뉩니다.

Unity Client (Windows PC) — `SmartFactoryUI`가 입력을 받아 `SOArmDualManager`로 전달하면,
매니저가 제어 모드에 따라 두 개의 `SOArmManager` 인스턴스에 명령을 라우팅합니다.
각 매니저는 Sim(Unity)과 Real(실로봇)을 동시에 제어합니다.

Network Bridge — TCP 소켓 위 JSON 메시지. `mode`, `motor`, `value` 세 필드로
어떤 로봇의 어느 모터를 어떤 값으로 움직일지 지정합니다.

Raspberry Pi Server (Ubuntu 24.04) — `robot_server_dual.py`가 메시지를 받아
LeRobot SDK의 FeetechMotorsBus로 두 대의 SO-ARM101을 제어합니다.
로봇당 Feetech STS3215 모터 6개, USB serial-by-id 경로로 식별합니다.

```
Unity UI → DualManager → Manager A/B → TCP/JSON → Pi Server → LeRobot SDK → SO-ARM101 A/B
```

## 파일 구성

- `unity/Assets/Scripts/` — Unity 클라이언트 스크립트 10개 (약 1,000줄)
  - `SOArmJointConfig.cs`, `SOArmPresets.cs` — 설정 데이터
  - `ISOArmController.cs` — 컨트롤러 인터페이스
  - `SOArmMotorMapper.cs` — 각도↔서버값 변환
  - `SOArmSocketClient.cs` — TCP 소켓 통신
  - `SOArmSimController.cs`, `SOArmRealController.cs` — Sim/Real 제어
  - `SOArmManager.cs` / `SOArmDualManager.cs` — 단일/이중 로봇 통합
  - `SmartFactoryUI.cs` — UI
- `raspberry_pi/robot_server_dual.py` — TCP 서버 (LeRobot SDK 기반)
- `urdf/SO101_unity/` — URDF + DAE 메시
- `docs/SETUP.md` — 상세 설치 가이드

## 실행

자세한 과정은 [docs/SETUP.md](docs/SETUP.md)에 있습니다. 요약:

라즈베리파이 (서버)
- LeRobot SDK + Feetech 드라이버 설치, PyTorch는 ARM CPU 빌드 필수
- 각 로봇 캘리브레이션 (`--robot.id=robot1`, `--robot.id=robot2`)
- `python raspberry_pi/robot_server_dual.py`

Unity (클라이언트)
- Unity 6000.4.3f1, URDF Importer 설치 (Package Manager → Git URL)
- `urdf/SO101_unity/so101.urdf` 임포트, 스크립트 추가
- Robot1_Group / Robot2_Group 씬 구성 후 Play

## 통신 프로토콜

Unity → 라즈베리파이 방향, TCP 소켓 위 JSON.

- `mode`: `robot1` / `robot2` / `mirror` — 제어 대상
- `motor`: `shoulder_pan` / `shoulder_lift` / `elbow_flex` / `wrist_flex` / `wrist_roll` / `gripper`
- `value`: -100 ~ 100 (float) — 정규화 위치 값

서버는 명령을 LeRobot SDK의 `FeetechMotorsBus.write()`로 전달합니다.

## 관절 사양

| 관절 | 최소 (°) | 최대 (°) | 비고 |
|---|---|---|---|
| shoulder_pan | -110.0 | 110.0 | 베이스 회전 |
| shoulder_lift | -100.0 | 100.0 | 어깨 |
| elbow_flex | -96.8 | 96.8 | 팔꿈치 (5° 캘리브 오프셋) |
| wrist_flex | -95.0 | 95.0 | 손목 상하 |
| wrist_roll | -157.2 | 162.8 | 손목 회전 (비대칭) |
| gripper | -10.0 | 100.0 | 그리퍼 (비대칭) |

## 트러블슈팅

| 이슈 | 원인 | 해결 |
|---|---|---|
| URDF Import NullReferenceException | STL Convex Mesh 생성 실패 | STL → DAE 변환 + Collision 주석 처리 |
| Unity 모델이 분홍색 | URP 머티리얼 미적용 | Edit → Render Pipeline → URP Material 변환 |
| Overload Error | 모터 과부하 또는 케이블 | 전원 OFF/ON, 모터 스캔 재확인 |
| Address already in use | 이전 서버 프로세스 잔존 | `pkill -f robot_server_dual.py` |
| PyTorch Illegal Instruction | ARM 비호환 빌드 | ARM CPU 빌드 사용 |

## 진행 상태

- [x] 기본 제어 (각 로봇 독립 + 미러)
- [x] Unity 시뮬 ↔ 실로봇 동기화
- [ ] 작업 시퀀스 녹화/재생 (JSON)
- [ ] 협동 작업 (Pick & Place 전달)
- [ ] 작업 큐 관리 UI
- [ ] 카메라 비전 통합

## 참고 자료

- [SO-ARM100/101 GitHub](https://github.com/TheRobotStudio/SO-ARM100)
- [LeRobot (Hugging Face)](https://github.com/huggingface/lerobot)
- [Unity URDF Importer](https://github.com/Unity-Technologies/URDF-Importer)

인터페이스 설계는 자매 프로젝트 [Fairino FR5 Digital Twin](https://github.com/kimar1022-code/fairino-fr5-digital-twin)을 참고했습니다.

## 라이선스

코드는 [MIT License](LICENSE)를 따릅니다.
SO-ARM100/101 하드웨어는 Apache 2.0 (TheRobotStudio), LeRobot은 Apache 2.0 (Hugging Face).
