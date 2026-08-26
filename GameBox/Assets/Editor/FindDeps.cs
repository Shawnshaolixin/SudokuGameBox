using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// 诊断工具:验证 UI prefab → TMP 字体(SDF/TTF) 的 Unity 依赖图是否完整
/// (真机缺字根因排查:Addressables 打包时依赖资产未进 bundle)。
/// CLI 无头执行:unity ... -executeMethod FindDeps.Run
/// </summary>
public static class FindDeps
{
    public static void Run()
    {
        const string prefab = "Assets/UI/Prefabs/MainMenuView.prefab";
        var deps = AssetDatabase.GetDependencies(prefab, true);
        Debug.Log($"[FindDeps] {prefab} 依赖总数: {deps.Length}");
        foreach (var d in deps.Where(d => d.Contains("Fonts") || d.Contains(".ttf") || d.Contains("SDF")))
            Debug.Log($"[FindDeps] 字体依赖: {d}");
        if (!deps.Any(d => d.Contains("Fonts")))
            Debug.LogWarning("[FindDeps] ⚠ prefab 依赖中未发现任何字体资产!");

        // 对照:贴图依赖(已知进包,应出现在依赖里)
        var uiDeps = deps.Where(d => d.Contains("Art/UI")).ToList();
        Debug.Log($"[FindDeps] Art/UI 依赖 {uiDeps.Count} 个: {string.Join(", ", uiDeps)}");

        // 检查 SDF 资产自身引用
        const string sdf = "Assets/UI/Fonts/MiSans-Regular-Subset SDF.asset";
        var sdfDeps = AssetDatabase.GetDependencies(sdf, false);
        Debug.Log($"[FindDeps] SDF 直接依赖: {string.Join(", ", sdfDeps)}");

        // Addressables 组打包模式
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        foreach (var g in settings.groups.Where(g => g != null))
        {
            var bundleMode = g.Schemas.FirstOrDefault(
                s => s.GetType().Name.Contains("BundledAssetGroupSchema"));
            Debug.Log($"[FindDeps] 组 {g.Name} entries={g.entries.Count} bundleSchema={bundleMode?.GetType().Name}");
        }
    }
}
