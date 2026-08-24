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

        EnsureGroup(settings, GroupCore, false);
        EnsureGroup(settings, GroupUiLocal, false);
        EnsureGroup(settings, GroupArtAudio, false);
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