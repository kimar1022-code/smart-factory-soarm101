# H/W Architecture

| **과정명** | **Unity 활용 DT 로봇 분야 개발자 양성과정 1기** |
| --- | --- |
| **프로젝트명** | SO-ARM101 스마트팩토리 로봇팔 디지털 트윈 | **문서 버전** | v2.0 |
| **팀명** | SO-ARM101 (개인 프로젝트) | **작성일** | 2026-08-01 |
| **작성자** | 김애리 | **최종 수정** | 2026-08-01 |
| **문서 종류** | H/W Architecture / BOM / 기구 명세 / 배선 명세 / 안전 한계 |

---

## 변경 이력

```
8/1 - v2.0 최초 작성 (Confluence 팀 문서 체계 적용)
8/1 - PincOpen 손가락 가동범위 -69.9° → -48.5° 로 정정 (URDF 리밋 -0.8465 rad 실측)
8/1 - 손가락 간격 실측표를 구동축 -48.5° 기준으로 재표기 필요 항목으로 강등
8/1 - 링크 치수·질량을 URDF 원본에서 재계산해 전량 대조
```

## 범례

> * ✅ 확인 완료 (URDF / 씬 / 실측 근거 있음)
> * 🟡 코드·문서 근거는 있으나 실물 대조 안 됨
> * ⬜ 미적용 / 미수행
> * 🔺 문서와 실제 값이 불일치
> * ⚠️ 미확인 — 확인하지 못함. 추측으로 채우지 않았음

## 출처 표기 규칙

| **표기** | **뜻** |
| --- | --- |
| `URDF` | `F:\UNITY\LeRobot\Assets\SO101_unity\so101.urdf` 직접 읽음 |
| `SCENE` | `F:\UNITY\LeRobot\Assets\Scenes\LeRobot.unity` 직렬화 값 |
| `CODE` | `Assets/Script/*.cs` 상수 |
| `공식 노트북` | PincOpen 저장소 `flash_and_tests/flash_test.ipynb` |
| `ROS2 xacro` | CNURobotics `urdf/gripper_macros.xacro` |
| `STL 실측` | 메시 파일을 직접 파싱해 경계값을 잰 결과 |
| `PROJECT_NOTES.md` | `F:\UNITY\LeRobot\PROJECT_NOTES.md` |

**단위:** 길이 = mm (URDF 원본 m × 1000) / 각도 = 도(°) (URDF 원본 rad × 180/π, 원본 rad 병기)

---

# 1. H/W Architecture — 물리 구성도

```
┌──────────────────────────────────────────────────────────────────────┐
│  💻 Windows PC — Unity 호스트                                        │
│     Unity 6.4 · F:\UNITY\LeRobot · SOArmSocketClient (TCP 클라이언트) │
└────────────────────────────┬─────────────────────────────────────────┘
                             │ LAN / Wi-Fi
                             │ TCP 포트 5000 · NDJSON
                             │ 송신 10 Hz / 폴링 30 Hz
┌────────────────────────────▼─────────────────────────────────────────┐
│  🥧 Raspberry Pi 4 (4 GB) — hostname: Apollon                        │
│     Ubuntu 24.04 LTS                                                 │
│     robot_server_dual.py  (TCP 서버 · 1 프로세스 / 1 포트)            │
│     ~/lerobot-env  (LeRobot SDK · PyTorch 2.7.0 ARM CPU 빌드)        │
│     ~/.cache/huggingface/lerobot/calibration/robots/so_follower/     │
│         ├─ robot1.json                                               │
│         └─ robot2.json                                               │
└───────────┬──────────────────────────────────┬───────────────────────┘
            │ USB (포트 4개 중 2개 사용)        │
            │ /dev/serial/by-id/               │ /dev/serial/by-id/
            │ usb-1a86_USB_Single_Serial_      │ usb-1a86_USB_Single_Serial_
            │ 5B14112388-if00                  │ 5B14029636-if00
┌───────────▼──────────────┐      ┌────────────▼─────────────┐
│ 🔧 Waveshare Bus Servo   │      │ 🔧 Waveshare Bus Servo   │
│    Adapter (A) #1        │      │    Adapter (A) #2        │
│    ⚠️ 점퍼 = B (USB-SERVO)│      │    ⚠️ 점퍼 = B           │
│    USB ↔ TTL (칩셋 1a86) │      │                          │
└───────────┬──────────────┘      └────────────┬─────────────┘
            │ 3선 데이지 체인                   │ 3선 데이지 체인
            │ VCC(12V) · GND · Signal          │
            │ TTL 반이중 · 1 Mbps              │
┌───────────▼──────────────┐      ┌────────────▼─────────────┐
│ 🔗 모터버스 #1 (로봇 1)   │      │ 🔗 모터버스 #2 (로봇 2)   │
│  ID1 shoulder_pan        │      │  ID1 ~ ID6               │
│  ID2 shoulder_lift       │      │  STS3215 × 6             │
│  ID3 elbow_flex          │      │  (동일 구성)              │
│  ID4 wrist_flex          │      │                          │
│  ID5 wrist_roll          │      │                          │
│  ID6 gripper (PincOpen)  │      │                          │
└──────────────────────────┘      └──────────────────────────┘
            ▲                                  ▲
            └──────────── ⚡ 12 V DC ──────────┘
                     (⚠️ 전류 용량 미확인)

  📷 손목 카메라 — ⚠️ 개수 · 연결 위치 · 작동 여부 전부 미확인
```

## 1.1 왜 어댑터가 필요한가

