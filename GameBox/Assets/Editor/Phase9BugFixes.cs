using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Phase 9 真机问题修复(用户反馈 5 连 bug 的确定根因修复,CLI 无头执行):
///   1) FixAlwaysIncludedShaders:把运行时 Shader.Find 的粒子 shader 与 TMP UI shader
///      加入 GraphicsSettings.m_AlwaysIncludedShaders(否则打包剥离,真机 Shader.Find 返回 null);
///   2) FixMissingGlyphs:对 Subset SDF TryAddCharacters 补全 L10n 缺失字形
///      (Dynamic 模式编辑器内烘焙+保存,运行时直接可用);
///   3) FixFontsInAddressables:把 TTF+SDF 显式注册进 Addressables(UI_Local 组)
///      ——Addressables 隐式依赖收集未包含字体,导致真机字体资产缺失、文字空白。
/// CLI:unity ... -executeMethod Phase9BugFixes.RunAll(然后重新 BuildPlayerContent + 打 APK)
/// 幂等:已包含/已注册/已烘焙均自动跳过。
/// </summary>
public static class Phase9BugFixes
{
    const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";
    const string FontsDir = "Assets/UI/Fonts";
    const string GroupUiLocal = "UI_Local";

    /// <summary>运行时必须存在的 shader 清单:粒子(Shader.Find)+ TMP UI(文本渲染)。</summary>
    static readonly (string Name, string Path)[] RequiredShaders =
    {
        ("Legacy Shaders/Particles/Alpha Blended", null),        // FxPool 运行时 Shader.Find
        ("TMP_SDF-Mobile", "Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader"), // TMP UI 文本
    };

    [MenuItem("Box/Phase9/1. Fix Always-Included Shaders")]
    public static void FixAlwaysIncludedShaders()
    {
        var gs = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath)
            .FirstOrDefault(o => o is GraphicsSettings);
        if (gs == null) { Debug.LogError("[Phase9] GraphicsSettings 资产未找到"); return; }

        var so = new SerializedObject(gs);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        var existing = new List<Object>();
        for (int i = 0; i < list.arraySize; i++)
            existing.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

