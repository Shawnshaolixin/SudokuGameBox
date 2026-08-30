using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Phase 6 Addressables 初始化与资源迁移(10 文档 Phase 6;11 文档 §3.3 分组架构)。
/// CLI 无头执行(方式②):
///   1) 初始化:unity ... -executeMethod Phase6AddressablesSetup.EnsureSetup
///   2) 迁移:  unity ... -executeMethod Phase6AddressablesSetup.MigrateResources
/// 幂等:已存在/已迁移/已入库均自动跳过,可反复执行。
/// 分组:Core(核心通用)、UI_Local(UI 预制体,首包内)、Art_Audio(美术音频,v1.1 预留远程)。
/// 注意:Addressables 无法直接引用 Resources 目录内的资源,迁移必须物理移出 Resources
/// (AssetDatabase.MoveAsset 保 GUID,引用不断)。
/// </summary>
public static class Phase6AddressablesSetup
{
    const string SettingsPath = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";
    const string GroupUiLocal = "UI_Local";
    const string GroupArtAudio = "Art_Audio";
    const string GroupCore = "Core";
    const string GroupModuleSudoku = "Module_Sudoku"; // 玩法模块组(v2.0 前本地组,热更时切远程,§3.3 模块资源隔离)

    // 模块独有资源迁移表(Sudoku):源路径 → 模块目录目标路径 + 模块地址
    // 判定标准(2026-08-30):仅 sudoku 模块使用的资源;框架/多模块共用的留公共组(UI_Local/Art_Audio)。
    // 物理移动保 GUID(Addressables 条目与场景引用不断),幂等:已迁移跳过。
    static readonly (string Src, string Dst, string Address)[] SudokuMigrations =
    {
        ("Assets/UI/Prefabs/GameplayView.prefab", "Assets/Modules/Sudoku/Prefabs/GameplayView.prefab", "Sudoku/Prefabs/GameplayView"),
        ("Assets/UI/Prefabs/Popups/DifficultySelect.prefab", "Assets/Modules/Sudoku/Prefabs/DifficultySelect.prefab", "Sudoku/Prefabs/DifficultySelect"),
        ("Assets/Art/Effects/Particles/star_01_particle.png", "Assets/Modules/Sudoku/Fx/star_01_particle.png", "Sudoku/Fx/star_01_particle"),
        ("Assets/Art/Effects/Particles/spark_01_particle.png", "Assets/Modules/Sudoku/Fx/spark_01_particle.png", "Sudoku/Fx/spark_01_particle"),
        ("Assets/Art/Effects/Particles/star_04_particle.png", "Assets/Modules/Sudoku/Fx/star_04_particle.png", "Sudoku/Fx/star_04_particle"),
        ("Assets/Art/Audio/SFX/switch1.ogg", "Assets/Modules/Sudoku/Audio/switch1.ogg", "Sudoku/Audio/switch1"),
        ("Assets/Art/Audio/SFX/switch4.ogg", "Assets/Modules/Sudoku/Audio/switch4.ogg", "Sudoku/Audio/switch4"),
        ("Assets/Art/Audio/SFX/switch38.ogg", "Assets/Modules/Sudoku/Audio/switch38.ogg", "Sudoku/Audio/switch38"),
    };

    // Resources/UI 迁移表:源相对路径 → 目标相对路径 + Addressables 地址
    static readonly (string Src, string Dst, string Address)[] UiMigrations =
    {
        ("Assets/Resources/UI/GameplayView.prefab", "Assets/UI/Prefabs/GameplayView.prefab", "UI/GameplayView"),
        ("Assets/Resources/UI/MainMenuView.prefab", "Assets/UI/Prefabs/MainMenuView.prefab", "UI/MainMenuView"),
        ("Assets/Resources/UI/Popups/DifficultySelect.prefab", "Assets/UI/Prefabs/Popups/DifficultySelect.prefab", "UI/Popups/DifficultySelect"),
        ("Assets/Resources/UI/Popups/ExitConfirm.prefab", "Assets/UI/Prefabs/Popups/ExitConfirm.prefab", "UI/Popups/ExitConfirm"),
        ("Assets/Resources/UI/Popups/SettingsPopup.prefab", "Assets/UI/Prefabs/Popups/SettingsPopup.prefab", "UI/Popups/SettingsPopup"),
        ("Assets/Resources/UI/Popups/SettlementPopup.prefab", "Assets/UI/Prefabs/Popups/SettlementPopup.prefab", "UI/Popups/SettlementPopup"),
    };

