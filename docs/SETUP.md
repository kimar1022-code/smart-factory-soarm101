# 🛠️ 상세 설치 가이드

## 📋 사전 준비물

### 하드웨어
- [ ] SO-ARM101 로봇팔 2대 (조립 완료)
- [ ] Waveshare Bus Servo Adapter (A) 2개
- [ ] Raspberry Pi 4 (4GB+)
- [ ] 12V/5A 어댑터 2개
- [ ] USB-C 케이블 2개
- [ ] 마이크로 SD 카드 (32GB+)

### 소프트웨어
- [ ] Ubuntu 24.04 (라즈베리파이용)
- [ ] Unity 6.4 (Windows PC)
- [ ] Git
- [ ] Python 3.11+

---

## 🤖 Step 1: 라즈베리파이 초기 설정

### Ubuntu 설치
1. Raspberry Pi Imager 다운로드
2. Ubuntu 24.04 LTS 설치
3. SSH 활성화

### 패키지 업데이트
```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y git python3-pip python3-venv
```

---

## 🔧 Step 2: LeRobot 설치

```bash
# LeRobot 저장소 복제
cd ~
git clone https://github.com/huggingface/lerobot.git
cd lerobot

# 가상환경 생성
python3 -m venv ~/lerobot-env
source ~/lerobot-env/bin/activate

# LeRobot + Feetech 모터 드라이버 설치
pip install -e ".[feetech]"

# PyTorch ARM CPU 빌드 설치 (필수!)
# 일반 PyTorch는 ARM에서 Illegal Instruction 오류 발생
pip install torch==2.7.0 torchvision==0.22.0 \
    --index-url https://download.pytorch.org/whl/cpu
```

### 설치 확인
```bash
python -c "import torch; print(torch.__version__)"
# 출력: 2.7.0
```

---

## ⚙️ Step 3: 보드 점퍼 설정

**중요**: Waveshare 보드의 점퍼는 반드시 **B 위치 (USB-SERVO)**에 있어야 합니다!

```
[Board]
  [USB Mode] = A 위치 (X)
  [USB-SERVO] = B 위치 (✓)
```

---

## 🔌 Step 4: USB 포트 확인

```bash
ls -la /dev/serial/by-id/
```

출력 예시:
```
usb-1a86_USB_Single_Serial_5B14112388-if00 -> ../../ttyACM0
usb-1a86_USB_Single_Serial_5B14029636-if00 -> ../../ttyACM1
```

> **팁**: `serial/by-id` 경로는 USB를 다시 꽂아도 변하지 않아 안정적입니다.

---

## 🎯 Step 5: 모터 ID 설정

각 로봇별로 1번씩만 진행. 모터를 1개씩 연결해서 ID 1~6 부여.

```bash
source ~/lerobot-env/bin/activate

python -m lerobot.scripts.lerobot_setup_motors \
    --robot.type=so100_follower \
    --robot.port=/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00
```

화면 안내에 따라:
1. Gripper 모터만 연결 → Enter
2. Wrist roll 모터 연결 → Enter
3. Wrist flex 모터 연결 → Enter
4. Elbow flex 모터 연결 → Enter
5. Shoulder lift 모터 연결 → Enter
6. Shoulder pan 모터 연결 → Enter

---

## 🎯 Step 6: 캘리브레이션

```bash
python -m lerobot.scripts.lerobot_calibrate \
    --robot.type=so100_follower \
    --robot.port=/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00 \
    --robot.id=robot1
```

진행 순서:
1. 로봇을 **모든 관절의 가운데 위치**로 이동
2. Enter 누름
3. 각 관절을 **전체 범위**로 천천히 움직임 (wrist_roll 제외)
4. Enter 눌러 종료

> **중요**: `--robot.id=robot1` 플래그를 꼭 붙여야 `robot1.json`으로 저장됩니다!

두 번째 로봇은 `--robot.id=robot2`로 설정.

캘리브레이션 파일 위치:
```
~/.cache/huggingface/lerobot/calibration/robots/so_follower/
├── robot1.json
└── robot2.json
```

---

## 📡 Step 7: 서버 코드 배치

```bash
# 서버 코드를 라즈베리파이에 복사
scp robot_server_dual.py sw@192.168.45.18:/home/sw/

# 또는 라즈베리파이에서 직접 작성
nano /home/sw/robot_server_dual.py
```

---

## 🎮 Step 8: Unity 프로젝트 설정

### Unity 6.4 설치
- Unity Hub에서 **6000.4.3f1 (LTS)** 설치

### 새 프로젝트 생성
- Template: **Universal 3D**
- 프로젝트 이름: `SmartFactory`

### URDF Importer 설치
1. Window → Package Manager 열기
2. ⊕ 버튼 → **Add package from git URL...**
3. URL 입력:
   ```
   https://github.com/Unity-Technologies/URDF-Importer.git?path=/com.unity.robotics.urdf-importer
   ```
4. Add 클릭

### URDF 임포트
1. `urdf/SO101_unity` 폴더를 Unity의 Assets로 드래그
2. `so101.urdf` 파일 우클릭
3. **Import Robot from Selected URDF file** 클릭
4. 옵션:
   - Mesh Type: **Visual Only**
   - Axis Type: **Y Axis**
5. **Import** 클릭

### 스크립트 추가
`unity/Assets/Script/` 폴더 안의 10개 `.cs` 파일을 Unity 프로젝트로 복사:

```
SOArmJointConfig.cs
SOArmPresets.cs
ISOArmController.cs
SOArmMotorMapper.cs
SOArmSocketClient.cs
SOArmSimController.cs
SOArmRealController.cs
SOArmManager.cs
SOArmDualManager.cs
SmartFactoryUI.cs
```

### 씬 구성

#### 1. SmartFactoryManager (빈 GO)
- `SOArmDualManager` 컴포넌트 추가
- `SmartFactoryUI` 컴포넌트 추가

#### 2. Robot1_Group (빈 GO)
- `SOArmSocketClient` 추가
  - Server IP: `192.168.45.18` (라즈베리파이 IP)
  - Server Port: `5000`
- `SOArmRealController` 추가
  - Robot Server Mode: `robot1`
- `SOArmManager` 추가
  - Mode: `Mirror`
  - Auto Connect Real: ✓

#### 3. Robot_1 (URDF 임포트한 로봇)
- `SOArmSimController` 추가
- 각 J1~J6 ArticulationBody를 Joints 배열에 드래그

#### 4. Robot_1 → SOArmManager 연결
- SOArmManager의 Sim 슬롯: Robot_1 드래그
- SOArmManager의 Real 슬롯: Robot1_Group 드래그

#### 5. Robot 2도 동일하게 (mode: robot2)

#### 6. SOArmDualManager 슬롯 연결
- Robot1: Robot1_Group의 SOArmManager
- Robot2: Robot2_Group의 SOArmManager

---

## ✅ Step 9: 테스트

### 1. 서버 실행 (라즈베리파이)
```bash
ssh sw@192.168.45.18
source ~/lerobot-env/bin/activate
python /home/sw/robot_server_dual.py
```

출력:
```
로봇 1 연결중...
로봇 2 연결중...
두 로봇 연결 완료!
유니티 연결 대기중... (포트 5000)
```

### 2. Unity Play
1. Unity에서 **▶ Play 버튼** 클릭
2. Game 화면에 슬라이더 UI 표시 확인
3. 슬라이더 움직이면 시뮬과 실제 로봇이 동시에 움직임

---

## 🎉 완료!

이제 Unity에서 두 대의 SO-ARM101을 동시에 제어할 수 있습니다!
