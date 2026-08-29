using System.IO;
using Box.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 弹窗/主菜单按钮统一换肤工具(2026-08-29 Bug 清单 ③④ + 用户要求):
/// ① 生成官方 UISprite.png —— 从 Unity 内置 UI 元素「UI/Skin/UISprite.psd」导出,
///    9-slice 圆角矩形,替代第三方 Kenney 按钮贴图(官方可用 UI 元素实现圆角);
/// ② 所有按钮 Image → UISprite + Sliced + 设计系统语义色(确认=橙 #E97832 / 取消=奶白 #FFF9E9);
/// ③ 按钮 Label 字色同步(橙底白字 / 奶白底深棕字);
/// ④ 弹窗静态文案英文化(兜底,运行时由 L10n 覆盖,防刷新前闪现中文);
/// ⑤ 删除 SettingsPopup 的 LangButton(语言切换入口已移除)。
/// 幂等:重复执行覆盖式写入相同结果。CLI 无头:unity -executeMethod PopupButtonSkin.ApplyAll
/// </summary>
public static class PopupButtonSkin
{
    /// <summary>官方 UISprite 落地路径(UITheme.ButtonTex 与 Addressables Art/ 前缀对应)。</summary>
    public const string UISpritePath = "Assets/Art/UI/Buttons/UISprite.png";

    // 9-slice 边框:官方 UISprite 100x100 圆角矩形,角区 32x32 完整保留圆角
    static readonly Vector4 UiSpriteBorder = new Vector4(16, 16, 16, 16);

    // ---- 语义色(设计系统 token,docs/UIDesignSystem) ----
    static readonly Color BtnPrimary = UITheme.Primary;          // #E97832 橙:确认/完成/主操作
    static readonly Color BtnSurface = UITheme.Panel;            // #FFF9E9 奶白:取消/次级/列表项
    static readonly Color TextOnPrimary = Color.white;           // 橙底白字
    static readonly Color TextOnSurface = UITheme.TextPrimary;   // 奶白底深棕字 #3A2A1A

    /// <summary>橙色按钮节点名(确认/完成/主操作)。</summary>
    static readonly string[] PrimaryNames = { "Confirm", "CloseButton", "EasyButton", "MediumButton", "HardButton" };

    /// <summary>奶白按钮节点名(取消/次级/列表项;设置行按钮同为次级操作)。</summary>
    static readonly string[] SurfaceNames =
        { "Cancel", "ItemTemplate", "SoundButton", "MusicButton", "ThemeButton", "RemoveAdsButton", "PrivacyButton" };

    [MenuItem("Box/Phase8/2. Apply Popup Button Skin (UISprite)")]
    public static void ApplyAll()
    {
        var sprite = EnsureUiSprite();
        if (sprite == null)
        {
            Debug.LogError("[PopupButtonSkin] 官方 UISprite 生成失败,终止换肤");
            return;
        }

        // 主菜单(非弹窗):全部按钮统一橙色(用户已确认,全 App 一致)
        SkinPrefab("Assets/UI/Prefabs/MainMenuView.prefab", sprite, isMainMenu: true);

        // 弹窗:按按钮名语义色区分(确认橙 / 取消奶白)
        string[] popups = {
            "Assets/UI/Prefabs/Popups/AdHintConfirm.prefab",
            "Assets/UI/Prefabs/Popups/DifficultySelect.prefab",
            "Assets/UI/Prefabs/Popups/ExitConfirm.prefab",
            "Assets/UI/Prefabs/Popups/MoreGamesPopup.prefab",
            "Assets/UI/Prefabs/Popups/SettingsPopup.prefab",
            "Assets/UI/Prefabs/Popups/SettlementPopup.prefab",
        };
        foreach (var path in popups) SkinPrefab(path, sprite, isMainMenu: false);

        // 新生成的 UISprite 幂等注册进 Addressables(Phase6 已按目录扫描)
        Phase6AddressablesSetup.RegisterArtAssets();

        AssetDatabase.SaveAssets();
        Debug.Log("[PopupButtonSkin] 换肤完成(官方 UISprite + 语义色 + 英文兜底文案)");
    }

