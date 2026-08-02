using UnityEditor;
using UnityEngine;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// 관제 아이콘을 자동으로 배정한다.
    ///
    /// CleanFlatIcon 은 파일명이 번호식(icon_line_common_42)이라 사람이 649개를 눈으로
    /// 뒤져야 했다. 대지(contact sheet)를 만들어 모양을 확인한 뒤, 관제에 맞는 것만
    /// 골라 여기에 적어 두었다. 메뉴 한 번이면 전부 붙는다.
    ///
    /// 마음에 안 드는 아이콘은 인스펙터에서 개별로 바꾸면 된다. 이건 시작점일 뿐이다.
    /// </summary>
    public static class ControlTowerIconSetup
    {
        const string Root = "Assets/UI/Icons";

        // (필드명, 카테고리, 번호) — 번호는 대지에서 눈으로 확인한 값
        static readonly (string field, string cat, int no)[] Picks =
        {
            ("iconEstop",     "media",  37),   // 경광등 — 비상정지
            ("iconRecord",    "media",  46),   // 비디오 카메라
            ("iconPlay",      "arrow",   9),   // ▷ 재생
            ("iconHome",      "architecture", 1),   // 🏠 집
            ("iconSave",      "arrow",  78),   // 파일 ↑
            ("iconLoad",      "arrow",  79),   // 파일 ↓
            ("iconPlus",      "common", 42),   // ⊕
            ("iconMinus",     "common", 43),   // ⊖
            ("iconDelete",    "common", 29),   // ✕
            ("iconNew",       "common", 76),   // 점선 +
            ("iconTeach",     "common", 73),   // 토글 스위치 — 수동모드
            ("iconMirror",    "common", 13),   // ‖ 두 개
            ("iconR1",        "device", 27),   // 로봇
            ("iconR2",        "device", 27),   // 로봇
            ("iconGripOpen",  "arrow",  29),   // ↔ 벌림
            ("iconGripClose", "arrow",  27),   // →|← 오므림

            ("iconView",        "media",  51),  // CCTV — TOP/SIDE/FRONT VIEW 제목
            ("iconRobotStatus", "device", 27),  // 로봇 — ROBOT STATUS 제목
            ("iconSystem",      "device", 27),  // 로봇 — SO-ARM SYSTEM 제목
                                                // (이 팩에 로봇팔 아이콘은 없다. 로봇으로 대체)
            ("iconRobotCard",   "device", 27),  // 로봇 — 왼쪽 ROBOT 1 / 2 카드
            ("iconSpeed",       "arrow",  52),  // ⇉ 나란한 화살표 — 속도
            ("iconAccel",       "arrow",  18),  // ⋙ 삼중 갈매기 — 가속
            ("iconLoop",        "arrow",   2),  // ↻ 순환 — 반복 구간
            ("iconAdd",         "common", 34),  // + 더하기 — 스텝 추가
        };

        [MenuItem("Tools/관제/아이콘 자동 배정")]
        static void Assign()
        {
            var ct = Object.FindAnyObjectByType<ControlTowerCanvas>();
            if (ct == null) { Debug.LogWarning("[아이콘] 씬에 ControlTowerCanvas 가 없습니다."); return; }

            var so = new SerializedObject(ct);
            int ok = 0, miss = 0;

            foreach (var (field, cat, no) in Picks)
            {
                string path = $"{Root}/icon_line_{cat}/icon_line_{cat}_{no}.png";
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);

                var prop = so.FindProperty(field);
                if (prop == null) { Debug.LogWarning($"[아이콘] 필드 없음: {field}"); continue; }

                if (sp == null)
                {
                    // Sprite 로 임포트되지 않았으면 여기서 걸린다.
                    Debug.LogWarning($"[아이콘] Sprite 를 못 찾음: {path}\n" +
                                     "→ Tools/관제/아이콘을 Sprite 로 재임포트 를 먼저 실행하세요.");
                    miss++; continue;
                }

                prop.objectReferenceValue = sp;
                ok++;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ct);

            ct.ApplyIcons();   // 화면을 다시 만들지 않는다 — 저장한 배치가 날아가면 안 된다
            Debug.Log($"[아이콘] 자동 배정 완료 — 적용 {ok}개, 실패 {miss}개");
        }

        [MenuItem("Tools/관제/아이콘 전부 비우기")]
        static void Clear()
        {
            var ct = Object.FindAnyObjectByType<ControlTowerCanvas>();
            if (ct == null) return;

            var so = new SerializedObject(ct);
            foreach (var (field, _, _) in Picks)
            {
                var p = so.FindProperty(field);
                if (p != null) p.objectReferenceValue = null;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ct);

            ct.ApplyIcons();
            Debug.Log("[아이콘] 전부 비웠습니다 (글자만 표시)");
        }
    }
}
