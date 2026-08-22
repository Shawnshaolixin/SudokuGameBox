using Box.ModuleFramework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 4.5 盒子骨架资源生成器(10 文档 §4.5):ModuleCatalog.asset(模块清单,Resources 兜底路径)。
/// 由 CLI 无头执行:unity run GameBox -- -executeMethod Phase45ModuleSetup.Build
/// 幂等:已存在则只补齐缺失条目(缺 id 才追加),不覆盖既有配置。
/// </summary>
public static class Phase45ModuleSetup
{
    const string CatalogPath = "Assets/Resources/Config/ModuleCatalog.asset";

    [MenuItem("Box/Phase4.5/Build Module Framework Assets")]
    public static void Build()
    {
        EnsureFolder("Assets/Resources/Config");
        var catalog = AssetDatabase.LoadAssetAtPath<ModuleCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ModuleCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        if (!Contains(catalog, "sudoku"))
        {
            var entries = new ModuleEntry[catalog.entries.Length + 1];
            System.Array.Copy(catalog.entries, entries, catalog.entries.Length);
            entries[entries.Length - 1] = new ModuleEntry
            {
                id = "sudoku",
                entryType = "Box.HotUpdate.Sudoku.SudokuModule",
                entryScene = "Gameplay",
                displayName = "数独",
                enabled = true,
                sortOrder = 0,
            };
            catalog.entries = entries;
            EditorUtility.SetDirty(catalog);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase4.5] ModuleCatalog ready: {CatalogPath} ({catalog.entries.Length} entries)");
    }

    static bool Contains(ModuleCatalog catalog, string id)
    {
        foreach (var e in catalog.entries)
            if (e != null && e.id == id) return true;
        return false;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        if (string.IsNullOrEmpty(parent))
            AssetDatabase.CreateFolder("Assets", name);
        else
            AssetDatabase.CreateFolder(parent, name);
    }
}
