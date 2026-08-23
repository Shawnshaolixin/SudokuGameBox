using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Phase 6 首包瘦身:SDF 字体重建为预烘焙模式(基于子集 TTF)。
/// 背景:旧 SDF 为 Dynamic + TMP 全局 m_ClearDynamicDataOnBuild=true → 构建时
/// TMP_PreBuildProcessor 清空字形表 → 真机首帧运行时烘焙全部 UI 中文字形 = 长帧。
/// 方案:1) 基于 MiSans-Regular-Subset.ttf / MiSans-Bold-Subset.ttf(fonttools 子集化,
/// 323 字形)以 AtlasPopulationMode.Dynamic 重建(CreateFontAsset 原生路径,源字体自动挂载);
/// 2) 反射关闭 per-asset m_ClearDynamicDataOnBuild=false(构建保留字形数据,不会首帧烘焙);
/// 3) TryAddCharacters(全子集字符) 编辑器内预烘焙 323 字形(自动 Reinitialize 1024x1024 atlas);
/// 4) TMP Settings 默认字体 + 6 prefab 的 TMP 引用重映射到新 SDF;
/// 5) 保留 Dynamic 模式:子集外字符(如玩家输入)运行时仍可动态兜底。
/// CLI 无头执行:unity ... -executeMethod Phase6FontRebuild.Run
/// 幂等:先删旧 Subset SDF 再重建(GUID 变化 → 重映射配套执行)。
/// </summary>
public static class Phase6FontRebuild
{
    const string FontsDir = "Assets/UI/Fonts";
    static readonly string[] SubsetSdfPaths =
    {
        FontsDir + "/MiSans-Regular-Subset SDF.asset",
        FontsDir + "/MiSans-Bold-Subset SDF.asset",
    };

    // 与旧 SDF 保持视觉一致的烘焙参数
    const int SamplingPointSize = 90;
    const int AtlasPadding = 9;
    const int AtlasWidth = 1024;
    const int AtlasHeight = 1024;

    static readonly string[] PrefabPaths =
    {
        "Assets/UI/Prefabs/GameplayView.prefab",
        "Assets/UI/Prefabs/MainMenuView.prefab",
        "Assets/UI/Prefabs/Popups/DifficultySelect.prefab",
        "Assets/UI/Prefabs/Popups/ExitConfirm.prefab",
        "Assets/UI/Prefabs/Popups/SettingsPopup.prefab",
        "Assets/UI/Prefabs/Popups/SettlementPopup.prefab",
    };