PC·라즈베리파이의 USB는 **USB 프로토콜**로 말하고, 모터는 **TTL 시리얼**로 말한다.
언어가 달라 직접 붙일 수 없다. Waveshare 어댑터가 그 사이에서 **통역**을 한다.

또 하나 — STS3215는 **반이중(half-duplex) 1선 통신**이다.
말하는 선과 듣는 선이 같아서 보낼 때와 받을 때 방향을 전환해야 하며, 어댑터가 이 전환을 담당한다.
⚠️ 어댑터 내부 회로 세부는 미확인. 일반적인 버스 서보 어댑터 동작 기준.

---

# 2. BOM (Bill of Materials)

| **분류** | **부품** | **수량** | **사양** | **상태** |
| --- | --- | --- | --- | --- |
| 로봇팔 | SO-ARM101 | 2 | 3D 프린팅, 6-DOF, 조립 완료 | ✅ |
| 액추에이터 | Feetech **STS3215** | 12 | 12 V, TTL 시리얼, baudrate **1,000,000**, 모델번호 **777** | ✅ |
| 인터페이스 | Waveshare Bus Servo Adapter (A) | 2 | USB ↔ TTL, 점퍼 **B 위치 필수** | ✅ |
| 제어 컴퓨터 | Raspberry Pi 4 | 1 | 4 GB RAM, Ubuntu 24.04 LTS, hostname `Apollon`, USB 4포트 | ✅ |
| 호스트 | Windows PC | 1 | Unity 6.4 | ✅ |
| 엔드이펙터 | **PincOpen** 그리퍼 | ⚠️ | 평행 4절 링크. Pollen Robotics, CC BY-SA 4.0 | ⚠️ 실물 장착 대수 미확인 (씬에는 2대 모두 이식) |
| 결합부 | Interface_ARM100 어댑터 | ⚠️ | 두께 **8.117 mm** × 51 × 24 mm | ✅ 치수 / ⚠️ 실물 수량 |
| 센서 | 손목 카메라 | ⚠️ | USB 웹캠 추정 | ⚠️ 미확인 |
| 전원 | 12 V DC | ⚠️ | 전류 용량·어댑터 모델 미확인 | ⚠️ 미확인 |

---

# 3. 기구 명세 — 6-DOF 링크 체인

## 3.1 링크 체인 (URDF 기준)

```
base_link                         147.0 g   (고정 — 책상/베이스 플레이트)
  │
  ├ ⚙️ shoulder_pan · revolute · 모터 ID 1 · 축 Z
  │    origin (38.84, 0, 62.40) mm
  ▼
shoulder_link                     100.0 g
  │
  ├ ⚙️ shoulder_lift · revolute · 모터 ID 2 · 축 Z
  │    origin (−30.40, −18.28, −54.20) mm
  ▼
upper_arm_link                    103.0 g
  │
  ├ ⚙️ elbow_flex · revolute · 모터 ID 3 · 축 Z
  │    origin (−112.57, −28.00, 0) mm
  ▼
lower_arm_link                    104.0 g
  │
  ├ ⚙️ wrist_flex · revolute · 모터 ID 4 · 축 Z
  │    origin (−134.90, 5.20, 0) mm
  ▼
wrist_link                         79.0 g
  │
  ├ ⚙️ wrist_roll · revolute · 모터 ID 5 · 축 Z
  │    origin (0, −61.10, 18.10) mm
  ▼
gripper_link                       87.0 g
  │
  ├ 📍 gripper_frame_joint · fixed
  │    origin (−7.90, −0.22, −98.13) mm
  │    └▶ gripper_frame_link (TCP, 질량 1e−9 kg 더미)
  │        ⚠️ 순정 조 끝 기준 — PincOpen 손끝으로 옮겨야 함
  │
  └ 🔩 pincopen_mount · fixed
       origin (0, 0, −8.117) mm · rpy (0, 90°, 0)
       ▼
     pincopen_adapter_link          15.0 g   (Interface_ARM100)
       │
       └ 🔩 pincopen_base_joint · fixed · origin (0, 0, 0)
            ▼
          pincopen_base_link        80.0 g   ※ STS3215 ID 6 이 이 메시에 포함
            │
            ├ ⚙️ gripper · revolute ★구동축 · 모터 ID 6 · 축 Z
            │    origin (53.492, +32.50, −5.00) mm
            │    ▼
            │  pincopen_left_proximal_link   20.0 g   🟩 구동
            │    │
            │    └ ↔️ pincopen_left_distal_joint · 종동
            │         origin (37.848, +12.944, −1.00) mm
            │         ▼
            │       pincopen_left_distal_link  10.0 g  🟨 종동
            │
            └ ↔️ pincopen_right_proximal_joint · 종동
                 origin (53.492, −32.50, −5.00) mm
                 ▼
               pincopen_right_proximal_link  20.0 g   🟨 종동
                 │
                 └ ↔️ pincopen_right_distal_joint · 종동
                      origin (37.848, −12.944, −1.00) mm
                      ▼
                    pincopen_right_distal_link 10.0 g 🟨 종동

  🟩 = 모터가 직접 도는 축 (구동축)
  🟨 = 기계적으로 따라 도는 축 (종동축) — 소프트웨어가 미러링
```

## 3.2 관절별 모터 ID / 가동범위