    /// <summary>
    /// 单个 prefab 换肤:按钮图/色 + Label 字色 + 静态文案英文化 + 删 LangButton。
    /// 幂等:LoadPrefabContents 副本上修改 → SaveAsPrefabAsset 覆盖写回(保 GUID)。
    /// </summary>
    static void SkinPrefab(string prefabPath, Sprite btnSprite, bool isMainMenu)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            Debug.LogWarning("[PopupButtonSkin] 缺失 prefab: " + prefabPath);
            return;
        }
        var root = PrefabUtility.LoadPrefabContents(prefabPath);

        // ⑤ 设置弹窗语言切换按钮已移除(Bug 清单 ②):删除 LangButton 节点
        if (!isMainMenu)
        {
            var lang = root.transform.Find("Card/LangButton") ?? root.transform.Find("LangButton");
            if (lang != null)
            {
                Object.DestroyImmediate(lang.gameObject);
                Debug.Log("[PopupButtonSkin] 删除语言切换按钮: " + prefabPath);
            }
        }

        // ②③ 按钮:Image 换官方 UISprite + 语义色;Label 字色同步
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            var btn = img.GetComponent<BoxButton>();
            if (btn == null) continue; // 只处理含 BoxButton 的交互按钮

            Color btnColor, textColor;
            if (isMainMenu || IsNameOf(img, PrimaryNames))
            {
                btnColor = BtnPrimary;   // 橙:主操作
                textColor = TextOnPrimary;
            }
            else if (IsNameOf(img, SurfaceNames))
            {
                btnColor = BtnSurface;   // 奶白:取消/次级/列表项
                textColor = TextOnSurface;
            }
            else
            {
                continue; // 未识别的按钮(如对局内按钮)不动,保持现状
            }

            img.sprite = btnSprite;
            img.type = Image.Type.Sliced;
            img.color = btnColor;

            // Label 子节点字色(按钮文案载体;无 Label 跳过)
            var label = img.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (label != null) label.color = textColor;
        }

        // ④ 静态文案英文化(兜底;运行时 L10n 覆盖,保持与 en 表一致)
        ApplyEnglishTexts(root, Path.GetFileNameWithoutExtension(prefabPath));

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    /// <summary>节点名精确匹配(不匹配父级路径,防误伤)。</summary>
    static bool IsNameOf(Image img, string[] names)
    {
        foreach (var n in names)
            if (img.gameObject.name == n) return true;
        return false;
    }

    /// <summary>
    /// 弹窗静态文案英文化(兜底,与 L10n en 表对齐,防 OnShow 前闪现中文)。
    /// 按 prefab 名分发精确文案(Title/Message 各弹窗不同)。
    /// </summary>
    static void ApplyEnglishTexts(GameObject root, string prefabName)
    {
        // 通用按钮文案(各弹窗一致;CloseButton=完成/关闭,MoreGames/Settings 共用)
        Set("Card/Confirm/Label", prefabName switch
        {
            "AdHintConfirm" => "Watch",
            "SettlementPopup" => "Next",
            _ => "Quit", // ExitConfirm
        });
        Set("Card/Cancel/Label", prefabName == "SettlementPopup" ? "Menu" : "Cancel");
        Set("Card/CloseButton/Label", "Done");

        switch (prefabName)
        {
            case "ExitConfirm":
                Set("Card/Title", "Quit Game");
                Set("Card/Message", "Progress will be lost. Quit?");
                break;
            case "AdHintConfirm":
                Set("Card/Title", "Hints Exhausted");
                Set("Card/Message", "Watch an ad for 1 more hint? Max {0} per game");
                break;
            case "SettlementPopup":
                Set("Card/Title", "Level Complete");
                Set("Card/Message", "Stars 0/3   Time 00:00   Mistakes 0");
                break;
            case "MoreGamesPopup":
                Set("Card/Title", "More Games");
                Set("Card/Content/ItemTemplate/Label", "Game"); // ItemTemplate 在 ScrollView 的 Content 容器下
                break;
            case "SettingsPopup":
                Set("Card/Title", "Settings");
                Set("Card/SoundButton/Label", "Sound: On");
                Set("Card/MusicButton/Label", "Music: On");
                Set("Card/ThemeButton/Label", "Theme: Light");
                Set("Card/RemoveAdsButton/Label", "Remove Ads");
                Set("Card/PrivacyButton/Label", "Privacy Policy");
                break;
        }

        void Set(string path, string text)
        {
            var t = FindInactive(root.transform, path)?.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }
    }

    /// <summary>递归按路径查找 Transform(含未激活对象):Transform.Find 与 foreach 枚举子节点
    /// 都会跳过 inactive,而 MoreGames 列表 ItemTemplate 是 inactive 模板,必须用 GetChild 索引遍历。</summary>
    static Transform FindInactive(Transform root, string path)
    {
        Transform cur = root;
        foreach (var name in path.Split('/'))
        {
            Transform next = null;
            for (int i = 0; i < cur.childCount; i++)
            {
                var child = cur.GetChild(i);
                if (child.name == name) { next = child; break; }
            }
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }

    /// <summary>
    /// 生成官方 UISprite.png(幂等):从 Unity 内置 UI 元素「UI/Skin/UISprite.psd」导出为项目资产,
    /// 配置 9-slice 圆角。内置纹理不可直接 ReadPixels,走 RenderTexture 中转。
    /// 公开:Phase8UiSkin 等历史换肤脚本共用同一官方按钮贴图。
    /// </summary>
    public static Sprite EnsureUiSprite()
    {
        var dir = Path.GetDirectoryName(UISpritePath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(UISpritePath);
        if (existing != null)
        {
            EnsureSpriteImporter(); // 已存在:仅校正 9-slice border(幂等)
            return AssetDatabase.LoadAssetAtPath<Sprite>(UISpritePath);
        }

        var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (builtin == null || builtin.texture == null)
        {
            Debug.LogError("[PopupButtonSkin] 获取官方内置 UISprite 失败(引擎资源缺失?)");
            return null;
        }
        var tex = builtin.texture;

        // 内置纹理不可读 → RenderTexture 中转导出 PNG
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(UISpritePath, readable.EncodeToPNG());
        Object.DestroyImmediate(readable);
        AssetDatabase.Refresh();
        EnsureSpriteImporter();
        Debug.Log("[PopupButtonSkin] 官方 UISprite 导出: " + UISpritePath);
        return AssetDatabase.LoadAssetAtPath<Sprite>(UISpritePath);
    }

    /// <summary>UISprite 导入配置:Single Sprite + 9-slice border(幂等)。</summary>
    static void EnsureSpriteImporter()
    {
        var importer = AssetImporter.GetAtPath(UISpritePath) as TextureImporter;
        if (importer == null) return;
        if (importer.textureType == TextureImporterType.Sprite
            && importer.spriteImportMode == SpriteImportMode.Single
            && importer.spriteBorder == UiSpriteBorder) return; // 已配置
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = UiSpriteBorder;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }
}