    [MenuItem("Box/Phase6/3. Rebuild SDF (Subset Prebaked)")]
    public static void Run()
    {
        // 1) 删除上一轮遗留的空字形 SDF(Static) → 以新 GUID 重建;引用随后重映射修复
        foreach (var p in SubsetSdfPaths)
            if (File.Exists(p)) AssetDatabase.DeleteAsset(p);
        AssetDatabase.SaveAssets();

        // 2) 重建(Dynamic + 预烘焙)
        var regular = EnsureSubsetSdf("MiSans-Regular-Subset.ttf", "MiSans-Regular-Subset SDF.asset");
        var bold = EnsureSubsetSdf("MiSans-Bold-Subset.ttf", "MiSans-Bold-Subset SDF.asset");
        if (regular == null) { Debug.LogError("[Phase6Font] Regular SDF 创建失败,终止"); return; }

        // 3) 默认字体 + prefab 引用重映射(新 GUID)
        SetDefaultFont(regular);
        ReplaceInPrefabs(regular, bold);
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6Font] done: prebaked subset SDF + references remapped");
    }

    static string LoadCharset()
    {
        var p = "D:/Projects/AI/SudokuGameBox/docs/字体子集字符集_完整.txt";
        if (File.Exists(p)) return File.ReadAllText(p, System.Text.Encoding.UTF8);
        Debug.LogWarning("[Phase6Font] 字符集文件未找到,用默认子集(空)");
        return string.Empty;
    }

    static string Safe(string s, int n) => s == null ? "(null)" : (s.Length > n ? s.Substring(0, n) : s);

    static TMP_FontAsset EnsureSubsetSdf(string ttf, string assetName)
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontsDir + "/" + ttf);
        if (font == null) { Debug.LogError("[Phase6Font] 子集 TTF 未导入: " + ttf); return null; }
        var path = FontsDir + "/" + assetName;

        // Dynamic 原生创建路径(源字体自动挂载;multiAtlas=true 与旧 SDF 一致,323 字形需多张 atlas)
        var asset = TMP_FontAsset.CreateFontAsset(
            font, SamplingPointSize, AtlasPadding,
            GlyphRenderMode.SDFAA, AtlasWidth, AtlasHeight,
            AtlasPopulationMode.Dynamic, true /*multiAtlas*/);
        if (asset == null) return null;

        // 反射关闭 per-asset 构建清空(TMP 全局 m_ClearDynamicDataOnBuild=true → 默认继承 true)
        var fi = typeof(TMP_FontAsset).GetField("m_ClearDynamicDataOnBuild",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null && fi.FieldType == typeof(bool)) fi.SetValue(asset, false);

        AssetDatabase.CreateAsset(asset, path);
        foreach (var tex in asset.atlasTextures)
            if (tex != null) AssetDatabase.AddObjectToAsset(tex, asset);
        if (asset.material != null) AssetDatabase.AddObjectToAsset(asset.material, asset);

        // 预烘焙全部子集字符(Dynamic 模式放行;自动 Reinitialize atlas + 打包)
        var charset = LoadCharset();
        Debug.Log($"[Phase6Font] {assetName} charset len={charset.Length} head=\"{Safe(charset, 36)}\"");
        bool ok = asset.TryAddCharacters(charset, out string missing);
        string m = missing ?? "";
        Debug.Log($"[Phase6Font] {assetName} TryAdd ok={ok} missingLen={m.Length} head=\"{Safe(m, 40)}\"");

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase6Font] created {assetName}: glyphs={asset.characterTable.Count} atlasW={asset.atlasTexture?.width} mode={asset.atlasPopulationMode}");
        return asset;
    }

    static void SetDefaultFont(TMP_FontAsset font)
    {
        var settings = TMP_Settings.instance;
        if (settings == null) return;
        TMP_Settings.defaultFontAsset = font;
        EditorUtility.SetDirty(settings);
    }

    static void ReplaceInPrefabs(TMP_FontAsset regular, TMP_FontAsset bold)
    {
        foreach (var path in PrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[Phase6Font] prefab 不存在: " + path); continue; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            int count = 0;
            foreach (var tmp in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                tmp.font = (tmp.fontStyle & FontStyles.Bold) != 0 ? bold : regular;
                count++;
            }
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log($"[Phase6Font] {path}: {count} TMP 字体已重映射");
        }
    }

    /// <summary>
    /// 清理遗留旧字体资产:旧 SDF(已确认无引用,prefab/TMP Settings 全指向新 SDF)
    /// + 旧 TTF(旧 SDF 删除后无引用;构建时 Dynamic 源字体由新子集 TTF 承担)。
    /// 必须在重建 + 重映射 + EditMode 全量验证通过后执行。
    /// </summary>
    [MenuItem("Box/Phase6/4. Remove Legacy Fonts (Old SDF + TTF)")]
    public static void RemoveLegacyFonts()
    {
        string[] legacy =
        {
            FontsDir + "/MiSans-Regular SDF.asset",
            FontsDir + "/MiSans-Bold SDF.asset",
            FontsDir + "/MiSans-Regular.ttf",
            FontsDir + "/MiSans-Bold.ttf",
        };
        foreach (var p in legacy)
        {
            if (!File.Exists(p)) { Debug.LogWarning("[Phase6Font] 旧资产已不存在: " + p); continue; }
            AssetDatabase.DeleteAsset(p);
            Debug.Log("[Phase6Font] deleted legacy: " + p);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6Font] done: legacy fonts removed (SDF ~2.7MB + TTF ~15.2MB)");
    }

    /// <summary>
    /// todo 8:裁剪 TMP 安装包示例资产(工程未引用)。
    /// 删:LiberationSans(FFonts/ TTF + Resources/Fonts & Materials/ SDF 与材质,~2.5MB,
    /// prefab 零引用且 TMP 默认字体已换 MiSans)+ EmojiOne 示例 sprite(Sprites/ + Resources/Sprite Assets/,~0.14MB,
    /// 仅 TMP Settings 默认 sprite 引用,ui 未用 → 顺带把默认 sprite 置空)。
    /// 保留:Shaders/(TMP 渲染必需)、Resources/Style Sheets/(默认样式表被 Settings 引用)。
    /// </summary>
    [MenuItem("Box/Phase6/5. Trim TMP Examples")]
    public static void TrimTmpExamples()
    {
        string[] trim =
        {
            "Assets/TextMesh Pro/Fonts/LiberationSans.ttf",
            "Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt",
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset",
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Drop Shadow.mat",
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Outline.mat",
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset",
            "Assets/TextMesh Pro/Sprites/EmojiOne.json",
            "Assets/TextMesh Pro/Sprites/EmojiOne.png",
            "Assets/TextMesh Pro/Sprites/EmojiOne Attribution.txt",
            "Assets/TextMesh Pro/Resources/Sprite Assets/EmojiOne.asset",
        };
        foreach (var p in trim)
            if (File.Exists(p)) AssetDatabase.DeleteAsset(p);
            else Debug.LogWarning("[Phase6Font] TMP 示例已不存在: " + p);

        // 默认 sprite 置空(EmojiOne 删除后引用悬空;UI 未用 sprite)
        var settings = TMP_Settings.instance;
        if (settings != null)
        {
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultSpriteAsset");
            if (prop != null) { prop.objectReferenceValue = null; so.ApplyModifiedProperties(); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6Font] done: TMP examples trimmed (~2.7MB)");
    }

    /// <summary>
    /// todo 8:删除工程遗留 2D 场景模板 Assets/Settings/Lit2DSceneTemplate.scenetemplate(~3.85MB,
    /// 零引用)+ 模板场景(URP2DSceneTemplate.unity)。保留 Renderer2D/UniversalRP(URP 管线必需)。
    /// </summary>
    [MenuItem("Box/Phase6/6. Trim Scene Template")]
    public static void TrimSceneTemplate()
    {
        string[] trim =
        {
            "Assets/Settings/Lit2DSceneTemplate.scenetemplate",
            "Assets/Settings/Scenes/URP2DSceneTemplate.unity",
        };
        foreach (var p in trim)
            if (File.Exists(p)) { AssetDatabase.DeleteAsset(p); Debug.Log("[Phase6Font] deleted: " + p); }
            else Debug.LogWarning("[Phase6Font] 已不存在: " + p);
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6Font] done: scene template trimmed (~3.85MB)");
    }
}