| **관절** | **모터 ID** | **URDF 조인트명** | **축** | **URDF 리밋 (rad)** | **환산 (°)** | **씬 설정값 (°)** | **일치** |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 베이스 좌우 회전 | **1** | `shoulder_pan` | Z | −1.91986 ~ 1.91986 | −110.00 ~ 110.00 | −110 ~ 110 | ✅ |
| 어깨 상하 | **2** | `shoulder_lift` | Z | −1.74533 ~ 1.74533 | −100.00 ~ 100.00 | −100 ~ 100 | ✅ |
| 팔꿈치 | **3** | `elbow_flex` | Z | −1.69 ~ 1.69 | −96.83 ~ 96.83 | −96.8 ~ 96.8 | ✅ |
| 손목 상하 | **4** | `wrist_flex` | Z | −1.65806 ~ 1.65806 | −95.00 ~ 95.00 | −95 ~ 95 | ✅ |
| 손목 회전 | **5** | `wrist_roll` | Z | −2.74385 ~ 2.84121 | −157.21 ~ 162.79 | −157.2 ~ 162.8 | ✅ **비대칭** |
| 그리퍼 (PincOpen) | **6** | `gripper` | Z | **−0.8465 ~ 0** | **−48.50 ~ 0** | **−48.5 ~ 0** | ✅ |

> **홈 포즈: 모든 관절 0°** — LeRobot 캘리브레이션이 "가운데 = 0" 으로 잡기 때문 (`PROJECT_NOTES.md`).
> 단, PincOpen J6 의 홈은 `FingerOpenDeg = 0°` = **열림** 상태다 (`CODE` `SOArmPresets`).
>
> ⚠️ 모든 revolute 조인트의 `effort=10`, `velocity=10` 은 **URDF 기본값**이며
> STS3215 의 실제 정격 토크·속도와 대응하지 않는다. **동역학 계산에 쓰지 말 것.**
>
> ⚠️ J6 리밋은 **손가락 각도**다. 모터 각도가 아니다 (§4.6).

## 3.3 링크 치수 — 조인트 원점 간 거리

URDF의 `<joint><origin xyz>` 벡터 크기를 직접 계산한 값이다.

| **구간** | **origin 벡터 (mm)** | **거리 (mm)** | **의미** |
| --- | --- | --- | --- |
| base → shoulder | (38.84, 0, 62.40) | **73.50** | 베이스 높이 |
| shoulder → upper_arm | (−30.40, −18.28, −54.20) | **64.78** | 어깨 오프셋 |
| upper_arm → lower_arm | (−112.57, −28.00, 0) | **116.00** | **상완 (upper arm)** |
| lower_arm → wrist | (−134.90, 5.20, 0) | **135.00** | **전완 (forearm)** |
| wrist → gripper_link | (0, −61.10, 18.10) | **63.73** | 손목 |
| gripper_link → TCP | (−7.90, −0.22, −98.13) | **98.44** | 순정 조 길이 |
| **산술 합** | — | **≈ 551.05** | 최대 도달거리 **상한** |

> ⚠️ 551 mm 는 모든 링크를 일직선으로 폈을 때의 **산술 합**이다.
> 관절 리밋 때문에 실제로 이 자세는 나오지 않으므로 **상한값으로만** 참고할 것.
> 정확한 작업 영역(workspace)은 순기구학(FK) 계산이 필요하며 **미수행**이다.

## 3.4 질량 (URDF `<inertial><mass>`)

| **링크** | **질량 (g)** |
| --- | --- |
| `base_link` | 147.0 |
| `shoulder_link` | 100.0 |
| `upper_arm_link` | 103.0 |
| `lower_arm_link` | 104.0 |
| `wrist_link` | 79.0 |
| `gripper_link` | 87.0 |
| **팔 본체 소계** | **620.0** |
| `pincopen_adapter_link` | 15.0 |
| `pincopen_base_link` | 80.0 |
| `left/right_proximal` | 20.0 × 2 = 40.0 |
| `left/right_distal` | 10.0 × 2 = 20.0 |
| **그리퍼 소계** | **155.0** |
| **합계 (팔 1대)** | **≈ 775 g** |

> ⚠️ **PincOpen 부품의 질량·관성(inertia)은 형상 크기에 맞춘 대략값이다.**
> MJCF 원본이 명백한 임시값이라 쓰지 않았다고 URDF 주석(L331~332)에 명시되어 있다.
> **동역학 계산(중력 보상, 토크 예측)에 쓰지 말 것.**

---

# 4. PincOpen 그리퍼 상세

## 4.1 왜 PincOpen 으로 바꿨나

순정 SO-ARM101 그리퍼(`moving_jaw_so101_v1`)는 **한쪽 조만** 움직이는 단순 구조다.
**PincOpen** 은 평행 4절 링크라 **두 손가락이 항상 평행하게** 맞물린다.
평행하게 물면 물체가 미끄러지거나 회전하지 않아 pick & place 에 유리하다.

## 4.2 Interface_ARM100 어댑터 — 8.117 mm

| **항목** | **값** | **출처** |
| --- | --- | --- |
| 두께 | **8.117 mm** | `STL 실측` (원본 CAD X 범위 `−61.609 ~ −53.492` mm) |
| 폭 × 높이 | 51 × 24 mm | `URDF` L366 |
| 질량 (URDF) | 15 g | `URDF` L373 |

**⭐ 자동 정렬이 되는 이유 (조정 불필요):**

어댑터 X 최대값 `−53.492` 와 PincOpen 조인트 원점 `0.053492` (= 53.492 mm)가 **정확히 같은 수**다.
즉 MuJoCo 팀이 `base` 메시를 재중심화할 때 쓴 기준면이 **어댑터 결합면**이었다.

