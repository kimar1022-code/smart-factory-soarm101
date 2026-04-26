# 🤖 Smart Factory: SO-ARM101 Dual Robot Control System

> Unity 시뮬레이션과 실제 로봇을 동기화하여 두 대의 SO-ARM101 협동 제어가 가능한 스마트 팩토리 시스템

[![Unity](https://img.shields.io/badge/Unity-6.4-blue?logo=unity)](https://unity.com/)
[![LeRobot](https://img.shields.io/badge/LeRobot-HuggingFace-yellow)](https://github.com/huggingface/lerobot)
[![Python](https://img.shields.io/badge/Python-3.11+-green?logo=python)](https://www.python.org/)
[![Platform](https://img.shields.io/badge/Platform-Raspberry%20Pi%204-red)](https://www.raspberrypi.org/)

---

## 📋 프로젝트 개요

이 프로젝트는 **Hugging Face의 LeRobot 프레임워크**를 기반으로, 두 대의 **SO-ARM101 6축 로봇팔**을 Unity에서 시각화하고 실시간으로 동기화 제어하는 **스마트 팩토리 시뮬레이션**입니다.

### 🎯 주요 기능

- ✅ **두 로봇 동시 제어** (Robot1 / Robot2 / 독립 / 미러 / 협동 모드)
- ✅ **Unity 시뮬레이션 ↔ 실제 로봇 실시간 동기화**
- ✅ **TCP 소켓 통신** 기반 클라이언트-서버 구조
- ✅ **URDF 임포트** 정확한 3D 모델링
- ✅ **공식 SO-ARM101 관절 범위** 적용

---

## 🏗️ 시스템 아키텍처

```
┌──────────────────────────────────┐
│  Unity 6 (Windows PC)            │
│  ┌──────────────────────────┐    │
│  │  SmartFactoryUI          │    │
│  │  ↓                       │    │
│  │  SOArmDualManager        │    │
│  │  ├─ Robot1 (Sim+Real)    │    │
│  │  └─ Robot2 (Sim+Real)    │    │
│  └──────────────────────────┘    │
└──────────────┬───────────────────┘
               │ TCP Socket (JSON)
               │ Port 5000
               ▼
┌──────────────────────────────────┐
│  Raspberry Pi 4 (Ubuntu 24.04)   │
│  ┌──────────────────────────┐    │
│  │  robot_server_dual.py    │    │
│  │  ↓                       │    │
│  │  LeRobot SDK             │    │
│  └──────────────────────────┘    │
└─────┬─────────────┬──────────────┘
      │ USB         │ USB
      ▼             ▼
┌──────────┐   ┌──────────┐
│ Robot 1  │   │ Robot 2  │
│ SO-ARM101│   │ SO-ARM101│
└──────────┘   └──────────┘
```

---

## 🛠️ 하드웨어 구성

| 항목 | 사양 |
|---|---|
| 로봇팔 | SO-ARM101 (3D 프린팅, 6-DOF) × 2대 |
| 모터 | Feetech STS3215 × 12개 |
| 컨트롤러 보드 | Waveshare Bus Servo Adapter (A) × 2개 |
| 메인 컴퓨터 | Raspberry Pi 4 (4GB RAM) |
| OS | Ubuntu 24.04 LTS |
| Unity 호스트 | Windows PC |

---

## 💾 소프트웨어 스택

### 라즈베리파이 (서버)
- **Python 3.11** + 가상환경 (`lerobot-env`)
- **LeRobot SDK** (Hugging Face)
- **PyTorch 2.7.0** (ARM CPU 빌드)

### Unity (클라이언트)
- **Unity 6.4 (6000.4.3f1)**
- **URDF Importer** (Unity Robotics)
- **C# 스크립트** 10개 (`SOArmControl` namespace)

---

## 📂 프로젝트 구조

```
smart-factory-soarm101/
├── README.md
├── unity/
│   └── Assets/Script/
│       ├── SOArmJointConfig.cs       # 관절 설정 데이터
│       ├── SOArmPresets.cs            # 6축 프리셋 (공식 URDF 값)
│       ├── ISOArmController.cs        # 컨트롤러 인터페이스
│       ├── SOArmMotorMapper.cs        # 각도 ↔ 서버값 변환
│       ├── SOArmSocketClient.cs       # TCP 소켓 통신
│       ├── SOArmSimController.cs      # Unity 시뮬 제어
│       ├── SOArmRealController.cs     # 실제 로봇 제어
│       ├── SOArmManager.cs            # Sim+Real 통합 (1대)
│       ├── SOArmDualManager.cs        # 두 로봇 통합
│       └── SmartFactoryUI.cs          # OnGUI 제어 UI
│
├── raspberry_pi/
│   └── robot_server_dual.py           # 두 로봇 통합 서버
│
└── urdf/
    └── SO101_unity/
        ├── so101.urdf                  # FR5 스타일 URDF (DAE 형식)
        ├── meshes/SO101/visual/        # DAE 메시 파일 13개
        └── Materials/                  # 머티리얼 파일
```

---

## 🚀 설치 및 실행

### 1. 라즈베리파이 설정

```bash
# LeRobot 설치
git clone https://github.com/huggingface/lerobot.git
cd lerobot
python -m venv ~/lerobot-env
source ~/lerobot-env/bin/activate
pip install -e ".[feetech]"

# PyTorch ARM CPU 빌드 (필수)
pip install torch==2.7.0 torchvision==0.22.0 --index-url https://download.pytorch.org/whl/cpu
```

### 2. 모터 ID 설정 (각 로봇별 1회)

```bash
python -m lerobot.scripts.lerobot_setup_motors \
    --robot.type=so100_follower \
    --robot.port=/dev/serial/by-id/usb-1a86_USB_Single_Serial_XXX-if00
```

> **주의**: Waveshare 보드의 점퍼는 반드시 **B 위치 (USB-SERVO)**에 있어야 합니다.

### 3. 캘리브레이션

```bash
python -m lerobot.scripts.lerobot_calibrate \
    --robot.type=so100_follower \
    --robot.port=/dev/serial/by-id/usb-1a86_USB_Single_Serial_XXX-if00 \
    --robot.id=robot1
```

### 4. 서버 실행

```bash
source ~/lerobot-env/bin/activate
python /home/sw/robot_server_dual.py
```

서버 실행 시 출력:
```
로봇 1 연결중...
로봇 2 연결중...
두 로봇 연결 완료!
유니티 연결 대기중... (포트 5000)
```

### 5. Unity 설정

1. Unity 6.4 프로젝트 생성
2. URDF Importer 설치 (Package Manager → Git URL)
   ```
   https://github.com/Unity-Technologies/URDF-Importer.git?path=/com.unity.robotics.urdf-importer
   ```
3. `urdf/SO101_unity` 폴더를 Assets에 복사
4. `so101.urdf` 우클릭 → **Import Robot from Selected URDF file**
5. `unity/Assets/Script` 폴더의 10개 스크립트를 프로젝트에 추가

### 6. 씬 설정

```
SmartFactoryManager (빈 GO)
├─ SOArmDualManager 컴포넌트
└─ SmartFactoryUI 컴포넌트

Robot1_Group (빈 GO)
├─ SOArmSocketClient (mode: robot1, IP: 라즈베리파이IP)
├─ SOArmRealController (mode: robot1)
├─ Robot_1 (URDF 임포트, J1~J6)
│  └─ SOArmSimController
└─ SOArmManager (Sim+Real)

Robot2_Group (Robot1과 동일 구조, mode: robot2)
```

---

## 📊 통신 프로토콜

### Unity → Raspberry Pi (TCP/JSON)

```json
{
  "mode": "robot1",
  "motor": "shoulder_pan",
  "value": 30.5
}
```

| 필드 | 값 | 설명 |
|---|---|---|
| `mode` | `"robot1"` / `"robot2"` / `"mirror"` | 제어 대상 로봇 |
| `motor` | `shoulder_pan`, `shoulder_lift`, `elbow_flex`, `wrist_flex`, `wrist_roll`, `gripper` | 모터 이름 |
| `value` | -100 ~ 100 (float) | 정규화된 위치 값 |

---

## 📐 공식 SO-ARM101 관절 범위

URDF (`so101_new_calib.urdf`)에서 추출한 정확한 값:

| 관절 | 최소 (°) | 최대 (°) | 비고 |
|---|---|---|---|
| shoulder_pan (베이스) | -110.0 | 110.0 | |
| shoulder_lift (어깨) | -100.0 | 100.0 | |
| elbow_flex (팔꿈치) | -96.8 | 96.8 | 5° 캘리브 오프셋 적용 |
| wrist_flex (손목 상하) | -95.0 | 95.0 | |
| wrist_roll (손목 회전) | -157.2 | 162.8 | 비대칭 |
| gripper (그리퍼) | -10.0 | 100.0 | 비대칭 |

**홈 포즈**: 모든 관절 0° (캘리브레이션 시 가운데 위치)

---

## 🎮 제어 모드

`SOArmDualManager.ControlMode`로 5가지 모드 지원:

| 모드 | 설명 |
|---|---|
| **Robot1Only** | 1번 로봇만 제어 |
| **Robot2Only** | 2번 로봇만 제어 |
| **Independent** | 두 로봇 독립 제어 (UI에서 각각) |
| **Mirror** | 두 로봇 동시에 같은 동작 (디지털 트윈) |
| **Cooperative** | 협동 작업 (스마트 팩토리용) |

---

## 🐛 트러블슈팅

### URDF Import 시 NullReferenceException
- **원인**: STL 파일의 Convex Mesh 생성 실패
- **해결**: STL → DAE 변환 + Collision 섹션 주석 처리 (FR5 스타일)

### Unity 모델이 분홍색으로 보임
- **원인**: URP 머티리얼 미적용
- **해결**: Edit → Render Pipeline → URP Material 변환

### Overload Error / 모터 응답 없음
- **원인**: 모터 과부하 또는 케이블 연결 문제
- **해결**: 로봇 전원 OFF/ON, 모터 스캔으로 재확인

### Address already in use (포트 5000)
```bash
pkill -f robot_server_dual.py
python /home/sw/robot_server_dual.py
```

---

## 🛣️ 로드맵

- [x] **Phase 1**: 기본 제어 (각 로봇 독립 + 미러)
- [x] **Phase 2**: Unity 시뮬 ↔ 실로봇 동기화
- [ ] **Phase 3**: 작업 시퀀스 녹화/재생 (JSON)
- [ ] **Phase 4**: 협동 작업 (Pick & Place 전달)
- [ ] **Phase 5**: 스마트 팩토리 UI (작업 큐 관리)
- [ ] **Phase 6**: 카메라 비전 통합

---

## 📚 참고 자료

- [SO-ARM100/101 GitHub](https://github.com/TheRobotStudio/SO-ARM100)
- [LeRobot (Hugging Face)](https://github.com/huggingface/lerobot)
- [Unity URDF Importer](https://github.com/Unity-Technologies/URDF-Importer)
- [Foxglove LeRobot 시각화](https://foxglove.dev/blog/visualizing-lerobot-so-100-using-foxglove)

---

## 📜 라이선스

이 프로젝트는 학습 및 연구 목적으로 공개되어 있습니다.

- SO-ARM100/101 하드웨어: [Apache 2.0 (TheRobotStudio)](https://github.com/TheRobotStudio/SO-ARM100/blob/main/LICENSE)
- LeRobot: [Apache 2.0 (Hugging Face)](https://github.com/huggingface/lerobot/blob/main/LICENSE)

---

## 🙏 감사의 말

- **Hugging Face LeRobot 팀** - 오픈소스 로봇 학습 프레임워크
- **The Robot Studio** - SO-ARM 하드웨어 디자인
- **Unity Robotics** - URDF Importer
- **Fairino FR5 프로젝트** - 인터페이스 구조 참고

---

## 📞 문의

프로젝트 관련 문의는 GitHub Issues로 남겨주세요!
