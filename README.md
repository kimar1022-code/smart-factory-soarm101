# 🏭 Smart Factory: Dual SO-ARM101 Robot Control System

> Unity + Raspberry Pi + LeRobot SDK로 구축한 두 대의 협동로봇 분산 제어 시스템

[![Unity](https://img.shields.io/badge/Unity-6000.4.3f1-black?logo=unity)](https://unity.com/)
[![Python](https://img.shields.io/badge/Python-3.11+-3776AB?logo=python&logoColor=white)](https://www.python.org/)
[![LeRobot](https://img.shields.io/badge/LeRobot-HuggingFace-FFD21E)](https://github.com/huggingface/lerobot)
[![Raspberry Pi](https://img.shields.io/badge/RaspberryPi-4-A22846?logo=raspberry-pi&logoColor=white)](https://www.raspberrypi.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Status](https://img.shields.io/badge/Status-Active%20Development-orange)]()

<!-- 시각 자료 추가 예정: docs/images/demo.gif -->

---

## 📋 Overview

**Smart Factory: Dual SO-ARM101**은 Hugging Face의 **LeRobot 프레임워크**를 기반으로, 두 대의 SO-ARM101 6축 협동로봇을 Unity 시뮬레이터와 실시간 동기화 제어하는 **스마트 팩토리 시뮬레이션 시스템**입니다.

| 항목 | 사양 |
|---|---|
| 로봇 | SO-ARM101 (3D 프린팅, 6-DOF) × 2대 |
| 모터 | Feetech STS3215 × 12개 |
| 컨트롤러 보드 | Waveshare Bus Servo Adapter (A) × 2개 |
| 메인 컴퓨터 | Raspberry Pi 4 (4GB RAM, Ubuntu 24.04) |
| Unity 호스트 | Windows PC |
| 통신 | TCP Socket (JSON Protocol, Port 5000) |

---

## ✨ Features

### 🤖 두 로봇 동시 제어
- **Robot1Only / Robot2Only**: 단일 로봇 제어
- **Independent**: 두 로봇 독립 제어
- **Mirror**: 두 로봇 동기 동작 (디지털 트윈)
- **Cooperative**: 협동 작업 (스마트 팩토리)

### 🔄 Sim ↔ Real 실시간 동기화
- Unity URDF 임포트 정확한 3D 모델링
- 공식 SO-ARM101 관절 범위 적용
- 슬라이더 조작 → 시뮬+실로봇 동시 동작

### 📡 분산 시스템 아키텍처
- TCP/JSON 프로토콜 직접 설계
- Unity(클라이언트) ↔ Raspberry Pi(서버) ↔ LeRobot SDK
- USB serial-by-id 기반 안정적 모터 식별

---

## 🏗️ Architecture

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

---

## 🛠️ Tech Stack

### Raspberry Pi (Server)
- **OS**: Ubuntu 24.04 LTS
- **Python**: 3.11 + venv
- **LeRobot SDK** (Hugging Face)
- **PyTorch 2.7.0** (ARM CPU 빌드)
- **Feetech 모터 드라이버**

### Unity (Client)
- **Unity 6000.4.3f1**
- **C#** (10개 스크립트, 약 1,000줄)
- **URDF Importer** (Unity Robotics)
- **TCP Socket Client**

---

## 📁 Project Structure
smart-factory-soarm101/
├── unity/
│   └── Assets/Scripts/
│       ├── SOArmJointConfig.cs       # 관절 설정 데이터
│       ├── SOArmPresets.cs           # 6축 프리셋 (공식 URDF 값)
│       ├── ISOArmController.cs       # 컨트롤러 인터페이스
│       ├── SOArmMotorMapper.cs       # 각도 ↔ 서버값 변환
│       ├── SOArmSocketClient.cs      # TCP 소켓 통신
│       ├── SOArmSimController.cs     # Unity 시뮬 제어
│       ├── SOArmRealController.cs    # 실제 로봇 제어
│       ├── SOArmManager.cs           # Sim+Real 통합 (1대)
│       ├── SOArmDualManager.cs       # 두 로봇 통합
│       └── SmartFactoryUI.cs         # OnGUI 제어 UI
├── raspberry_pi/
│   └── robot_server_dual.py          # 두 로봇 통합 TCP 서버
├── urdf/
│   └── SO101_unity/                  # URDF + DAE 메시
├── docs/
│   └── SETUP.md                      # 상세 설치 가이드
├── README.md
├── LICENSE
└── .gitignore

---

## 🚀 Getting Started

자세한 설치 가이드는 [`docs/SETUP.md`](docs/SETUP.md)를 참고하세요.

### Raspberry Pi (Server)

```bash
# LeRobot 설치
git clone https://github.com/huggingface/lerobot.git
cd lerobot
python -m venv ~/lerobot-env
source ~/lerobot-env/bin/activate
pip install -e ".[feetech]"

# PyTorch ARM CPU 빌드 (필수)
pip install torch==2.7.0 torchvision==0.22.0 \
    --index-url https://download.pytorch.org/whl/cpu

# 캘리브레이션 (각 로봇별 1회)
python -m lerobot.scripts.lerobot_calibrate \
    --robot.type=so100_follower \
    --robot.port=/dev/serial/by-id/... \
    --robot.id=robot1

# 서버 실행
python raspberry_pi/robot_server_dual.py
```

### Unity (Client)

1. Unity 6000.4.3f1 프로젝트 생성
2. URDF Importer 설치
3. `urdf/SO101_unity/so101.urdf` 임포트
4. `unity/Assets/Scripts/` 스크립트 추가
5. Robot1_Group / Robot2_Group 씬 구성
6. ▶ Play

---

## 📡 Communication Protocol

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
| `mode` | `robot1` / `robot2` / `mirror` | 제어 대상 |
| `motor` | `shoulder_pan`, `shoulder_lift`, `elbow_flex`, `wrist_flex`, `wrist_roll`, `gripper` | 모터 이름 |
| `value` | -100 ~ 100 (float) | 정규화 위치 값 |

---

## 📐 SO-ARM101 Joint Specifications

| 관절 | 최소 (°) | 최대 (°) | 비고 |
|---|---|---|---|
| shoulder_pan | -110.0 | 110.0 | 베이스 회전 |
| shoulder_lift | -100.0 | 100.0 | 어깨 |
| elbow_flex | -96.8 | 96.8 | 팔꿈치 (5° 캘리브 오프셋) |
| wrist_flex | -95.0 | 95.0 | 손목 상하 |
| wrist_roll | -157.2 | 162.8 | 손목 회전 (비대칭) |
| gripper | -10.0 | 100.0 | 그리퍼 (비대칭) |

---

## 🐛 Troubleshooting

| 이슈 | 원인 | 해결 |
|---|---|---|
| **URDF Import NullReferenceException** | STL Convex Mesh 생성 실패 | STL → DAE 변환 + Collision 주석 처리 |
| **Unity 모델이 분홍색** | URP 머티리얼 미적용 | Edit → Render Pipeline → URP Material 변환 |
| **Overload Error / 모터 응답 없음** | 모터 과부하 또는 케이블 | 전원 OFF/ON, 모터 스캔 재확인 |
| **Address already in use (port 5000)** | 이전 서버 프로세스 잔존 | `pkill -f robot_server_dual.py` |
| **PyTorch Illegal Instruction** | ARM 비호환 빌드 | ARM CPU 빌드(`--index-url cpu`) 사용 |

---

## 🛣️ Roadmap

- [x] **Phase 1**: 기본 제어 (각 로봇 독립 + 미러)
- [x] **Phase 2**: Unity 시뮬 ↔ 실로봇 동기화
- [ ] **Phase 3**: 작업 시퀀스 녹화/재생 (JSON)
- [ ] **Phase 4**: 협동 작업 (Pick & Place 전달)
- [ ] **Phase 5**: 스마트 팩토리 UI (작업 큐 관리)
- [ ] **Phase 6**: 카메라 비전 통합

---

## 📚 References

- [SO-ARM100/101 GitHub](https://github.com/TheRobotStudio/SO-ARM100)
- [LeRobot (Hugging Face)](https://github.com/huggingface/lerobot)
- [Unity URDF Importer](https://github.com/Unity-Technologies/URDF-Importer)

---

## 📜 License

본 프로젝트의 코드는 [MIT License](LICENSE)를 따릅니다.
- SO-ARM100/101 하드웨어: Apache 2.0 (TheRobotStudio)
- LeRobot: Apache 2.0 (Hugging Face)

---

## 🙏 Acknowledgments

- **Hugging Face LeRobot 팀** — 오픈소스 로봇 학습 프레임워크
- **The Robot Studio** — SO-ARM 하드웨어 디자인
- **Unity Robotics** — URDF Importer
- 본 프로젝트는 AI 페어 프로그래밍 도구(Anthropic Claude)를 활용하여 개발되었으며, 시스템 설계·디버깅·아키텍처 결정은 작성자가 주도하였습니다.
- 인터페이스 설계는 자매 프로젝트 [Fairino FR5 Digital Twin](https://github.com/kimar1022-code/fairino-fr5-digital-twin)을 참고했습니다.

---

<p align="center">
  <b>Author</b>: Aeri Kim · 
  <a href="https://github.com/kimar1022-code">GitHub</a> · 
  <a href="mailto:kimar1022@gmail.com">Email</a>
</p>
