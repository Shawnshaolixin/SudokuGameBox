using UnityEditor;
using UnityEngine;

/// <summary>
/// 自动 Sprite 导入器 — 监听 Art/ 目录下的新 PNG，自动配置 Unity 导入设置。
///
/// 工作原理：
///   你把后处理完的 PNG 放到 Assets/Art/ 下的任意子目录，
///   Unity 检测到新文件 → 触发 OnPreprocessTexture → 自动设 Sprite 类型。
///
/// 命名约定（与 Python 脚本 tools/sprite_pipeline.py 对齐）：
///   _btn      → 按钮 → Texture Type=Sprite, 自动 9-slice border
///   _panel    → 面板 → Texture Type=Sprite, 自动 9-slice border
///   _icon     → 图标 → Texture Type=Sprite, Pivot=Center
///   _particle → 粒子贴图 → Texture Type=Sprite, 无压缩, WrapMode=Clamp
///   _bg       → 背景 → Texture Type=Sprite
/// </summary>
public class SpritePipelineImporter : AssetPostprocessor
{
    // ============================================================
    // 配置常量 — 按你的项目需求调整
    // ============================================================

    // 9-slice 自动推算参数：图片尺寸的 30% 作为 border
    // 例如 256x256 的按钮 → border = (77, 77, 77, 77)，取整后为 (77,77,77,77)
    private const float NineSliceRatio = 0.30f;

    /// <summary>
    /// 纹理导入前的钩子 — Unity 在每个纹理导入时自动调用
    /// </summary>
    void OnPreprocessTexture()
    {
        // 只处理 Art/ 目录下的文件
        if (!assetPath.Contains("Assets/Art/"))
            return;

        var importer = (TextureImporter)assetImporter;

        // === 基础设置：所有 Art 资源都是 Sprite ===
        importer.textureType = TextureImporterType.Sprite;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.spritePixelsPerUnit = 100;

        string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);

        // === 按命名约定细分设置 ===

        if (fileName.EndsWith("_btn") || fileName.EndsWith("_button"))
        {
            // 按钮：9-slice，可拉伸不变形
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Compressed;
            // border 在 OnPostprocessTexture 设（因为此时还不知道图片尺寸）
        }
        else if (fileName.EndsWith("_panel"))
        {
            // 面板背景：9-slice
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }
        else if (fileName.EndsWith("_icon"))
        {
            // 图标：Single，保持清晰锐利
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.filterMode = FilterMode.Bilinear;
        }
        else if (fileName.EndsWith("_particle"))
        {
            // 粒子贴图：不压缩（粒子对精度敏感），钳制边缘
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
        }
        else if (fileName.EndsWith("_bg"))
        {
            // 背景：允许更大尺寸，适当压缩
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 2048;
        }
        else
        {
            // 默认：Single Sprite
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }
    }

    /// <summary>
    /// 纹理导入后 — 此时能读取实际图片尺寸，用于自动设置 9-slice border
    /// </summary>
    void OnPostprocessTexture(Texture2D texture)
    {
        if (!assetPath.Contains("Assets/Art/"))
            return;

        string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);

        // 只为按钮和面板自动设 9-slice
        bool isButtonOrPanel = fileName.EndsWith("_btn")
                            || fileName.EndsWith("_button")
                            || fileName.EndsWith("_panel");

        if (!isButtonOrPanel)
            return;

        // 使用延迟调用确保 importer 已完全就绪
        EditorApplication.delayCall += () =>
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            int width = texture.width;
            int height = texture.height;

            // 按 30% 推算 9-slice 边框
            int borderX = Mathf.RoundToInt(width * NineSliceRatio);
            int borderY = Mathf.RoundToInt(height * NineSliceRatio);

            importer.spriteBorder = new Vector4(borderX, borderY, borderX, borderY);
            importer.SaveAndReimport();

            Debug.Log(
                $"[SpritePipeline] 自动 9-slice: {fileName}.png " +
                $"({width}x{height}) → border=({borderX},{borderY},{borderX},{borderY})"
            );
        };
    }

    // ============================================================
    // 辅助：对已导入的文件手动重新应用设置
    // ============================================================

    /// <summary>
    /// 遍历 Art/ 下已有资源，重新刷新导入设置。
    /// 当你更新了此脚本的规则后，调用此方法一次性刷新所有旧资源。
    /// </summary>
    [MenuItem("Tools/Sprite Pipeline/Refresh All Art Assets")]
    public static void RefreshAllArtAssets()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art" });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            // 重新导入会再次触发 OnPreprocessTexture
            importer.SaveAndReimport();
            count++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[SpritePipeline] ✅ 刷新完成，共处理 {count} 个资源");
    }
}