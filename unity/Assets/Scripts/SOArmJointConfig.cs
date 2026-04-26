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

        [Header("각도 범위")]
        public float minAngle = -110f;
        public float maxAngle = 110f;
        public float homeAngle = 0f;

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