→ 어댑터를 **+53.492 mm** 평행이동하면 `−8.117 ~ 0` 이 되어 base 뒷면(`x=0`)과 정확히 맞물린다.
이 평행이동은 **DAE 변환 시 메시에 미리 구워넣었다.** 따라서 아래 두 값은 **0이 맞고 건드리면 안 된다.**

```
pincopen_adapter_link 의 visual origin = 0 0 0
pincopen_base_joint   의 origin        = 0 0 0
```

## 4.3 장착 좌표 유도 (추측 아님)

```xml
<joint name="pincopen_mount" type="fixed">
  <origin xyz="0 0 -0.008117" rpy="0 1.5708 0"/>
  <parent link="gripper_link"/>
  <child  link="pincopen_adapter_link"/>
</joint>
```

| **항목** | **유도 근거** |
| --- | --- |
| 회전 `rpy="0 1.5708 0"` (Y축 +90°) | 순정 TCP(`gripper_frame_joint`)가 `z = −0.0981` → 공구 방향은 `gripper_link` 의 **−Z**. PincOpen 은 **+X** 가 손가락 방향. **+X 를 −Z 로 보내는 회전 = Y축 +90°** |
| 이동 `z = −0.008117` | 어댑터 두께 8.117 mm 만큼 뒤로 물림 |

## 4.4 장착 검증 결과

| **항목** | **측정** | **판정** |
| --- | --- | --- |
| 어댑터 두께 | 8.1 mm | STL 실측 8.117 mm 과 일치 ✅ |
| PincOpen base 크기 | 98.6 × 59.4 × 57.5 mm | 실물 치수 일치 ✅ |
| 손목 ↔ 어댑터 빈틈 | **0.0 mm** (접촉) | ✅ |
| 좌우 정렬 | 대칭 | ✅ |
| 커플링 자동 연결 | 5개 링크 전부 | ✅ |
| 자로 잰 실측 대조 | — | ⬜ **미수행** (렌더링 기준 검증만) |

## 4.5 평행 4절 링크 구조와 커플링 배율

```
        pincopen_base_link (고정 프레임)
        ┌───────────────────────────────┐
        │  좌 피벗 y=+32.5    우 피벗 y=−32.5 │
        └────┬──────────────────┬───────┘
             │ ⚙️ 구동축 θ       │ ↔️ ×(−1.0)
             │   (모터 ID 6)     │
             ▼                  ▼
       left_proximal      right_proximal
             │                  │
             │ ↔️ ×(−1.0)        │ ↔️ ×(+1.0)
             │ 피벗              │ 피벗
             │ (37.848,+12.944)  │ (37.848,−12.944)
             ▼                  ▼
        left_distal ◀─평행 유지─▶ right_distal
        (= 왼쪽 손가락)          (= 오른쪽 손가락)
```

**커플링 배율 (`MJCF_Full` 프리셋, 2026-08-01 확정) — `CODE` `PincOpenCoupling`**

```
left_distal    = θ × (−1.0)
right_proximal = θ × (−1.0)
right_distal   = θ × (+1.0)
```

**왜 ×1.0 인가 (ROS2 문헌은 ×0.5):**

두 문헌은 **기준 관절이 다를 뿐** 모순이 아니다.
ROS2 xacro 는 네 관절을 전부 *모터축* 기준으로 ±0.5배로 적는데,
이 프로젝트 URDF 의 구동축은 모터축이 아니라 **왼쪽 proximal** (= ROS2 의 `base_link_to_left_arm`)이고
이 관절 자체가 이미 모터의 −0.5배다. 모터 `M = −2θ` 로 환산하면 ×1.0 의 (−1, −1, +1) 이 된다.

**렌더링 교차검증:** ×1.0 에서만 손가락 패드가 서로 **평행**하게 맞물린다. ×0.5 는 끝만 뾰족하게 모인다.

## 4.6 ⭐ 모터 각도 vs 손가락 각도 — 반드시 구분할 것

> **이 프로젝트에서 가장 헷갈리기 쉬운 지점이다.**

| | **모터 각도** | **손가락 각도 (구동축)** |
| --- | --- | --- |
| 무엇 | STS3215 ID 6 이 실제로 도는 각도 | URDF `gripper` 조인트 = `left_proximal` 이 도는 각도 |
| 열림 | **−140°** | **0°** |
| 닫힘 | **0°** | **−48.5°** |
| 하드 리밋 | **−147°** | (해당 없음) |
| 가동폭 | 140° | **48.5°** (= 0.8465 rad) |
| 출처 | `공식 노트북` `set_goal_position(-140)` / `set_min_angle_limit(-147)` | **메시 정점 실측** — `CODE` `PincOpenCoupling.FingerClosedDeg = -48.5f`, `URDF` `lower="-0.8465"` |
| 부호 확정 근거 | — | 렌더링 검증 (**음수 = 닫힘**) |

**왜 48.5° 인가 (ROS2 의 69.9° 를 쓰지 않는 이유):**

메시 정점을 base 로컬좌표로 변환해 좌우 손가락 최단거리를 잰 결과,
**−48.9° 에서 두 패드가 정확히 맞닿는다.** 여기에 0.4° 여유를 둔 값이 −48.5° 다.

ROS2 xacro 의 1.22 rad (69.9°) 를 그대로 쓰면 **손가락이 서로 22 mm 파고든다.**
그 값은 캠을 포함한 실제 4절 링크를 *모터축* 기준으로 잰 것이라
MuJoCo 메시의 0점(= 벌어진 자세)과 기준이 다르기 때문이다.
MJCF 의 0.77 rad (44.1°) 가 이 실측치(48.9°)에 가깝다.

