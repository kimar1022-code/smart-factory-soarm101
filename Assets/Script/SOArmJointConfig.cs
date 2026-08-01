using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// SO-ARM101 관절 하나의 설정.
    /// 시뮬용 Transform 정보 + 실로봇 매핑 정보를 모두 포함.
    /// </summary>
    [System.Serializable]
    public class SOArmJointConfig
    {
        [Header("기본 정보")]
        [Tooltip("UI 표시용 이름")]
        public string displayName = "Joint";

        [Tooltip("서버에 보낼 모터 이름 (shoulder_pan 등)")]
        public string motorName = "shoulder_pan";

        [Header("각도 범위 (정규화 기준 — 함부로 바꾸지 말 것)")]
        [Tooltip("⚠️ 이 값은 서버 정규화값(-100~100)과 각도를 잇는 '자'다.\n" +
                 "  -100 → minAngle,  +100 → maxAngle 로 선형 대응한다.\n" +
                 "안전 목적으로 이 값을 좁히면 환산 자체가 틀어져서\n" +
                 "시뮬과 실물이 어긋난다. 명령 범위를 제한하려면\n" +
                 "아래 softMin/softMax 를 쓸 것.")]
        public float minAngle = -110f;
        public float maxAngle = 110f;
        public float homeAngle = 0f;

        [Header("소프트 리밋 (명령 제한 전용)")]
        [Tooltip("실제로 명령을 허용할 범위. 정규화 환산에는 쓰이지 않는다.\n" +
                 "기계적 스토퍼에 부딪혀 모터가 과열되는 것을 막는 용도.\n" +
                 "useSoftLimit 가 꺼져 있으면 min/maxAngle 을 그대로 쓴다.")]
        public bool useSoftLimit = false;
        public float softMinAngle = -110f;
        public float softMaxAngle = 110f;

        /// <summary>명령을 잘라낼 하한. 소프트 리밋이 꺼져 있으면 minAngle.</summary>
        public float ClampMin => useSoftLimit ? Mathf.Max(minAngle, softMinAngle) : minAngle;

        /// <summary>명령을 잘라낼 상한. 소프트 리밋이 꺼져 있으면 maxAngle.</summary>
        public float ClampMax => useSoftLimit ? Mathf.Min(maxAngle, softMaxAngle) : maxAngle;

        [Header("유니티 시뮬 제어")]
        [Tooltip("회전시킬 ArticulationBody (URDF 임포트한 관절)")]
        public ArticulationBody articulationBody;

        [Header("동기화 옵션")]
        [Tooltip("시뮬과 실로봇 회전 방향이 반대일 때 체크")]
        public bool invertSign = false;

        [Tooltip("시뮬 기준점 보정 (degree)")]
        public float angleOffset = 0f;
    }
}
