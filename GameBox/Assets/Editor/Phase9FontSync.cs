using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 9 字体同步工具(可重复执行,配合 Tools/font/rebuild_font_subset.py):
/// 1. 重新导入被覆盖的子集 TTF
/// 2. 从 Localization.cs 提取字符串字面量中文(跳过注释)
/// 3. 对两个 SDF 执行 TryAddCharacters 补烘焙新字形并落盘
/// 4. 自验证:提取集 vs SDF 字符表,缺失必须为 0
/// CLI 无头执行:unity ... -executeMethod Phase9FontSync.Run
/// </summary>
public static class Phase9FontSync
{
    static readonly string[] SdfPaths =
    {
        "Assets/UI/Fonts/MiSans-Regular-Subset SDF.asset",
        "Assets/UI/Fonts/MiSans-Bold-Subset SDF.asset",
    };
    const string TtfPath = "Assets/UI/Fonts/MiSans-Regular-Subset.ttf";
    const string LcPath = "Assets/Services/Abstractions/Localization.cs";

    public static void Run()
    {
        // 1) TTF 被 Python 覆盖 → 强制重新导入,让 SDF 源字体更新
        AssetDatabase.ImportAsset(TtfPath, ImportAssetOptions.ForceUpdate);

        // 2) 提取 L10n 字符串字面量中的中文字符(跳过 // 与 /* */ 注释)
        var chars = ExtractL10nChars(File.ReadAllText(LcPath));

        // 3) 对每个 SDF 补烘焙新字形
        var overall = new HashSet<char>();
        foreach (var path in SdfPaths)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) { Debug.LogError("[FontSync] 未找到 SDF: " + path); continue; }

            var missing = "";
            if (!font.TryAddCharacters(string.Concat(chars), out missing))
                Debug.LogError($"[FontSync] TryAddCharacters 失败: {path} 缺失={missing}");
            else if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[FontSync] 源字体缺字(无法补烘焙): {path} 缺失={missing}");

            // 收集当前 SDF 全部字形,用于最终自验证
            foreach (var ct in font.characterTable)
                overall.Add((char)ct.unicode);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FontSync] {Path.GetFileName(path)}: 字形={font.characterTable.Count}");
        }

        // 4) 自验证:所有 L10n 字符必须已在 SDF 中
        var notCovered = new HashSet<char>(chars);
        notCovered.ExceptWith(overall);
        if (notCovered.Count > 0)
        {
            Debug.LogError($"[FontSync][FAIL] SDF 仍缺字({notCovered.Count}): {Dump(notCovered)}");
            return;
        }
        Debug.Log($"[FontSync][OK] L10n 字符 {chars.Count} 个全部覆盖,零缺失");
    }

    /// <summary>提取字符串字面量中的 CJK 字符(状态机,跳过注释,同 Python 工具逻辑)。</summary>
    static HashSet<char> ExtractL10nChars(string text)
    {
        var outChars = new HashSet<char>();
        int i = 0, n = text.Length;
        bool inStr = false, inBlock = false;
        while (i < n)
        {
            char c = text[i];
            if (inBlock)
            {
                if (c == '*' && i + 1 < n && text[i + 1] == '/') { inBlock = false; i += 2; }
                else i++;
                continue;
            }
            if (inStr)
            {
                if (c == '\\') { i += 2; continue; }
                if (c == '"') inStr = false;
                else if (c >= 0x4E00 && c <= 0x9FFF) outChars.Add(c);
                i++;
                continue;
            }
            if (c == '/' && i + 1 < n)
            {
                if (text[i + 1] == '/') { int j = text.IndexOf('\n', i); i = j < 0 ? n : j + 1; continue; }
                if (text[i + 1] == '*') { inBlock = true; i += 2; continue; }
            }
            if (c == '"') inStr = true;
            i++;
        }
        return outChars;
    }

    static string Dump(IEnumerable<char> chars) => string.Concat(chars);
}