> 🔺 **구 문서 정정:** `docs/PINCOPEN_INTEGRATION.md` §7 과 `docs/HW_ARCHITECTURE.md`(v1)의
> **−69.9°** 는 폐기된 값이다. 현재 코드·URDF·씬 모두 **−48.5°** 다.

## 4.7 손가락 간격

| **명령 %** | **구동축 각도** | **손가락 간격** | **상태** |
| --- | --- | --- | --- |
| 100 % (완전 열림) | 0.0° | **약 66 mm** | 🟡 `CODE` 주석 근거 ("MuJoCo 메시 rest pose, 개구부 약 66 mm") |
| 0 % (완전 닫힘) | **−48.5°** | 좌우 패드 접촉 (약 0 mm 간극) | 🟡 메시 실측 −48.9° 접촉 + 0.4° 여유 |
| 150 % (범위 초과) | **0.0° 로 잘림** | — | ✅ 클램프 동작 확인 |

> 🔺 **구 자체검증표 폐기:** `PINCOPEN_INTEGRATION.md` §7 의
> `100 %→94.6 mm / 0 %→−69.9°, 22.9 mm` 표는 **−69.9° 리밋 시절의 값**이라 현재와 맞지 않는다.
> `Tools ▸ SO-ARM ▸ 그리퍼 구동 자체검증` 을 **−48.5° 기준으로 재실행해 표를 갱신해야 한다.** ⬜

## 4.8 🔴 모터 중복 렌더링 제거

PincOpen `base` 메시 안에 **STS3215(ID 6)가 이미 포함**되어 있다.
순정 `gripper_link` 의 `sts3215_03a_v1` 을 그대로 두면 화면에 모터가 두 개로 보인다.
실물은 하나이므로 순정 쪽을 **주석 처리**했다 (삭제 아님 — 되돌릴 수 있게).

씬 마이그레이션 시에는 `sts3215_03a_v1` 과 `wrist_roll_follower_so101_v1` 시각 메시를 제거한다
(`PincOpenMainSceneMigrator.RemoveVisualChild()`).

---

# 5. 전원 · 배선 · 포트 명세

## 5.1 전원

| **항목** | **값** | **상태** |
| --- | --- | --- |
| 모터 구동 전압 | **12 V DC** | ✅ |
| 전류 용량 | — | ⚠️ **미확인** |
| 전원 어댑터 모델 | — | ⚠️ **미확인** |
| 라즈베리파이 전원 | 별도 USB-C 추정 | ⚠️ **미확인** |
| 전원 인가/차단 순서 규정 | — | ⚠️ **미확인** |
| 12 V 계통 ↔ 라파 5 V 계통 GND 공통 여부 | — | ⚠️ **미확인** |

> ⚠️ 시리얼 통신에서 GND 가 분리되면 통신이 불안정해진다. **실물 확인이 필요하다.**

## 5.2 통신 배선 명세

| **구간** | **연결 방식** | **케이블/신호** | **비고** |
| --- | --- | --- | --- |
| PC ↔ 라파 | Ethernet / Wi-Fi | TCP 포트 5000 | NDJSON |
| 라파 ↔ 어댑터 #1 | USB | CH34x (VID 1a86) | `/dev/serial/by-id/...5B14112388-if00` |
| 라파 ↔ 어댑터 #2 | USB | CH34x (VID 1a86) | `/dev/serial/by-id/...5B14029636-if00` |
| 어댑터 ↔ 모터버스 | 3핀 데이지 체인 | VCC(12 V) · GND · Signal | TTL 반이중, 1 Mbps |
| 모터 ↔ 모터 | 3핀 데이지 체인 | 동일 | 각 모터에 IN/OUT 커넥터 2개 |
| 12 V 전원 ↔ 어댑터 | DC 배럴 | 12 V | 2개 어댑터에 각각 공급 |

**데이지 체인(daisy chain)** 이란 부품을 한 줄로 줄줄이 이어 붙이는 배선 방식이다.
STS3215 는 입출력 커넥터가 2개씩 있어 모터 → 모터 → 모터 로 이어 붙일 수 있다.
선 하나로 6개를 다 연결할 수 있어 배선이 단순해진다.

**대신 각 모터가 자기 주소(ID)를 가져야 한다.** 같은 ID 가 두 개면 통신이 충돌한다.
실제로 `Missing motor IDs: 1, 6` 장애를 겪었고 `setup_motors` 로 ID 를 재설정해 해결했다.

> ⚠️ **`setup_motors` 주의:** Enter 를 칠 때마다 다음 모터로 넘어간다.
> 한 모터만 바꾸려면 원하는 단계 직후 **Ctrl+C** 로 빠져나와야 한다.

## 5.3 포트 경로 (⚠️ 반드시 by-id 사용)

```
로봇1: /dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00
로봇2: /dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14029636-if00
```

`ttyACM0` / `ttyACM1` 은 **꽂는 순서와 재부팅에 따라 번호가 바뀐다.**
`by-id` 경로는 USB 장치의 **시리얼 번호**로 만들어지므로 절대 안 바뀐다.
`5B14112388` / `5B14029636` 이 각 어댑터의 고유 시리얼이다.

## 5.4 점퍼 설정

| **부품** | **점퍼 위치** | **결과** |
| --- | --- | --- |
| Waveshare Bus Servo Adapter (A) | **B (USB-SERVO)** | ✅ 정상 통신 |
| 〃 | A | ❌ **통신 안 됨** |

## 5.5 캘리브레이션 파일

```
~/.cache/huggingface/lerobot/calibration/robots/so_follower/
├── robot1.json
└── robot2.json
```

