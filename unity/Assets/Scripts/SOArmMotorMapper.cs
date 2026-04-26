using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 유니티 각도(degree) ↔ 서버 값(-100~100) 변환.
    /// LeRobot 서버는 -100~100 범위를 쓰므로 관절별 min/max를 정규화.
    /// </summary>
    public static class SOArmMotorMapper
    {
        /// <summary>유니티 각도 → 서버 값 (-100 ~ 100).</summary>
        public static float AngleToServerValue(float angle, SOArmJointConfig joint)
        {
            float range = joint.maxAngle - joint.minAngle;
            if (range < 0.01f) return 0f;

            // 정규화: minAngle → 0, maxAngle → 1
            float normalized = (angle - joint.minAngle) / range;

            // 0~1 → -100~100
            float serverValue = normalized * 200f - 100f;

            if (joint.invertSign) serverValue = -serverValue;

            return Mathf.Clamp(serverValue, -100f, 100f);
        }

        /// <summary>서버 값 (-100 ~ 100) → 유니티 각도.</summary>
        public static float ServerValueToAngle(float serverValue, SOArmJointConfig joint)
        {
            if (joint.invertSign) serverValue = -serverValue;

            // -100~100 → 0~1
            float normalized = (serverValue + 100f) / 200f;

            // 0~1 → minAngle~maxAngle
            return Mathf.Lerp(joint.minAngle, joint.maxAngle, normalized);
        }

        /// <summary>그리퍼 퍼센트 (0~100) → 서버 값 (-100~100).</summary>
        public static float PercentToGripperValue(float percent)
        {
            percent = Mathf.Clamp(percent, 0f, 100f);
            return (percent - 50f) * 2f;
        }
    }
}
