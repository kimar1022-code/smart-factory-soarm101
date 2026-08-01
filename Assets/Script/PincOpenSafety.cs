using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// PincOpen 그리퍼 실물 명령 안전장치.
    ///
    /// STS3215 는 위치 제어 모드에서 토크 제한 기능이 없다.
    /// 물체를 물면 모터가 계속 힘을 주다가 모터가 타거나 플라스틱이 부러진다.
    /// (PincOpen 저장소 원문 경고)
    ///
    /// 그래서 소프트웨어에서 두 겹으로 막는다.
    ///   1) 명령 범위 제한 — 검증된 각도 밖으로 나가는 명령을 잘라낸다
    ///   2) 캘리브레이션 게이트 — 순정 그리퍼 기준 캘리브레이션이 남아 있으면
    ///      -100 이 PincOpen 의 파손 각도에 대응할 수 있으므로 아예 막는다
    ///
    /// 펌웨어 쪽 보호(overload/protective torque)는 라즈베리파이에서 따로 구워야 한다.
    /// 이 클래스는 그 앞단의 소프트웨어 방어선이다.
    /// </summary>
    public static class PincOpenSafety
    {
        // ── 검증된 모터 각도 (공식 노트북 flash_test.ipynb) ──────────
        public const float MotorHardLimitDeg = -147f;  // set_min_angle_limit
        public const float MotorOpenDeg = -140f;       // set_goal_position (열림)
        public const float MotorClosedDeg = 0f;        // set_goal_position (닫힘)

        // ── 펌웨어에 구워야 할 보호 파라미터 (참고값) ────────────────
        // 주석과 코드가 다른데, 코드 쪽이 더 보수적이라 코드 값을 쓴다.
        public const int TorqueLimit = 1000;
        public const int OverloadTorque = 40;    // 초과 시 보호 발동
        public const int ProtectiveTorque = 5;   // 발동 후 이 값으로 강하
        public const int ProtectionTime = 7;     // 70ms
        public const int Acceleration = 200;

        /// <summary>
        /// 실물 그리퍼 명령 허용 여부.
        ///
        /// ✅ 2026-08-01 해제. 아래 전제 조건을 모두 실측으로 충족했다.
        ///
        /// 원래 잠가둔 이유:
        ///   PincOpen 장착 전 캘리브레이션은 순정 moving_jaw 기준이라,
        ///   그 상태로 명령하면 PincOpen 의 기계적 한계를 넘어 파손될 수 있었다.
        ///   (실제로 "닫힘" 명령에도 손가락이 20mm 벌어진 채 멈췄다)
        ///
        /// 해제 근거 — 두 로봇 모두 완료:
        ///   1. 모터 자력 저토크 탐색으로 실제 기계 한계 측정
        ///        robot1  raw  807 ~ 3364   (행정 1439 → 2557틱)
        ///        robot2  raw  431 ~ 3322   (행정 1500 → 2931틱)
        ///   2. 캘리브레이션 range 를 실측값으로 교체
        ///   3. **모터 펌웨어 Min/Max_Position_Limit 에 같은 값을 기록** ← 하드웨어 보호
        ///      소프트웨어에 버그가 있어도 모터가 스스로 한계를 넘지 않는다
        ///   4. 그리퍼 방향(invertSign)을 로봇별로 확정
        ///
        /// ⚠️ 캘리브레이션을 다시 만지거나 그리퍼를 재조립하면 이 값을 다시 false 로 내리고
        ///    위 1~4 를 다시 밟을 것.
        /// </summary>
        public static bool RealGripperEnabled = true;

        /// <summary>마지막 차단 사유. UI 표시용.</summary>
        public static string LastBlockReason { get; private set; } = "";

        /// <summary>
        /// 정규화값의 방향 반전.
        ///
        /// ✅ 2026-08-01 실측으로 true 확정 (robot1).
        ///    모터 raw 가 **낮을수록 열림**, 높을수록 닫힘이다.
        ///      raw  807 = 열림 (기계 한계 782 + 여유 25)
        ///      raw 3364 = 닫힘 (기계 한계 3389 − 여유 25)
        ///    RANGE_M100_100 은 range_min 이 -100 이므로 -100 = 열림이 된다.
        ///    그런데 이 프로젝트의 약속은 percent 0 = 닫힘 → norm -100 이다.
        ///    그래서 부호를 뒤집어야 의미가 맞는다.
        ///
        /// ⚠️ robot2 는 아직 그리퍼 재캘리브레이션 전이다.
        ///    이 값은 static 이라 두 로봇에 함께 적용되니, robot2 작업 후 재확인할 것.
        /// </summary>
        public static bool InvertDirection = true;

        /// <summary>
        /// 명령 가능한 행정 비율(%). 처음에는 양 끝을 조금 남겨두고 쓰는 게 안전하다.
        /// 끝단은 기계적 정지점이라 모터가 계속 밀어붙이기 쉽다.
        /// 동작이 확인되면 0~100 으로 넓혀도 된다.
        /// </summary>
        public static float TravelMarginPercent = 5f;

        /// <summary>
        /// 실물로 나갈 그리퍼 명령을 검사한다.
        /// 통과하면 true 와 함께 안전 범위로 자른 값을 돌려준다.
        /// </summary>
        public static bool TryApprove(float percent, out float safePercent)
        {
            safePercent = Mathf.Clamp(percent, 0f, 100f);

            if (!RealGripperEnabled)
            {
                LastBlockReason =
                    "PincOpen 실물 명령이 잠겨 있습니다. " +
                    "재캘리브레이션 + 펌웨어 각도 리밋(-147~0)을 마친 뒤 " +
                    "PincOpenSafety.RealGripperEnabled 를 켜세요. (docs/PINCOPEN.md 4절)";
                return false;
            }

            LastBlockReason = "";
            return true;
        }

        /// <summary>
        /// 퍼센트(0=닫힘, 100=열림)를 모터 각도로 변환한다.
        /// 하드 리밋(-147°)에는 절대 닿지 않고 -140° 에서 멈춘다.
        /// </summary>
        public static float PercentToMotorDeg(float percent)
        {
            percent = Mathf.Clamp(percent, 0f, 100f);
            return Mathf.Lerp(MotorClosedDeg, MotorOpenDeg, percent * 0.01f);
        }

        /// <summary>
        /// 퍼센트(0=닫힘, 100=열림)를 서버로 보낼 정규화값(-100~100)으로 바꾼다.
        /// 양 끝을 TravelMarginPercent 만큼 남기고, 필요하면 방향을 뒤집는다.
        ///
        /// 끝단을 남기는 이유: 기계적 정지점에 목표를 두면 모터가 도달하지 못한 채
        /// 계속 힘을 준다. STS3215 는 위치 제어에서 토크 제한이 없어 이때 손상된다.
        /// </summary>
        public static float PercentToServerValue(float percent)
            => PercentToServerValue(percent, InvertDirection);

        /// <summary>
        /// 반전 여부를 호출자가 지정하는 버전.
        ///
        /// ⚠️ 두 로봇의 그리퍼 장착 방향이 서로 반대라 전역 플래그로는 둘 다 못 맞춘다.
        ///    (실측: robot1 은 raw 가 높을수록 닫힘, robot2 는 raw 가 높을수록 열림)
        ///    그래서 관절별 invertSign 을 넘겨 쓰는 이 오버로드를 사용한다.
        /// </summary>
        public static float PercentToServerValue(float percent, bool invert)
        {
            float m = Mathf.Clamp(TravelMarginPercent, 0f, 40f);
            float safePct = Mathf.Lerp(m, 100f - m, Mathf.Clamp(percent, 0f, 100f) * 0.01f);

            float v = safePct * 2f - 100f;          // 0~100 → -100~100
            return invert ? -v : v;
        }

        /// <summary>펌웨어에 구울 보호 설정을 라즈베리파이용 파이썬으로 출력한다.</summary>
        public static string GetFirmwareSetupSnippet(int motorId = 6)
        {
            return
                $"# PincOpen 보호 설정 (ID {motorId}) — 라파에서 1회 실행\n" +
                $"# ⚠️ 레지스터 이름은 LeRobot 기준으로 먼저 확인할 것:\n" +
                $"#    print(bus.model_ctrl_table['sts3215'].keys())\n" +
                $"set_lock({{{motorId}: 0}})\n" +
                $"set_acceleration({{{motorId}: {Acceleration}}})\n" +
                $"set_max_angle_limit({{{motorId}: {MotorClosedDeg:F0}}})\n" +
                $"set_min_angle_limit({{{motorId}: {MotorHardLimitDeg:F0}}})\n" +
                $"set_torque_limit({{{motorId}: {TorqueLimit}}})\n" +
                $"set_overload_torque({{{motorId}: {OverloadTorque}}})\n" +
                $"set_protective_torque({{{motorId}: {ProtectiveTorque}}})\n" +
                $"set_protection_time({{{motorId}: {ProtectionTime}}})\n" +
                $"set_lock({{{motorId}: 1}})\n";
        }
    }
}