캘리브레이션이란 "각 모터의 엔코더 몇 번 카운트가 0° 인가" 를 기록해 둔 파일이다.
로봇마다 조립 오차가 달라 이 값이 다르다.
**⚠️ 재캘리브레이션 시 `--robot.id` 를 반드시 지정**해야 엉뚱한 로봇 파일을 덮어쓰지 않는다.

## 5.6 라즈베리파이 소프트웨어 환경

| **항목** | **값** |
| --- | --- |
| OS | Ubuntu 24.04 LTS |
| 가상환경 | `~/lerobot-env` (반드시 activate 후 실행) |
| LeRobot 소스 | `/home/sw/lerobot/.venv` (uv 관리) |
| 서버 파일 | `/home/sw/robot_server_dual.py` |
| **PyTorch** | **2.7.0 + torchvision 0.22.0 (ARM CPU 빌드)** |
| SSH | `ssh sw@<IP>` (계정 `sw`) |

> ⚠️ **일반 PyTorch 를 설치하면 `Illegal Instruction` 으로 죽는다.**
> 라즈베리파이 CPU(ARM Cortex-A72)에 없는 명령어가 x86용 휠에 들어 있기 때문이다.
> ```bash
> pip install torch==2.7.0 torchvision==0.22.0 --index-url https://download.pytorch.org/whl/cpu
> ```

**실행 순서:**
```bash
ssh sw@<IP>
source ~/lerobot-env/bin/activate
pkill -f robot_server_dual.py        # 기존 서버 정리 (포트 5000 점유 방지)
python /home/sw/robot_server_dual.py
```

정상 출력:
```
로봇 1 연결중...
로봇 2 연결중...
두 로봇 연결 완료!
유니티 연결 대기중... (포트 5000)
```

> 🔺 IP 기록이 문서마다 다르다: `PROJECT_NOTES.md` = `192.168.75.245`,
> `SOArmSocketClient.cs` 기본값 = `192.168.45.18`,
> **Unity 씬 실제 저장값 = `192.168.75.245`**. 안 되면 라파에서 `hostname -I` 로 재확인할 것.

---

# 6. 하드웨어 안전 한계

## 6.1 전기적 한계

| **항목** | **값** | **위반 시** |
| --- | --- | --- |
| 모터 공급 전압 | **12 V DC** | 과전압 시 모터 손상 |
| 통신 baudrate | **1,000,000 bps** | 불일치 시 통신 실패 |
| 모터 모델번호 | 777 (스캔 식별값) | — |
| 어댑터 점퍼 | **B (USB-SERVO)** | A 면 통신 불가 |
| 모터 ID 중복 | **금지** | `Missing motor IDs` 오류 |

## 6.2 각도 한계 (소프트 리밋)

| **관절** | **소프트 리밋 (°)** | **적용 위치** |
| --- | --- | --- |
| `shoulder_pan` | −110 ~ 110 | `SOArmSimController.SetJointTarget()` / `SOArmRealController.SetJointTarget()` / `SOArmMotorMapper` |
| `shoulder_lift` | −100 ~ 100 | 〃 |
| `elbow_flex` | −96.8 ~ 96.8 | 〃 |
| `wrist_flex` | −95 ~ 95 | 〃 |
| `wrist_roll` | −157.2 ~ 162.8 | 〃 |
| `gripper` (손가락) | **−48.5 ~ 0** | 위 + `PincOpenCoupling.SetDriveAngle()` |

## 6.3 그리퍼 전용 안전 한계 (5계층)

| **계층** | **항목** | **값** | **상태** |
| --- | --- | --- | --- |
| **1. 소프트웨어 게이트** | `PincOpenSafety.RealGripperEnabled` | **`false` (기본 잠금)** | ✅ 적용중 |
| 〃 | 게이트 통과 지점 | `SetGripperTarget()` **+ 송신 루프 재검사** | ✅ 2026-08-01 이중화 |
| **2. 행정 여유** | `TravelMarginPercent` | **5 %** (양 끝 각각) | ✅ 적용중 |
| **3. 범위 클램프** | 손가락 각도 | −48.5° ~ 0° | ✅ 적용중 |
| **4. URDF 리밋** | 모든 PincOpen 관절 | −0.8465 ~ 0 rad | ✅ 적용중 |
| **5. 펌웨어 각도 리밋** | `set_min_angle_limit` | **−147°** | ⬜ **미적용** |
| 〃 | `set_max_angle_limit` | **0°** | ⬜ **미적용** |
| **6. 펌웨어 보호 파라미터** | `torque_limit` | 1000 | ⬜ **미적용** |
| 〃 | `overload_torque` (초과 시 보호 발동) | 40 | ⬜ **미적용** |
| 〃 | `protective_torque` (발동 후 강하값) | 5 | ⬜ **미적용** |
| 〃 | `protection_time` | 7 (= 70 ms) | ⬜ **미적용** |
| 〃 | `acceleration` | 200 | ⬜ **미적용** |

> 출처: `PincOpenSafety.cs` L22~33 (코드값 우선). 주석과 코드가 다른 부분은 **더 보수적인 코드값**을 채택했다.

**펌웨어 설정 코드 생성:** `PincOpenSafety.GetFirmwareSetupSnippet(motorId: 6)` 이 라즈베리파이용 Python 을 출력한다.

