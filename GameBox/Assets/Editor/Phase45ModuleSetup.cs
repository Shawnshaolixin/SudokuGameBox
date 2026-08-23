using Box.ModuleFramework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 4.5 盒子骨架资源生成器(10 文档 §4.5):ModuleCatalog.asset(模块清单,Resources 兜底路径)。
/// 由 CLI 无头执行:unity run GameBox -- -executeMethod Phase45ModuleSetup.Build
/// 幂等:已存在则只补齐缺失条目(缺 id 才追加),不覆盖既有配置。
/// 新增玩法入口:调用 <see cref="AddEntry"/> 即可(13 文档 §2.3),内置玩法列表下方 AddDefaultEntries 维护。
/// </summary>
public static class Phase45ModuleSetup
{
    const string CatalogPath = "Assets/Resources/Config/ModuleCatalog.asset";

    [MenuItem("Box/Phase4.5/Build Module Framework Assets")]
    public static void Build()
    {
        var catalog = EnsureCatalog();
        AddDefaultEntries(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase4.5] ModuleCatalog ready: {CatalogPath} ({catalog.entries.Length} entries)");
    }

    /// <summary>内置玩法引导清单(首个玩法数独)。新增玩法不写这里,由玩法侧脚本调用 AddEntry。</summary>
    static void AddDefaultEntries(ModuleCatalog catalog)
    {
        AddEntry(catalog, "sudoku", "Box.HotUpdate.Sudoku.SudokuModule", "Gameplay", "数独", 0);
    }

    /// <summary>
    /// 泛化入口:按 id 幂等新增玩法模块条目(缺才追加,不覆盖既有配置)。
    /// 新玩法落地第 3 步(13 文档 §2.3):玩法侧 Editor 脚本在 OnEnter/一键脚本里调用本方法。
    /// </summary>
    /// <param name="catalog">ModuleCatalog 资产(已存在则原地补条目,未加载可先 AssetDatabase.LoadAssetAtPath)。</param>
    /// <param name="id">模块唯一 id(ModuleLoader.EnterAsync 键 + 埋点前缀 {id}.*,§8.4)。</param>
    /// <param name="entryType">入口类型全名(IGameModule 实现,如 Box.HotUpdate.TicTacToe.TicTacToeModule)。</param>
    /// <param name="entryScene">中间态玩法场景名(v1.1 单场景化后废弃,保留字段)。</param>
    /// <param name="displayName">大厅显示名(本地化 key 占位)。</param>
    /// <param name="sortOrder">大厅入口排序。</param>
    public static void AddEntry(
        ModuleCatalog catalog, string id, string entryType,
        string entryScene, string displayName, int sortOrder)
    {
        if (catalog == null) throw new System.ArgumentNullException(nameof(catalog));
        if (string.IsNullOrEmpty(id)) return;
        if (Contains(catalog, id)) return;

        var entries = new ModuleEntry[catalog.entries.Length + 1];
        System.Array.Copy(catalog.entries, entries, catalog.entries.Length);
        entries[entries.Length - 1] = new ModuleEntry
        {
            id = id,
            entryType = entryType,
            entryScene = entryScene,
            displayName = displayName,
            enabled = true,
            sortOrder = sortOrder,
        };
        catalog.entries = entries;
        EditorUtility.SetDirty(catalog);
        Debug.Log($"[Phase4.5] ModuleCatalog +{id} ({displayName}) -> {entries.Length} entries");
    }

    /// <summary>确保清单资产存在(Resources 兜底路径),返回加载/新实例。</summary>
    static ModuleCatalog EnsureCatalog()
    {
        EnsureFolder("Assets/Resources/Config");
        var catalog = AssetDatabase.LoadAssetAtPath<ModuleCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ModuleCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        return catalog;
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