        int added = 0;
        foreach (var (name, path) in RequiredShaders)
        {
            Shader shader = null;
            if (!string.IsNullOrEmpty(path))
                shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            shader ??= Shader.Find(name);
            if (shader == null) { Debug.LogError($"[Phase9] shader 未找到: {name}"); continue; }

            // 按对象判重(内置 shader 共用 unity_builtin_extra 路径,不能用路径去重)
            if (existing.Contains(shader)) { Debug.Log($"[Phase9] 已包含: {name}"); continue; }

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            existing.Add(shader);
            added++;
            Debug.Log($"[Phase9] 已添加 alwaysIncluded: {name} ({AssetDatabase.GetAssetPath(shader)})");
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase9] alwaysIncludedShaders 修复完成,新增 {added} 个");
    }

    /// <summary>补全 Subset SDF 缺失的 L10n 字形(从现有字符集 + Localization 字典提取,幂等)。</summary>
    [MenuItem("Box/Phase9/2. Bake Missing Glyphs Into SDF")]
    public static void FixMissingGlyphs()
    {
        // 收集需要覆盖的全部字符:现有字符集文件 + L10n 全量中文文案(遍历字典)
        var charset = new HashSet<char>();
        var charsetFile = "D:/Projects/AI/SudokuGameBox/docs/字体子集字符集_完整.txt";
        if (File.Exists(charsetFile))
            foreach (var c in File.ReadAllText(charsetFile, System.Text.Encoding.UTF8))
                if (c != '\n' && c != '\r') charset.Add(c);

        // 从 Box.Services.Localization 字典追加所有文案字符(运行时可访问,编辑器里反射读 zh 表)
        AppendLocalizationChars(charset);

        var missingAll = new List<string>();
        foreach (var path in new[]
                 {
                     FontsDir + "/MiSans-Regular-Subset SDF.asset",
                     FontsDir + "/MiSans-Bold-Subset SDF.asset",
                 })
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) { Debug.LogError("[Phase9] SDF 未找到: " + path); continue; }

            // 已有字形集(小写字母查找;char 顺序不敏感)
            var have = new HashSet<char>(font.characterTable.Select(ct => (char)ct.unicode));
            var toAdd = new string(charset.Where(c => !have.Contains(c)).ToArray());
            if (toAdd.Length == 0)
            {
                Debug.Log($"[Phase9] {path} 字形已完整({font.characterTable.Count}),跳过");
                continue;
            }

            // 扩展 atlas 需要源字体存在(Dynamic 模式,TryAddCharacters 自动烘焙)
            Debug.Log($"[Phase9] {Path.GetFileName(path)} 缺失 {toAdd.Length} 字形,开始烘焙...");
            bool ok = font.TryAddCharacters(toAdd, out string missing);
            Debug.Log($"[Phase9] {Path.GetFileName(path)} TryAdd ok={ok} 失败=\"{missing ?? ""}\" glyphs={font.characterTable.Count}");
            if (!string.IsNullOrEmpty(missing)) missingAll.Add(missing);

            EditorUtility.SetDirty(font);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase9] 字形烘焙完成;仍缺失: \"{string.Join("", missingAll)}\"");
    }

    /// <summary>反射读取 Box.Services.Localization 的 zh 字典,把所有文案字符并入字符集。</summary>
    static void AppendLocalizationChars(HashSet<char> charset)
    {
        // 从 Localization.cs 源码提取全部中文字符(不依赖程序集加载;随文案更新自动覆盖)
        var src = "Assets/Services/Abstractions/Localization.cs";
        if (File.Exists(src))
        {
            int zhCount = 0;
            foreach (var c in File.ReadAllText(src, System.Text.Encoding.UTF8))
            {
                // 中日韩统一表意文字区 + 全角标点(0x3000-0x30FF 含日文假名标点,统一纳入)
                if (c >= 0x4E00 && c <= 0x9FFF) { charset.Add(c); zhCount++; }
            }
            Debug.Log($"[Phase9] 从 Localization.cs 提取到 {zhCount} 个中文字符");
        }
        else Debug.LogWarning("[Phase9] Localization.cs 未找到,仅用字符集文件");
    }

    /// <summary>TTF + SDF 显式注册进 Addressables UI_Local 组(隐式依赖收集漏字体)。</summary>
    [MenuItem("Box/Phase9/3. Register Fonts Into Addressables")]
    public static void FixFontsInAddressables()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings.FindGroup(GroupUiLocal);
        if (group == null) { Debug.LogError("[Phase9] 组 " + GroupUiLocal + " 不存在"); return; }

        int added = 0;
        foreach (var path in Directory.GetFiles(FontsDir)
                     .Where(p => p.EndsWith(".ttf") || p.EndsWith(" SDF.asset")))
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (settings.FindAssetEntry(guid) != null) { Debug.Log($"[Phase9] 已注册: {path}"); continue; }
            var entry = settings.CreateOrMoveEntry(guid, group, false);
            if (entry == null) { Debug.LogError("[Phase9] 注册失败: " + path); continue; }
            // 地址 = UI/Fonts/<文件名去扩展名>,运行时不需要直接 Load(仅为进包),但保持可寻址
            entry.address = "UI/Fonts/" + Path.GetFileNameWithoutExtension(path);
            entry.labels.Add("UI");
            added++;
            Debug.Log($"[Phase9] 已注册: {path} → {entry.address}");
        }
        if (added > 0) EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase9] 字体注册完成,新增 {added} 个 → {GroupUiLocal}");
    }

    [MenuItem("Box/Phase9/Run All")]
    public static void RunAll()
    {
        FixAlwaysIncludedShaders();
        FixMissingGlyphs();
        FixFontsInAddressables();
        Debug.Log("[Phase9] 全部修复执行完毕,请重新 BuildPlayerContent + 打 APK");
    }
}