```python
# PincOpen 보호 설정 (ID 6) — 라파에서 1회 실행
# ⚠️ 레지스터 이름은 LeRobot 기준으로 먼저 확인할 것:
#    print(bus.model_ctrl_table['sts3215'].keys())
set_lock({6: 0})
set_acceleration({6: 200})
set_max_angle_limit({6: 0})
set_min_angle_limit({6: -147})
set_torque_limit({6: 1000})
set_overload_torque({6: 40})
set_protective_torque({6: 5})
set_protection_time({6: 7})
set_lock({6: 1})
```

> `set_lock({6: 0})` → 설정 잠금 해제, 값 쓰기, `set_lock({6: 1})` → 다시 잠금.
> 잠금을 안 걸면 전원을 껐다 켤 때 값이 날아갈 수 있다.

## 6.4 🔴 핵심 위험 — STS3215는 위치 제어 모드에서 토크 제한이 없다

**무슨 뜻인가:**
위치 제어(position control)는 "여기로 가라" 고 목표 각도만 준다.
모터는 목표에 도달할 때까지 **힘을 계속 키운다.**
물체를 물어서 더 못 가면 그래도 계속 힘을 준다 → **모터가 타거나 3D 프린팅 플라스틱이 부러진다.**

**대응 — 방어선 구성:**

```
 그리퍼 명령
     │
 ┌───▼─────────────────────────────────────────┐
 │ 🛡️ 소프트웨어 방어선 (적용중 ✅)             │
 │  ① 캘리브레이션 게이트                       │
 │     RealGripperEnabled = false → 기본 차단   │
 │  ② 게이트 이중화                             │
 │     SetGripperTarget() + 송신 루프 재검사     │
 │  ③ 행정 여유 5 % (끝단 회피)                 │
 │  ④ 범위 클램프 −48.5° ~ 0°                   │
 └───┬─────────────────────────────────────────┘
     │
 ┌───▼─────────────────────────────────────────┐
 │ 🔥 펌웨어 방어선 (⬜ 미적용)                  │
 │  ⑤ 각도 하드 리밋 −147° ~ 0°                 │
 │  ⑥ 과부하 보호 overload 40 → protective 5    │
 │     (70 ms 후 발동)                          │
 └───┬─────────────────────────────────────────┘
     ▼
  ⚙️ STS3215 ID 6
```

⚠️ **현재 ⑤⑥ 은 미적용이므로, 실물 그리퍼 명령은 잠긴 상태를 유지해야 한다.**

## 6.5 실물 그리퍼 잠금 해제 절차 (4단계)

`PincOpenSafety.RealGripperEnabled = true` 로 바꾸기 전 **반드시** 마쳐야 한다.

| **단계** | **작업** | **판정 기준** |
| --- | --- | --- |
| 1 | 토크 OFF 상태로 **손으로** 그리퍼를 열고 닫으며 위치를 읽는다 | — |
| 2 | 열림이 **−140°** 근처, 닫힘이 **0°** 근처인지 확인 | 아니면 ❌ **중단** |
| 3 | LeRobot 재캘리브레이션 (`--robot.id` **필수**) | — |
| 4 | 펌웨어 각도 리밋 굽기 (min −147 / max 0) | — |

> ⚠️ 이 절차의 원본 문서 `docs/PINCOPEN.md` 가 **존재하지 않는다.**
> 위 4단계는 `PincOpenSafety.cs` L43~47 과 `PINCOPEN_INTEGRATION.md` §8 에서 재구성한 것이다.
>
> 💡 방향이 반대로 움직이면 `PincOpenSafety.InvertDirection` 을 뒤집는다.
> 캘리브레이션 때 어느 쪽 끝을 먼저 잡았는지에 따라 −100 이 열림일 수도, 닫힘일 수도 있다.

## 6.6 운영 안전 규정 (⚠️ 사용자 지정 — 코드 근거 없음)

| **규정** | **이유** | **현재 구현** |
| --- | --- | --- |
| **12 V 인가 상태에서 토크 OFF 금지** | 토크를 끄면 중력으로 팔이 자유낙하 → 링크·기어 손상, 손 협착 위험 | `SetServoTorque(false)` API 는 존재하나 **UI 에 노출되지 않음**. 소프트웨어 차단은 ⬜ 미구현 |
| **직접교시(Teach) 시 토크 30 %** | 사람이 손으로 밀 수 있을 만큼 약하게, 그러나 팔이 떨어지지 않을 만큼은 유지 | ⬜ Teach 모드 자체가 미구현 |

> ⚠️ 위 두 규정은 **코드·기존 문서 어디에도 근거가 없다.** 사용자 구술 규정으로 기록한다.

## 6.7 알려진 하드웨어 장애와 대응

| **증상** | **원인** | **해결** |
| --- | --- | --- |
| 모터 일부 안 잡힘 (`Missing motor IDs: 1, 6`) | 두 모터가 같은 ID 보유 → 버스 충돌 | `setup_motors` 로 하나씩 연결해 ID 재설정. Enter 마다 다음 모터로 넘어가므로 원하는 단계 후 **Ctrl+C** |
| **Overload error** | 과부하 / 물리 간섭 | 로봇 전원 OFF/ON, 자세 정리 |
| 통신 자체가 안 됨 | 어댑터 점퍼 A 위치 | 점퍼를 **B (USB-SERVO)** 로 |
| 포트 번호가 재부팅마다 바뀜 | `ttyACM*` 는 열거 순서 기반 | `/dev/serial/by-id/` 사용 |
| `Address already in use` (포트 5000) | 이전 서버 프로세스 잔존 | `pkill -f robot_server_dual.py` |
| `Illegal Instruction` (Python 실행 시) | x86용 PyTorch 휠 | ARM CPU 빌드 2.7.0 설치 |
| 관절이 전혀 안 움직임 (시뮬) | URDF 임포터가 `stiffness = 0` 으로 둠 | `PincOpenCoupling.ConfigureDrives()` 실행. **펌웨어로 치면 레지스터 값만 쓰고 토크를 안 켠 상태** |
| 모델이 분홍색 | URP Material 미적용 | Material 수동 변환 |
| URDF Import `NullReferenceException` | vHACD 볼록분해기 크래시 | STL→DAE 변환 + `collision` 주석 + `convexMethod = unity` |

