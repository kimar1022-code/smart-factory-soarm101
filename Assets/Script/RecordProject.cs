using System;
using System.Collections.Generic;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// Record 프로젝트 전체를 표현.
    /// JSON 파일로 저장됨.
    /// </summary>
    [Serializable]
    public class RecordProject
    {
        public string projectName;        // "박스 전달 작업" 같은 이름
        public string createdAt;          // 생성 시각 (ISO 8601)
        public string lastModifiedAt;     // 마지막 수정 시각
        public string version;            // 데이터 버전 (마이그레이션용)
        public List<Waypoint> waypoints;  // 모든 스텝의 리스트

        public RecordProject()
        {
            projectName = "Untitled";
            createdAt = DateTime.Now.ToString("o");  // ISO 8601
            lastModifiedAt = createdAt;
            version = "1.0";
            waypoints = new List<Waypoint>();
        }

        // ── 새 빈 프로젝트 생성 ──
        public static RecordProject NewProject(string name)
        {
            var p = new RecordProject();
            p.projectName = name;
            return p;
        }

        // ── 스텝 번호 자동 재정렬 (1, 2, 3...) ──
        public void RenumberSteps()
        {
            for (int i = 0; i < waypoints.Count; i++)
                waypoints[i].stepNumber = i + 1;
        }

        // ── 수정 시각 갱신 ──
        public void Touch()
        {
            lastModifiedAt = DateTime.Now.ToString("o");
        }
    }
}
