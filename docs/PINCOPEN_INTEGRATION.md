# PincOpen 그리퍼 통합 — 확정 기록

> 확정일: 2026-08-01
> 조사 배경과 안전 규칙은 `PINCOPEN.md` 참조. 이 문서는 **Unity/URDF 통합 결과**만 다룬다.

---

## 1. 확정된 값

```xml
<joint name="pincopen_mount" type="fixed">
  <origin xyz="0 0 -0.008117" rpy="0 1.5708 0"/>
  <parent link="gripper_link"/>
  <child  link="pincopen_adapter_link"/>
</joint>
```

### 유도 근거 (추측 아님)

| 항목 | 근거 |
|---|---|
| 회전 `rpy="0 1.5708 0"` | 순정 TCP(`gripper_frame_joint`)가 `z=-0.0981` → 공구 방향은 `gripper_link` 의 **-Z**. PincOpen 은 **+X** 가 손가락 방향. +X→-Z 로 보내는 회전이 Y축 +90°. |
| 이동 `z=-0.008117` | Interface_ARM100 어댑터 두께(STL 실측 8.117mm)만큼 뒤로 물림. |

---

## 2. ⭐ 어댑터 ↔ 그리퍼는 자동 정렬됨 (조정 불필요)

STL/DAE 를 직접 파싱해 얻은 좌표:

| 메시 | X 범위 |
|---|---|
| `Interface_ARM100.stl` (원본 CAD, mm) | `-61.609 ~ -53.492` |
| PincOpen `base.dae` (m) | `0 ~ 0.057492` |

어댑터 X 최대값 `-53.492` 는 PincOpen 관절 원점 `0.053492` 와 **정확히 같은 수**다.
즉 **MuJoCo 팀이 base 메시를 재중심화할 때 쓴 기준면이 어댑터 결합면**이다.

→ 어댑터를 **+53.492mm** 평행이동하면 `-8.117 ~ 0` 이 되어 base 뒷면(`x=0`)과 정확히 맞물린다.
이 평행이동은 **DAE 변환 시 메시에 미리 구워넣었다.** 따라서:

- `pincopen_adapter_link` 의 visual origin = `0 0 0`
- `pincopen_base_joint` 의 origin = `0 0 0`

둘 다 0 이 맞으며 **건드리면 안 된다.**

---

## 3. 링크 구조

```
wrist_link
 └[wrist_roll]→ gripper_link
      ├[gripper_frame_joint, fixed]→ gripper_frame_link      ← TCP. IK 기준점
      └[pincopen_mount, fixed]→ pincopen_adapter_link        ← ⚙️ 조정 지점
            └[pincopen_base_joint, fixed]→ pincopen_base_link
                  ├[gripper, revolute]→ left_proximal        ← 구동축 (모터 ID 6)
                  │     └[left_distal_joint]→ left_distal
                  └[right_proximal_joint]→ right_proximal
                        └[right_distal_joint]→ right_distal
```

---

## 4. 검증 결과

| 항목 | 측정 | 판정 |
|---|---|---|
| 어댑터 두께 | 8.1mm | STL 실측 8.117mm 과 일치 ✅ |
| PincOpen base 크기 | 98.6 × 59.4 × 57.5mm | 실물 치수 일치 ✅ |
| 손목 ↔ 어댑터 빈틈 | 0.0mm | 접촉 ✅ |
| 좌우 정렬 | 대칭 | ✅ |
| 커플링 자동 연결 | 5개 링크 전부 | ✅ |

---

## 5. 변경 내역

| 파일 | 내용 |
|---|---|
| `so101.urdf` | `moving_jaw_so101_v1` 링크/조인트 삭제 → PincOpen 6링크 삽입 |
| `so101.urdf` | `gripper_link` 의 `sts3215_03a_v1` visual **주석 처리** (중복 렌더링) |
| `meshes/PincOpen/visual/` | DAE 6개 (base, l/r proximal, l/r distal, Interface_ARM100) |
| `Script/PincOpenCoupling.cs` | 4절 링크 커플링 + 마운트 조정 (신규) |
| `Editor/PincOpenSetupMenu.cs` | 재임포트 + 자동 연결 메뉴 (신규) |
| `Editor/PincOpenCapture.cs` | 헤드리스 렌더링 검증 도구 (신규) |

### 🔴 모터 중복 제거

PincOpen `base` 메시 안에 STS3215(ID 6)가 **이미 포함**돼 있다.
순정 `gripper_link` 의 `sts3215_03a_v1` 을 그대로 두면 모터가 두 개로 렌더링된다.
실물은 한 개이므로 순정 쪽을 주석 처리했다. (삭제 아님 — 되돌릴 수 있게)

---

## 6. ⚠️ 주의