---

# 7. Unity 측 시뮬레이션 파라미터 (참고)

실물이 아니라 **Unity 물리엔진(PhysX)** 쪽 설정이지만, 실물 거동과 비교할 때 필요해 함께 기록한다.

| **항목** | **값** | **위치** |
| --- | --- | --- |
| `ArticulationBody.xDrive.stiffness` | 10000 | `SOArmSimController`, `PincOpenCoupling` (씬 실측 일치) |
| `damping` | 1000 | 〃 |
| `forceLimit` | 1000 | 〃 |
| `lowerLimit` / `upperLimit` | 관절별 min/max 각도 | `ConfigureArticulationBodies()` / `ConfigureDrives()` |

> ⚠️ 씬 파일에는 `stiffness: 0` 인 `ArticulationBody` 가 다수 존재한다.
> 이는 `SOArmSimController.joints[]` 나 `PincOpenCoupling` 에 **연결되지 않은 중간 링크**들로,
> 목표를 주지 않으므로 문제되지 않는다. 구동 대상 관절은 전부 10000 으로 채워져 있다.

---

# 8. ⚠️ 미확인 항목 정리

| **#** | **항목** | **확인 방법** |
| --- | --- | --- |
| 1 | 12 V 전원의 전류 용량 / 어댑터 모델 | 전원 어댑터 라벨 확인 |
| 2 | 12 V 계통과 라파 5 V 계통의 GND 공통 여부 | 배선 육안 확인 + 도통 시험 |
| 3 | 전원 인가/차단 순서 규정 | 운영 절차 수립 필요 |
| 4 | 손목 카메라 — 개수, 연결 위치, 작동 여부 | 라파에서 `ls /dev/video*`, `v4l2-ctl --list-devices` |
| 5 | PincOpen 실물 장착 대수 (씬에는 2대 다 이식됨) | 육안 확인 |
| 6 | 실물 그리퍼 캘리브레이션 상태 | §6.5 절차 1~2 단계 수행 |
| 7 | `pincopen_mount` 오프셋 실측 대조 | 버니어 캘리퍼스로 어댑터 두께 재측정 |
| 8 | STS3215 정격 토크 / 무부하 속도 / 감속비 | 데이터시트 확보 (URDF 의 `effort=10`, `velocity=10` 은 기본값이지 실제 사양 아님) |
| 9 | 실제 작업 영역(workspace) | 순기구학(FK) 계산 또는 실측. §3.3 의 551 mm 는 산술 상한일 뿐 |
| 10 | 어댑터 내부 반이중 방향전환 회로 | Waveshare 회로도 확보 |
| 11 | 라즈베리파이 전원 공급 사양 | 어댑터 라벨 확인 |
| 12 | 라즈베리파이 IP 확정값 | `hostname -I` (문서 3곳 불일치) |
| 13 | **손가락 간격 실측표 (−48.5° 기준 재측정)** | `Tools ▸ SO-ARM ▸ 그리퍼 구동 자체검증` 재실행 후 §4.7 갱신 |

---

# 9. 출처 · 라이선스

| **자산** | **출처** | **라이선스** |
| --- | --- | --- |
| SO-ARM101 URDF (`so101_new_calib`) | onshape-to-robot 생성. Onshape 문서 ID `7715cc284bb430fe6dab4ffd` (`URDF` L2~3) | ⚠️ 미확인 |
| PincOpen 메시 6종 | [pollen-robotics/PincOpen](https://github.com/pollen-robotics/PincOpen)<br>· `Interface_ARM100.stl` → `cad/stl/`<br>· 그리퍼 5개 → PR #6 (MuJoCo Simulation Support) `mujoco/assets/` | **CC BY-SA 4.0** |
| PincOpen 관절 정보 | 같은 PR 의 `mujoco/eef.xml` | **CC BY-SA 4.0** |
| 모터 각도 기준값 | PincOpen `flash_and_tests/flash_test.ipynb` | 〃 |
| 손가락 각도 참고값 (미채택) | CNURobotics ROS2 `urdf/gripper_macros.xacro` | ⚠️ 미확인 |
| LeRobot SDK | [huggingface/lerobot](https://github.com/huggingface/lerobot) | ⚠️ 미확인 (Apache-2.0 추정 — **확인 필요**) |

> 📌 **GitHub 공개 시 출처 표기 + 동일 라이선스(CC BY-SA 4.0) 유지 필요.**

---

# 10. 관련 문서

| **문서** | **내용** |
| --- | --- |
| `docs/v2/USER_REQUIREMENT.md` | UR / SR / Scenario / Validation / Constraints |
| `docs/v2/SW_ARCHITECTURE.md` | 계층 구조, 모듈 명세, 인터페이스 명세, 상태/시퀀스 다이어그램, 데이터 구조 |
| `docs/PINCOPEN_INTEGRATION.md` | PincOpen 통합 확정 기록 (🔺 §7 수치는 §4.6/§4.7 참조) |
| `docs/PINCOPEN.md` | ⚠️ **부재** — 실물 그리퍼 안전 절차 원본 |
