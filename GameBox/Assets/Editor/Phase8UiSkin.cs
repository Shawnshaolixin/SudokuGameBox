using System.IO;
using Box.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 8 UI 换肤(10 文档 Phase 8 体验打磨;Kenney UI Pack CC0 素材落地)。
/// CLI 无头执行:unity ... -executeMethod Phase8UiSkin.ApplyAll
/// 幂等:重复执行覆盖式写入相同结果,可反复跑。
/// 规则(精确匹配,防止误伤纯色组件):
///   1. 含 BoxButton 组件的节点 → 主按钮图(Sliced)+ 主题主色;
///      (弹窗按钮除外:弹窗改造后按钮语义色由 PopupCardMigration 统一管,防换肤刷回品牌蓝)
///   2. Popups 目录下 prefab 的 Card 子节点 Image → 弹窗面板图(Sliced)+ 面板底色;
///      (弹窗改造 2026-08 后根 Image 为全屏遮罩,面板图只刷卡片)
///   3. 其余 Image(棋盘格/分隔线/高亮等纯色组件)一律不动。
/// 棋盘格是 GameplayView 运行时生成(BuildBoardCells),不在 prefab 资产内,天然无冲突。
/// 颜色收敛于 UITheme(Box.UI),换肤即写主题色入 prefab 序列化。
/// </summary>
public static class Phase8UiSkin
{
    const string PrefabRoot = "Assets/UI/Prefabs";
    const string ButtonTexPath = "Assets/Art/UI/Buttons/button_rectangle_depth_flat.png";
    const string PanelTexPath = "Assets/Art/UI/Panels/button_rectangle_depth_border.png";

    // 9-slice 边框:Kenney 按钮 192x64 圆角半径≈16px;面板 192x64 描边圆角≈20px
    static readonly Vector4 BtnBorder = new Vector4(16, 16, 16, 16);
    static readonly Vector4 PanelBorder = new Vector4(20, 20, 20, 20);

    [MenuItem("Box/Phase8/1. Apply Kenney UI Skin")]
    public static void ApplyAll()
    {
        // ① 贴图导入为 Sprite + 9-slice 边框(幂等:重复设置同值)
        var btnSprite = ConfigureSprite(ButtonTexPath, BtnBorder);
        var panelSprite = ConfigureSprite(PanelTexPath, PanelBorder);
        if (btnSprite == null || panelSprite == null)
        {
            Debug.LogError("[Phase8Skin] 贴图导入失败,终止换肤");
            return;
        }

        // ② 遍历 UI prefab 换肤
        int touched = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            touched += SkinPrefab(path, btnSprite, panelSprite);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase8Skin] 换肤完成,共修改 {touched} 个 Image 组件");
    }

    /// <summary>单个 prefab 换肤:返回修改的 Image 组件数。</summary>
    static int SkinPrefab(string prefabPath, Sprite btnSprite, Sprite panelSprite)
    {
        bool isPopup = prefabPath.Contains("/Popups/");
        var root = PrefabUtility.LoadPrefabContents(prefabPath); // 临时载入可编辑副本(不落盘)
        int count = 0;

        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            // 规则①:含 BoxButton 的节点 = 交互按钮 → 按钮图(弹窗按钮除外,语义色归迁移管)
            if (img.GetComponent<BoxButton>() != null)
            {
                if (!isPopup)
                {
                    Apply(img, btnSprite, Image.Type.Sliced, UITheme.Button);
                    count++;
                }
                continue;
            }
            // 规则②:弹窗 Card 子节点 Image = 面板背景 → 面板图(仅 Popups 目录;根 Image 是遮罩,不刷)
            if (isPopup && img.gameObject.name == "Card" && img.transform.parent == root.transform)
            {
                Apply(img, panelSprite, Image.Type.Sliced, UITheme.Panel);
                count++;
            }
            // 其余 Image 不动(规则③)
        }

        // LoadPrefabContents 载入的是临时副本:修改后须 SaveAsPrefabAsset 写回资产,再 Unload 释放
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        return count;
    }

    static void Apply(Image img, Sprite sprite, Image.Type type, Color color)
    {
        img.sprite = sprite;
        img.type = type;
        img.color = color;
    }

    /// <summary>贴图导入为 Sprite(Simple)并写入 9-slice 边框;返回 Sprite 资源(失败返回 null)。</summary>
    static Sprite ConfigureSprite(string path, Vector4 border)
    {
        if (!File.Exists(path))
        {
            Debug.LogError("[Phase8Skin] 贴图缺失: " + path);
            return null;
        }
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError("[Phase8Skin] 非贴图导入器: " + path);
            return null;
        }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = border; // 9-slice:Sliced 模式下四角不拉伸
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
