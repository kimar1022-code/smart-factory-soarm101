using System;
using System.Collections.Generic;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 스텝 한 개를 표현하는 데이터 클래스.
    /// type 필드로 4가지 타입 구분: motion, wait, loop_start, loop_end
    /// </summary>
    [Serializable]
    public class Waypoint
    {
        // ── 공통 필드 ──
        public int stepNumber;        // 스텝 번호 (1, 2, 3...)
        public string type;           // "motion" / "wait" / "loop_start" / "loop_end"
        public string name;           // 사용자 지정 이름 (자동 기본값 가능)

        // ── motion 타입 전용 필드 ──
        public string target;         // "robot1" / "robot2" / "both"
        public float[] joints;        // robot1 관절 6개 (robot2 단독일 땐 안 씀)
        public float gripper;         // 0~100 %
        public float[] joints2;       // robot2 관절 6개
        public float gripper2;        // robot2 그리퍼
        public int velocity;          // 이 스텝의 속도
        public int acceleration;      // 가속도
        public float delayAfter;      // 스텝 완료 후 대기 (초)

        // ── wait 타입 전용 필드 ──
        public float duration;        // 대기 시간 (초)

        // ── loop_start 전용 필드 ──
        public int loopCount;         // 반복 횟수

        // ── 기본 생성자 (Unity JsonUtility 필수) ──
        public Waypoint()
        {
            joints = new float[6];
            joints2 = new float[6];
            velocity = 800;
            acceleration = 50;
            delayAfter = 1.0f;
            gripper = 50f;
            gripper2 = 50f;
            duration = 1.0f;
            loopCount = 1;
        }

        // ── 표시용 짧은 설명 ──
        public string GetDisplayText()
        {
            switch (type)
            {
                case "motion":
                    return $"{target}: {name}";
                case "wait":
                    return $"⏱ 대기 {duration:F1}초";
                case "loop_start":
                    return $"🔁 반복 시작 ({loopCount}회)";
                case "loop_end":
                    return $"🔁 반복 끝";
                default:
                    return $"? {type}";
            }
        }
    }
}