### applyMountOffset 은 꺼둘 것
`PincOpenCoupling.applyMountOffset` 을 켜면 URDF 의 `pincopen_mount` origin 을
매 프레임 덮어쓴다. 값이 0 이면 그리퍼가 손목 원점으로 튄다.
**위치를 다시 조정할 때만 잠깐 켜고, 끝나면 URDF 에 굽고 반드시 다시 끌 것.**

### 관절각 = 손가락 각도 (모터 각도 아님)
URDF 의 `gripper` 조인트는 **손가락 각도**다. 모터 각도와 다르다.
`PINCOPEN.md` 기준 **모터 140° ≈ 손가락 44°**.
현재 리밋 `±1.25 rad (±71.6°)` 는 커플링 배율 미확정(×0.5 vs ×1.0)이라 넓게 잡은 값.

### 실물 명령 금지
여기까지는 **시뮬레이션 전용**이다.
실물 PincOpen 에 명령을 보내기 전에 `PINCOPEN.md` 4절 절차를 먼저 통과할 것:
토크 OFF → 손으로 열고 닫으며 -140°/0° 확인 → 캘리브레이션 → 펌웨어 각도 리밋.

---

## 7. 가동범위 (2026-08-01 확정)

### 모터 각도 — 공식 노트북 `flash_and_tests/flash_test.ipynb`
```
하드 리밋  set_min_angle_limit(-147)   ← 펌웨어에 굽는 값
열림       set_goal_position(-140)
닫힘       set_goal_position(0)
```
ROS2 드라이버 `config` 도 `min_position: -2.4 rad` 로 사실상 같은 값.

### 손가락 각도 — ROS2 xacro `urdf/gripper_macros.xacro`
```
gripper (모터축)            lower=-2.44  upper=0
base_link_to_right_arm      mimic ×+0.5   (-1.22 ~ 0)
base_link_to_left_arm       mimic ×-0.5   ( 1.22 ~ 0)
right_arm_to_right_finger   mimic ×-0.5   ( 0 ~ 1.22)
left_arm_to_left_finger     mimic ×+0.5   (-1.22 ~ 0)
```
→ 손가락 가동폭 **1.22 rad = 69.9°**

### ⭐ ×0.5 vs ×1.0 — 모순이 아니었음

두 문헌은 **기준 관절이 다를 뿐**이다.
ROS2 는 네 관절 전부를 *모터축* 기준으로 적는데,
우리 URDF 의 구동축은 모터축이 아니라 왼쪽 proximal(= `base_link_to_left_arm`)이고
이 관절 자체가 이미 모터의 -0.5 배다. 모터 `M = -2θ` 로 환산하면

```
left_distal    = +0.5M = -θ
right_proximal = +0.5M = -θ
right_distal   = -0.5M = +θ      →  ×1.0 의 (-1, -1, +1)
```

**렌더링 교차검증**: ×1.0 에서만 손가락 패드가 서로 **평행**하게 맞물린다.
PincOpen 은 평행 4절 링크이므로 이게 정답. ×0.5 는 끝만 뾰족하게 모인다.

**부호**: 렌더링 실험으로 **음수 = 닫힘** 확정.
양수 방향은 4절 링크가 뒤집혀 무효(+70° 에서 손가락이 몸체 위로 접힘).

### 코드 구동 자체검증 결과

⚠️ **아래 표는 닫힘각이 -69.9° 이던 시절 측정값이라 무효다.**
현재 닫힘각은 **-48.5°** 로 바뀌었으므로(§7 참조) 재측정이 필요하다. ⬜
`Tools ▸ SO-ARM ▸ 그리퍼 구동 자체검증` 으로 다시 돌릴 것.

```
(구 측정 — 참고용)
100% → 구동축   0.0°, 손가락 간격 94.6mm
 75% → 구동축 -17.5°, 74.4mm ↓
 50% → 구동축 -35.0°, 53.9mm ↓
 25% → 구동축 -52.4°, 35.2mm ↓
  0% → 구동축 -69.9°, 22.9mm ↓
150% 입력 → 0.0° 로 잘림 ✅
```

확정된 값 (메시 정점 실측):
```
-48.0° → 좌우 손가락 간격  1.07mm   ← 접촉
-48.5° → 약 0.5mm          ← 현재 설정 (맞닿되 겹치지 않음)
-48.9° → 0mm               ← 접촉점
-50.0° → -1.34mm           ❌ 겹침
```
`Tools ▸ SO-ARM ▸ 그리퍼 구동 자체검증` 으로 언제든 재실행 가능.

> 💡 관절이 전혀 안 움직이면 십중팔구 `stiffness=0` 이다.
> URDF 임포터가 limit 만 채우고 드라이브를 안 켜는 경우가 있다.
> `PincOpenCoupling.ConfigureDrives()` 가 채워준다.

---

## 8. 안전장치 (`PincOpenSafety.cs`)

STS3215 는 **위치 제어 모드에서 토크 제한이 없다.** 물체를 물면 계속 힘을 주다가
모터가 타거나 플라스틱이 부러진다. 그래서 소프트웨어에서 두 겹으로 막는다.

