using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 诊断工具:验证 SDF 字符表是否覆盖 L10n 全量中文(缺字根因排查)。
/// CLI 无头执行:unity ... -executeMethod FontVerify.Run
/// </summary>
public static class FontVerify
{
    static readonly string[] Paths =
    {
        "Assets/UI/Fonts/MiSans-Regular-Subset SDF.asset",
        "Assets/UI/Fonts/MiSans-Bold-Subset SDF.asset",
    };

    // L10n 缺字集合(此前 fontTools 对比确认;新 TTF 应已覆盖)
    const string MustHave = "丢做前多对将尽当得文星次浅深盒看级色获观";

    public static void Run()
    {
        foreach (var path in Paths)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) { Debug.LogError("[FontVerify] 未找到: " + path); continue; }
            var have = new System.Collections.Generic.HashSet<char>(
                font.characterTable.Select(ct => (char)ct.unicode));
            var missing = MustHave.Where(c => !have.Contains(c)).ToArray();
            var srcFont = font.sourceFontFile;
            Debug.Log($"[FontVerify] {System.IO.Path.GetFileName(path)}: glyphs={font.characterTable.Count} " +
                      $"源字体={srcFont?.name} 源字形缺失={string.Join("", missing)}");
        }
    }
}