    /// <summary>一键入口:初始化 + 迁移(CLI 单次执行用)。</summary>
    [MenuItem("Box/Phase6/Run All (Init + Migrate)")]
    public static void RunAll()
    {
        EnsureSetup();
        MigrateResources();
    }

    [MenuItem("Box/Phase6/1. Initialize Addressables Setup")]
    public static void EnsureSetup()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            settings = AddressableAssetSettings.Create(
                "Assets/AddressableAssetsData", "AddressableAssetSettings", true, true);
            if (settings == null)
            {
                Debug.LogError("[Phase6] AddressableAssetSettings 创建失败,请确认包已安装");
                return;
            }
            AddressableAssetSettingsDefaultObject.Settings = settings;
        }

        EnsureGroups(settings); // 建组无条件执行(幂等),settings 已存在时也能补齐新组
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6] Addressables 初始化完成:Default Local Group + "
                  + string.Join(", ", settings.groups.Select(g => g.Name)));
    }

    [MenuItem("Box/Phase6/2. Migrate Resources/UI -> Addressables")]
    public static void MigrateResources()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EnsureSetup();
            settings = AddressableAssetSettingsDefaultObject.Settings;
        }
        var uiGroup = settings.FindGroup(GroupUiLocal);
        if (uiGroup == null)
        {
            Debug.LogError("[Phase6] 分组 " + GroupUiLocal + " 不存在,请先执行 EnsureSetup");
            return;
        }

        // ① 物理移出 Resources(保 GUID)
        foreach (var t in UiMigrations)
        {
            if (File.Exists(t.Dst)) continue; // 已迁移
            if (!File.Exists(t.Src))
            {
                Debug.LogWarning("[Phase6] 缺失源文件(可能已迁移): " + t.Src);
                continue;
            }
            EnsureFolder(Path.GetDirectoryName(t.Dst));
            var err = AssetDatabase.MoveAsset(t.Src, t.Dst);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError("[Phase6] 移动失败 " + t.Src + " → " + t.Dst + " : " + err);
                continue;
            }
            Debug.Log("[Phase6] 已移动: " + t.Src + " → " + t.Dst);
        }

        // ② CreateOrMoveEntry 入库 UI_Local 组,显式设地址(旧 Resources.Load 路径改为 "UI/xxx")
        foreach (var t in UiMigrations)
        {
            if (!File.Exists(t.Dst)) continue;
            var guid = AssetDatabase.AssetPathToGUID(t.Dst);
            if (string.IsNullOrEmpty(guid)) continue;
            if (settings.FindAssetEntry(guid) != null) continue; // 已入库
            var entry = settings.CreateOrMoveEntry(guid, uiGroup, false);
            if (entry != null)
            {
                entry.address = t.Address;
                entry.labels.Add("UI");
            }
        }

EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6] Resources/UI 迁移完成 → " + GroupUiLocal + " (" + uiGroup.entries.Count + " entries)");
    }

    /// <summary>
    /// 注册 Assets/UI/Prefabs 下全部 prefab 到 UI_Local(Phase 7 后新增 prefab 走此入口,幂等):
    /// 地址约定 = 相对 UI/Prefabs 的路径去扩展名(如 Popups/AdHintConfirm → "UI/Popups/AdHintConfirm")。
    /// Phase 6 迁移表只覆盖当时 6 个资源;新增 prefab 直接放入 UI/Prefabs 后执行本方法即可被 Addressables 加载。
    /// </summary>
    [MenuItem("Box/Phase6/3. Ensure All UI Prefabs Registered")]
    public static void EnsureUiRegistered()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EnsureSetup();
            settings = AddressableAssetSettingsDefaultObject.Settings;
        }
        var uiGroup = settings.FindGroup(GroupUiLocal);
        if (uiGroup == null)
        {
            Debug.LogError("[Phase6] 分组 " + GroupUiLocal + " 不存在,请先执行 EnsureSetup");
            return;
        }

        const string prefabRoot = "Assets/UI/Prefabs";
        const string addressPrefix = "UI/";
        int added = 0;
        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { prefabRoot }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(path);
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid) || settings.FindAssetEntry(guid) != null) continue; // 已入库
            var entry = settings.CreateOrMoveEntry(guid, uiGroup, false);
            if (entry == null) continue;
            entry.address = addressPrefix + assetPath.Substring(prefabRoot.Length + 1)
                .Replace(".prefab", "").Replace('\\', '/');
            entry.labels.Add("UI");
            added++;
            Debug.Log("[Phase6] 新注册: " + assetPath + " → " + entry.address);
        }
        if (added > 0) EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6] UI prefab 注册完成,新增 " + added + " 个 → " + GroupUiLocal);
    }

    /// <summary>
    /// 注册 Assets/Art 下的美术/音频资源到 Art_Audio 分组(Phase 7 收尾后首包资源入口,幂等):
    /// 覆盖 t:AudioClip(ogg) 与 t:Texture2D(png),地址约定 = "Art/" + 相对路径去扩展名,
    /// 如 Art/Audio/SFX/click1 → 运行时经 Addressables.LoadAssetAsync<AudioClip>("Art/Audio/SFX/click1") 加载。
    /// 资源文件放入 Assets/Art/{Audio,Effects,UI} 后执行本方法即可被 Addressables 引用(首包内,不依赖远程)。
    /// </summary>
    [MenuItem("Box/Phase6/4. Ensure Art Assets Registered")]
    public static void RegisterArtAssets()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EnsureSetup();
            settings = AddressableAssetSettingsDefaultObject.Settings;
        }
        var artGroup = settings.FindGroup(GroupArtAudio);
        if (artGroup == null)
        {
            Debug.LogError("[Phase6] 分组 " + GroupArtAudio + " 不存在,请先执行 EnsureSetup");
            return;
        }

        const string artRoot = "Assets/Art";
        const string addressPrefix = "Art/";
        int added = 0;
        // 只注册 AudioClip 与 Texture2D(跳过文件夹/其他资源类型)
        foreach (var type in new[] { "t:AudioClip", "t:Texture2D" })
        {
            foreach (var guid in AssetDatabase.FindAssets(type, new[] { artRoot }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                // Texture2D 会命中同目录 .meta?不会——FindAssets 只返回主资源;但 sprite 子资源不入库
                if (!assetPath.StartsWith(artRoot)) continue;
                // 期望地址:去扩展名(ogg/wav/png),运行时按 "Art/..." 不带扩展名加载
                var expected = addressPrefix + assetPath.Substring(artRoot.Length + 1)
                    .Replace(".ogg", "").Replace(".wav", "").Replace(".png", "").Replace('\\', '/');
                var entry = settings.FindAssetEntry(guid);
                if (entry != null)
                {
                    // 已入库但地址漂移(资源重命名后 GUID 不变,旧地址残留 → 运行时加载失败):
                    // 校正为新文件名对应的地址,与 FxPool 等调用方契约保持一致
                    if (entry.address != expected)
                    {
                        entry.address = expected;
                        EditorUtility.SetDirty(settings);
                        Debug.Log($"[Phase6] 地址校正: {assetPath} → {expected}");
                    }
                    continue;
                }
                entry = settings.CreateOrMoveEntry(guid, artGroup, false);
                if (entry == null) continue;
                entry.address = expected;
                entry.labels.Add("Art");
                added++;
                Debug.Log("[Phase6] 新注册: " + assetPath + " → " + entry.address);
            }
        }

        // 清理已失效条目(源文件被删除的资源从分组移除,防残留地址误导)
        var stale = artGroup.entries
            .Where(e => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(e.guid)))
            .ToList();
        foreach (var e in stale)
        {
            artGroup.RemoveAssetEntry(e);
            Debug.Log("[Phase6] 清理失效条目: " + e.address);
        }
        if (added > 0) EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6] Art 资源注册完成,新增 " + added + " 个 → " + GroupArtAudio + " (共 " + artGroup.entries.Count + " entries)");
    }

    /// <summary>
    /// 迁移 Sudoku 模块独有资源到 Modules/Sudoku(2026-08-30 资源归属分离):
    /// 物理移动(MoveAsset 保 GUID,场景引用与 Addressables 条目不断),幂等:目标已存在跳过。
    /// 公共资源(框架 UI/字体/通用美术音频)留在 UI_Local/Art_Audio 不动。
    /// </summary>
    [MenuItem("Box/Phase6/5. Migrate Sudoku Module Assets")]
    public static void MigrateSudokuModuleAssets()
    {
        foreach (var t in SudokuMigrations)
        {
            if (File.Exists(t.Dst)) continue; // 已迁移
            if (!File.Exists(t.Src))
            {
                Debug.LogWarning("[Phase6] 缺失源文件(可能已迁移): " + t.Src);
                continue;
            }
            EnsureFolder(Path.GetDirectoryName(t.Dst));
            var err = AssetDatabase.MoveAsset(t.Src, t.Dst);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError("[Phase6] 移动失败 " + t.Src + " → " + t.Dst + " : " + err);
                continue;
            }
            Debug.Log("[Phase6] 已移动: " + t.Src + " → " + t.Dst);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6] Sudoku 模块资源迁移完成(保 GUID,引用不断)");
    }

    /// <summary>
    /// 注册 Modules/Sudoku 下全部资源到 Module_Sudoku 分组(幂等):
    /// 地址约定 = "Sudoku/" + 相对 Modules/Sudoku 的路径去扩展名(如 Prefabs/GameplayView → "Sudoku/Prefabs/GameplayView")。
    /// 已注册条目(迁移前在 UI_Local/Art_Audio)自动换组到 Module_Sudoku 并校正地址。
    /// </summary>
    [MenuItem("Box/Phase6/6. Register Sudoku Module Assets")]
    public static void RegisterModuleAssets()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EnsureSetup();
            settings = AddressableAssetSettingsDefaultObject.Settings;
        }
        // settings 已存在时 EnsureSetup 不会重跑,这里显式补建缺失组(幂等,含 Module_Sudoku)
        EnsureGroups(settings);
        var moduleGroup = settings.FindGroup(GroupModuleSudoku);
        if (moduleGroup == null)
        {
            Debug.LogError("[Phase6] 分组 " + GroupModuleSudoku + " 不存在,请先执行 EnsureSetup");
            return;
        }

        const string moduleRoot = "Assets/Modules/Sudoku";
        if (!AssetDatabase.IsValidFolder(moduleRoot))
        {
            Debug.LogError("[Phase6] 模块目录不存在: " + moduleRoot + "(先执行迁移)");
            return;
        }

        int added = 0;
        foreach (var type in new[] { "t:Prefab", "t:Texture2D", "t:AudioClip" })
        {
            foreach (var guid in AssetDatabase.FindAssets(type, new[] { moduleRoot }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.StartsWith(moduleRoot + "/")) continue;
                // 期望地址:Sudoku/ + 相对路径去扩展名
                var expected = "Sudoku/" + assetPath.Substring(moduleRoot.Length + 1)
                    .Replace(".prefab", "").Replace(".png", "").Replace(".ogg", "").Replace(".wav", "")
                    .Replace('\\', '/');
                var entry = settings.FindAssetEntry(guid);
                if (entry != null)
                {
                    // 已入库:地址校正 + 换组(迁移前在公共组的条目移入模块组)
                    if (entry.address != expected)
                    {
                        entry.address = expected;
                        EditorUtility.SetDirty(settings);
                        Debug.Log("[Phase6] 地址校正: " + assetPath + " → " + expected);
                    }
                    if (entry.parentGroup != moduleGroup)
                    {
                        settings.MoveEntry(entry, moduleGroup, false);
                        EditorUtility.SetDirty(settings);
                        Debug.Log("[Phase6] 换组: " + expected + " → " + GroupModuleSudoku);
                    }
                    continue;
                }
                entry = settings.CreateOrMoveEntry(guid, moduleGroup, false);
                if (entry == null) continue;
                entry.address = expected;
                entry.labels.Add("Sudoku");
                added++;
                Debug.Log("[Phase6] 新注册: " + assetPath + " → " + expected);
            }
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase6] Module_Sudoku 注册完成,新增 " + added + " 个(组内共 " + moduleGroup.entries.Count + " entries)");
    }

    /// <summary>确保四个标准分组存在(幂等,settings 已存在时也可补齐新组)。</summary>
    static void EnsureGroups(AddressableAssetSettings settings)
    {
        EnsureGroup(settings, GroupCore, false);
        EnsureGroup(settings, GroupUiLocal, false);
        EnsureGroup(settings, GroupArtAudio, false);
        EnsureGroup(settings, GroupModuleSudoku, false); // 玩法模块组(2026-08-30 资源归属分离)
    }

    static void EnsureGroup(AddressableAssetSettings settings, string name, bool setAsDefault)
    {
        if (settings.FindGroup(name) != null) return;
        var schemas = new List<AddressableAssetGroupSchema>(settings.DefaultGroup.Schemas);
        var group = settings.CreateGroup(name, setAsDefault, false, false, schemas);
        if (group == null)
            Debug.LogError("[Phase6] 分组创建失败: " + name);
        else
            Debug.Log("[Phase6] 分组已创建: " + name);
    }

    static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        path = path.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        if (string.IsNullOrEmpty(parent))
            AssetDatabase.CreateFolder("Assets", name);
        else
            AssetDatabase.CreateFolder(parent, name);
    }
}