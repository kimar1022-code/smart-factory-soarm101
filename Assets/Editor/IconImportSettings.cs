using UnityEditor;
using UnityEngine;

namespace SOArmControl.EditorTools
{
    /// <summary>
    /// Assets/UI/Icons 아래 PNG 를 자동으로 **Sprite** 로 임포트한다.
    ///
    /// 【왜 필요한가】
    ///   3D 템플릿 프로젝트는 PNG 를 Texture(textureType 0)로 들인다.
    ///   그러면 인스펙터의 Sprite 칸에 끌어다 놓을 수 없어서 아이콘을 쓸 수 없다.
    ///   649개를 손으로 바꾸는 건 현실적이지 않아 임포트 단계에서 처리한다.
    /// </summary>
    public class IconImportSettings : AssetPostprocessor
    {
        const string IconRoot = "Assets/UI/Icons/";

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(IconRoot)) return;

            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;                 // UI 는 밉맵이 필요 없다. 메모리만 먹는다
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;   // 128px 아이콘이라 부담 없다
            ti.maxTextureSize = 128;
        }

        /// <summary>
        /// 이미 임포트된 것들은 위 훅이 다시 돌지 않는다. 강제로 한 번 재임포트한다.
        /// </summary>
        [MenuItem("Tools/관제/아이콘을 Sprite 로 재임포트")]
        static void ReimportIcons()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UI/Icons"))
            {
                Debug.LogWarning("[아이콘] Assets/UI/Icons 폴더가 없습니다.");
                return;
            }

            AssetDatabase.ImportAsset("Assets/UI/Icons",
                ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            int n = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/UI/Icons" }).Length;
            Debug.Log($"[아이콘] 재임포트 완료 — Sprite {n}개");
        }
    }
}
