using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 字体管线(真机文字不显示修复):工程无 TMP Settings.asset,prefab 全部 TMP 组件
/// m_fontAsset 为空引用 → 真机文本不渲染。导入 MiSans(中英文全覆盖) →
/// 创建 TMP 动态字体资产(运行时按需生成 atlas,无需预烘焙中文字库) →
/// 创建 TMP Settings.asset 设默认字体 → 批量替换 prefab 组件字体引用。
/// CLI 无头执行:unity run GameBox -- -executeMethod FontSetup.Run
/// 幂等:资产与 Settings 已存在则跳过,组件替换无条件(引用已有效时无副作用)。
/// </summary>
public static class FontSetup
{
    const string FontsDir = "Assets/UI/Fonts";
    // 模板位置(唯一):Assets/Resources/TMP Settings.asset 曾存在冗余实例,已删除
    const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    [MenuItem("Box/Phase4/Font Setup")]
    public static void Run()
    {
        // 前置:TMP Essential Resources 已手动解包到 Assets/TextMesh Pro(shaders/TMP Settings 模板)。
        // 工程曾无 shader → 无头模式 Shader.Find 返回 null,字体资产材质创建失败。

        // 顺序敏感:CreateFontAsset 内部读取 TMP_Settings(clearDynamicDataOnBuild 等),
        // Settings 资产缺失时 instance 为 null 抛 NRE → 必须先确保 Settings 存在。
        EnsureSettings();
        var regular = EnsureTmpFont("MiSans-Regular.ttf", "MiSans-Regular SDF.asset");
        var bold = EnsureTmpFont("MiSans-Bold.ttf", "MiSans-Bold SDF.asset");
        if (regular == null) { Debug.LogError("[FontSetup] 字体资产创建失败,终止"); return; }

        SetDefaultFont(regular);
        ReplaceInPrefabs(regular, bold);
        AssetDatabase.SaveAssets();
        Debug.Log("[FontSetup] done: TMP font assets + TMP Settings + prefab references replaced");
    }

    static TMP_FontAsset EnsureTmpFont(string ttf, string assetName)
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontsDir + "/" + ttf);
        if (font == null) { Debug.LogError("[FontSetup] 字体文件未导入: " + ttf); return null; }
        var path = FontsDir + "/" + assetName;
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null && existing.atlasTexture != null) return existing;
        if (existing != null)
        {
            // 旧资产 atlas 纹理丢失(未保存子资产,m_AtlasTextures 全 fileID:0):删除重建
            Debug.LogWarning("[FontSetup] atlas 纹理缺失,重建: " + assetName);
            AssetDatabase.DeleteAsset(path);
        }
        // Dynamic + multiAtlas:运行时按需添加 glyph,中英文全字符集无需预烘焙
        Debug.Log($"[FontSetup] font={font.name} data={(font.fontNames != null ? font.fontNames.Length : -1)}");
        var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        Debug.Log($"[FontSetup] created asset={(asset != null ? asset.name : "NULL")}");
        if (asset == null) return null;
        AssetDatabase.CreateAsset(asset, path);
        // 动态字体的 atlas 纹理与材质是独立对象,必须保存为子资产;
        // 否则 CreateAsset 后引用丢失 → 重载时 m_AtlasTextures 为 null → TMP 运行时抛 UnassignedReferenceException
        foreach (var tex in asset.atlasTextures)
            if (tex != null) AssetDatabase.AddObjectToAsset(tex, asset);
        if (asset.material != null) AssetDatabase.AddObjectToAsset(asset.material, asset);
        return asset;
    }

    static void EnsureSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath);
        if (settings != null) return;
        settings = ScriptableObject.CreateInstance<TMP_Settings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
    }

    static void SetDefaultFont(TMP_FontAsset font)
    {
        var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath);
        if (settings == null) return;
        // 无条件覆盖:模板默认是 LiberationSans(无中文字形),运行时新建 TMP 组件(棋盘格)需 MiSans
        TMP_Settings.defaultFontAsset = font;
        EditorUtility.SetDirty(settings);
    }

    static void ReplaceInPrefabs(TMP_FontAsset regular, TMP_FontAsset bold)
    {
        // 2026-08-30 资源归属分离后实际位置:公共 UI 在 UI/Prefabs,玩法模块在 Modules/Sudoku/Prefabs
        string[] paths =
        {
            "Assets/UI/Prefabs/MainMenuView.prefab",
            "Assets/Modules/Sudoku/Prefabs/GameplayView.prefab",
            "Assets/UI/Prefabs/Popups/SettlementPopup.prefab",
            "Assets/Modules/Sudoku/Prefabs/DifficultySelect.prefab",
        };
        foreach (var path in paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            int count = 0;
            foreach (var tmp in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                tmp.font = (tmp.fontStyle & FontStyles.Bold) != 0 ? bold : regular;
                count++;
            }
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log($"[FontSetup] {path}: {count} TMP 组件字体已替换");
        }
    }
}
