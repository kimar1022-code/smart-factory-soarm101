using System;

namespace SOArmControl
{
    /// <summary>
    /// SO-ARM101 로봇 제어 공통 인터페이스.
    /// 시뮬과 실로봇 모두 이 계약을 따름.
    /// 
    /// SO-ARM에 맞게 단순화 (FR5의 Cartesian/IK/모드 제거).
    /// </summary>
    public interface ISOArmController
    {
        // ── 연결 상태 ─────────────────────────────────────────
        bool IsConnected { get; }
        string StatusMessage { get; }
        event Action<string> OnStatusChanged;

        void Connect();
        void Disconnect();

        // ── 조인트 ─────────────────────────────────────────────
        int JointCount { get; }
        string GetJointName(int index);
        float GetJointMinAngle(int index);
        float GetJointMaxAngle(int index);
        float GetJointAngle(int index);

        void SetJointTarget(int index, float angleDegrees);
        void SetAllJointTargets(float[] anglesDegrees);

        // ── 그리퍼 ─────────────────────────────────────────────
        // SO-ARM은 그리퍼도 일반 모터지만 별도 API 제공
        float GetGripperPercent();          // 0~100
        void SetGripperTarget(float percent); // 0=닫힘, 100=열림

        // ── 비상/홈 ────────────────────────────────────────────
        void StopMotion();
        void GoToHome();
        void SetHomeFromCurrent();

        // ── 홈 포즈 데이터 ─────────────────────────────────────
        float[] GetHomePose();
        void SetHomePose(float[] anglesDegrees);
    }
}