### (1) 캘리브레이션 게이트 — 기본 **잠금**
```csharp
PincOpenSafety.RealGripperEnabled = false;   // 기본값
```
순정 그리퍼(-10°~100°) 기준 캘리브레이션이 남아 있으면
정규화값 -100 이 PincOpen 의 파손 각도에 대응할 수 있다.
`SOArmRealController.SetGripperTarget` 이 이 게이트를 거치며, 막히면 경고 1회 후 무시.

**아래를 마친 뒤에만 켤 것** (`PINCOPEN.md` 4절):
1. 토크 OFF 로 손으로 열고 닫으며 위치 읽기
2. 열림 -140° / 닫힘 0° 근처인지 확인 — 아니면 **중단**
3. LeRobot 재캘리브레이션 (`--robot.id` 필수)
4. 펌웨어 각도 리밋 굽기

### (2) 범위 제한
- `PincOpenCoupling.SetGripperPercent` / `SetDriveAngle` 이 손가락 각도를 잘라냄
- URDF 리밋 `-1.22 ~ 0`
- 잘릴 때 경고 로그 출력

### (3) 펌웨어 보호 파라미터 (라파에서 1회 실행)
`PincOpenSafety.GetFirmwareSetupSnippet()` 이 파이썬 코드를 출력한다.
```
torque_limit 1000 / overload 40 / protective 5 / protection_time 7(70ms) / accel 200
```
⚠️ 레지스터 이름은 LeRobot 기준으로 먼저 확인:
`print(bus.model_ctrl_table['sts3215'].keys())`

---

## 9. 메인 씬 적용 (2026-08-01)

`LeRobot.unity` 의 로봇 2대에 PincOpen 을 이식했다.
**로봇을 통째로 재임포트하지 않고 그리퍼 subtree 만 교체**했다 —
통째로 갈면 `SOArmManager`·`SocketClient`·UI 의 인스펙터 연결이 전부 끊어지기 때문.

`Tools ▸ SO-ARM ▸ 메인 씬에 PincOpen 이식` (재실행해도 안전, 중복 생성 안 함)

| 처리 | 내용 |
|---|---|
| 제거 | `moving_jaw_so101_v1_link`, `sts3215_03a_v1`, `wrist_roll_follower_so101_v1` |
| 이식 | PincOpen 6링크를 `gripper_link` 밑에 |
| 재연결 | Sim J6 → `pincopen_left_proximal_link` |
| 범위 갱신 | Sim/Real J6 → `-69.9° ~ 0°` |
| 부착 | `PincOpenCoupling` (자동 연결 + 드라이브 설정, `applyMountOffset=false`) |

### 🔴 함께 고친 별도 버그 — Sim↔Real 관절 범위 불일치

인수인계 문서의 **"시뮬과 실로봇 포즈가 일치하지 않음"** 의 원인.

| | Sim (수정 전) | Real (수정 전) |
|---|---|---|
| J3 elbow_flex | -96.8 ~ 96.8 | **-100 ~ 100** ❌ |
| J5 wrist_roll | -157.2 ~ 162.8 | **-160 ~ 160** ❌ |

`SOArmMotorMapper` 는 min/max 로 정규화하므로, 범위가 다르면
**같은 슬라이더 값이 서로 다른 각도**가 된다. J5 에서 최대 약 3° 오차.
공식 URDF 값(Sim 쪽)으로 통일했다. 12개 슬롯 전부 일치 확인.

> 참고: `SOArmRealController` 의 `articulationBody` 슬롯이 한 칸씩 밀려 있으나
> 이 컴포넌트는 해당 필드를 사용하지 않는다(소켓 명령만 보냄). 무해.

---

## 10. 남은 미확정 항목

| 항목 | 상태 |
|---|---|
| TCP(`gripper_frame_link`) 위치 | 순정 조 끝 기준. PincOpen 손끝으로 옮겨야 함 (IK 단계) |
| 실물 캘리브레이션 | 미수행. `RealGripperEnabled` 가 잠겨 있는 이유 |
| 펌웨어 각도 리밋 | 미적용. LeRobot 레지스터 이름 확인 후 굽기 |
| `pincopen_mount` 미세 오차 | 렌더링 기준으로는 맞으나 실측 대조는 안 함 |

---

## 8. 출처 · 라이선스

- 메시: [pollen-robotics/PincOpen](https://github.com/pollen-robotics/PincOpen) — **CC BY-SA 4.0**
  - `Interface_ARM100.stl` : `cad/stl/`
  - 그리퍼 5개 : PR #6 (MuJoCo Simulation Support) `mujoco/assets/`
- 관절 정보: 같은 PR 의 `mujoco/eef.xml`

📌 **GitHub 공개 시 출처 표기 + 동일 라이선스(CC BY-SA 4.0) 유지 필요.**
