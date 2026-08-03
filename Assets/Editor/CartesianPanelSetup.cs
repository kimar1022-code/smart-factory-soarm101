using UnityEditor;
using UnityEngine;
using SOArmControl;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// 카티시안 패널을 씬에 한 번에 설치한다.
    ///
    /// 손으로 GameObject 만들고 컴포넌트 두 개 붙이고 슬롯 연결하는 과정을
    /// 매번 반복하면 빠뜨린다. 재실행해도 안전하다 — 이미 있으면 다시 안 만든다.
    /// </summary>
    public static class CartesianPanelSetup
    {
        const string ObjName = "CartesianPanel";

        [MenuItem("SO-ARM/카티시안 패널 설치", false, 20)]
        public static void Install()
        {
            var existing = Object.FindAnyObjectByType<CartesianPanel>();
            GameObject go;

            if (existing != null)
            {
                go = existing.gameObject;
                Debug.Log($"[카티시안] 이미 있음 — 슬롯만 다시 연결한다: {go.name}");
            }
            else
            {
                go = new GameObject(ObjName);
                Undo.RegisterCreatedObjectUndo(go, "카티시안 패널 설치");
                Debug.Log($"[카티시안] '{ObjName}' 생성");
            }

            var ik = go.GetComponent<SOArmIKController>();
            if (ik == null) ik = Undo.AddComponent<SOArmIKController>(go);

            if (go.GetComponent<CartesianPanel>() == null)
                Undo.AddComponent<CartesianPanel>(go);

            // 슬롯 연결 — 못 찾으면 런타임에 FindAnyObjectByType 이 다시 시도한다.
            if (ik.dualManager == null)
                ik.dualManager = Object.FindAnyObjectByType<SOArmDualManager>();
            if (ik.socketClient == null)
                ik.socketClient = Object.FindAnyObjectByType<SOArmSocketClient>();

            EditorUtility.SetDirty(go);

            string dm = ik.dualManager != null ? ik.dualManager.name : "❌ 못 찾음";
            string sc = ik.socketClient != null ? ik.socketClient.name : "❌ 못 찾음";
            Debug.Log($"[카티시안] 설치 완료.\n" +
                      $"  DualManager : {dm}\n" +
                      $"  SocketClient: {sc}\n" +
                      $"  Play 후 F2 로 패널을 연다.\n" +
                      $"  ⚠️ 라즈베리파이 서버가 켜져 있어야 IK 가 동작한다 (계산을 서버가 한다).");

            Selection.activeGameObject = go;
        }
    }
}
