# 🏭 Smart Factory: Dual SO-ARM101 Robot Control System

> Unity + Raspberry Pi + LeRobot SDK로 구축한 두 대의 협동로봇 분산 제어 시스템

[![Unity](https://img.shields.io/badge/Unity-6000.4.3f1-black?logo=unity)](https://unity.com/)
[![Python](https://img.shields.io/badge/Python-3.11+-3776AB?logo=python&logoColor=white)](https://www.python.org/)
[![LeRobot](https://img.shields.io/badge/LeRobot-HuggingFace-FFD21E)](https://github.com/huggingface/lerobot)
[![Raspberry Pi](https://img.shields.io/badge/RaspberryPi-4-A22846?logo=raspberry-pi&logoColor=white)](https://www.raspberrypi.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Status](https://img.shields.io/badge/Status-Active%20Development-orange)]()

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

본 시스템은 **3-tier 분산 아키텍처**로 구성되어 있습니다.

### Layer 1: Unity Client (Windows PC)

`SmartFactoryUI`가 사용자 입력을 받아 `SOArmDualManager`로 전달하면, 이 매니저가 5가지 제어 모드(Robot1Only / Robot2Only / Independent / Mirror / Cooperative)에 따라 두 개의 `SOArmManager` 인스턴스에 명령을 라우팅합니다. 각 매니저는 Sim(Unity 시뮬레이터)과 Real(실로봇)을 동시에 제어합니다.

### Layer 2: Network Bridge (TCP/JSON over Port 5000)

Unity와 Raspberry Pi는 TCP 소켓 위에 JSON 메시지로 통신합니다. `mode`, `motor`, `value` 세 필드로 구성된 단일 명령 형태로, 어떤 로봇의 어느 모터를 어떤 값으로 움직일지 지정합니다.

### Layer 3: Raspberry Pi Server (Ubuntu 24.04)

`robot_server_dual.py`가 TCP 메시지를 수신하면, LeRobot SDK의 FeetechMotorsBus를 통해 USB serial로 두 대의 SO-ARM101을 제어합니다. 각 로봇은 6개의 Feetech STS3215 모터로 구성되어 있고, USB serial-by-id 경로로 식별되어 재연결 시에도 안정성을 유지합니다.

### 데이터 흐름 요약

`Unity UI → DualManager → Manager A/B → TCP/JSON → Pi Server → LeRobot SDK → SO-ARM101 A/B`

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

폴더 구성은 다음과 같습니다.

- **`unity/Assets/Scripts/`** — Unity 클라이언트 스크립트 10개
  - `SOArmJointConfig.cs`, `SOArmPresets.cs` — 설정 데이터
  - `ISOArmController.cs` — 컨트롤러 인터페이스
  - `SOArmMotorMapper.cs` — 각도↔서버값 변환
  - `SOArmSocketClient.cs` — TCP 소켓 통신
  - `SOArmSimController.cs`, `SOArmRealController.cs` — Sim/Real 제어
  - `SOArmManager.cs` — 단일 로봇 통합
  - `SOArmDualManager.cs` — 두 로봇 통합
  - `SmartFactoryUI.cs` — UI
- **`raspberry_pi/robot_server_dual.py`** — TCP 서버 (LeRobot SDK 기반)
- **`urdf/SO101_unity/`** — URDF + DAE 메시 파일
- **`docs/SETUP.md`** — 상세 설치 가이드
- **`README.md`**, **`LICENSE`**, **`.gitignore`**

---

## 🚀 Getting Started

자세한 설치 가이드는 [`docs/SETUP.md`](docs/SETUP.md)를 참고하세요.

### Raspberry Pi (Server)

LeRobot 설치 후 캘리브레이션을 진행하고 서버를 실행합니다.

- LeRobot SDK + Feetech 드라이버 설치
- PyTorch ARM CPU 빌드 설치 (필수)
- 각 로봇 캘리브레이션 (`--robot.id=robot1`, `--robot.id=robot2`)
- 서버 실행: `python raspberry_pi/robot_server_dual.py`

### Unity (Client)

- Unity 6000.4.3f1 프로젝트 생성
- URDF Importer 설치 (Package Manager → Git URL)
- `urdf/SO101_unity/so101.urdf` 임포트
- `unity/Assets/Scripts/` 스크립트 추가
- Robot1_Group / Robot2_Group 씬 구성
- Play 버튼 실행

---

## 📡 Communication Protocol

Unity → Raspberry Pi 방향으로 TCP 소켓 위에 JSON 메시지를 전송합니다.

메시지 필드:

- **mode**: `robot1` / `robot2` / `mirror` — 제어 대상
- **motor**: `shoulder_pan` / `shoulder_lift` / `elbow_flex` / `wrist_flex` / `wrist_roll` / `gripper` — 모터 이름
- **value**: -100 ~ 100 (float) — 정규화된 위치 값

서버는 명령을 받아 LeRobot SDK의 `FeetechMotorsBus.write()`로 전달합니다.

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
| **Overload Error** | 모터 과부하 또는 케이블 | 전원 OFF/ON, 모터 스캔 재확인 |
| **Address already in use** | 이전 서버 프로세스 잔존 | `pkill -f robot_server_dual.py` |
| **PyTorch Illegal Instruction** | ARM 비호환 빌드 | ARM CPU 빌드 사용 |

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
