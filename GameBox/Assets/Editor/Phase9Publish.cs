using System.IO;
using HybridCLR.Editor; // SettingsUtil.Enable:v1.0/v1.1 语义判定(缓存目录劫持根治,见 BuildAll 注释)
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Phase 9 9-4 远程内容发布构建(10 文档 §16.5):
///  BuildAll          全量远程构建(清 PlayerContent → BuildPlayerContent,产 ServerData + content_state);
///  ContentUpdateBuild 增量 Content Update(基于入库的 content_state.bin,只重建变更组,产出增量 bundle)。
/// 更新流程:改代码 → GenerateAll → GenerateContent → 全量/增量构建 → deploy_remote.ps1 → 真机验证。
/// content_state.bin 必须入库(gitignore 否定规则),丢失则无法做增量更新。
/// </summary>
public static class Phase9Publish
{
    /// <summary>
    /// Content Update 状态文件(全量构建产出;增量构建输入;gitignore 已放行入库)。
    /// 路径含 [BuildTarget] 子目录 —— 本工程默认 ContentStateBuildPath=AddressableAssetsData/[BuildTarget]。
    /// </summary>
    public const string ContentStatePath = "Assets/AddressableAssetsData/Android/addressables_content_state.bin";

    /// <summary>全量远程内容构建:清空旧 PlayerContent 后重建(产物:AddressableAssetsData/ServerData/{BuildTarget}/)。</summary>
    [MenuItem("Box/Phase9/5.1 Publish All (Full Remote Build)")]
    public static void BuildAll()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Phase9Publish] Addressables 未初始化,请先执行 Phase9HybridCLRSetup.EnsureRemoteSetup");
            return;
        }
        // 2026-09-05 真机"点 More Games 无反应"事故根治(缓存 catalog 目录劫持):
        // Addressables 离线分支优先加载 persistentDataPath 缓存 catalog(源码 ContentCatalogProvider.
        // DetermineIdToLoad:remote hash 不可达且缓存存在 → 用缓存,不用包内)——旧安装残留的
        // 远程 catalog 会把本地 bundle 名指向旧内容哈希(ui_local...c86…),新包内文件是另一哈希
        // (…37152ac5) → 全部资源加载失败、UI 静默无响应(force-stop 不清 persistentDataPath 故复现)。
        // v1.0 包无远程内容:启动禁用 catalog 更新 → 缓存永不生效,永远读包内 catalog(自洽);
        // v1.1(enable=true)必须保持 false —— 热更内容更新依赖启动拉取远程 catalog。
        // 构建期按语义写入 settings.json 后恢复资产原值(入库默认 false,不留脏)。
        bool prevCatalogUpdate = settings.DisableCatalogUpdateOnStartup;
        settings.DisableCatalogUpdateOnStartup = !SettingsUtil.Enable;
        try
        {
            Debug.Log($"[Phase9Publish] 全量远程构建开始(BuildTarget={EditorUserBuildSettings.activeBuildTarget}," +
                      $"DisableCatalogUpdateOnStartup={settings.DisableCatalogUpdateOnStartup}," +
                      $"HybridCLR enable={SettingsUtil.Enable})...");
            AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (result.Error == null || result.Error.Length == 0)
            {
                Debug.Log("[Phase9Publish] 全量构建成功:ServerData/" + EditorUserBuildSettings.activeBuildTarget
                          + "(catalog + bundles + content_state.bin)");
            }
            else
            {
                Debug.LogError("[Phase9Publish] 全量构建失败: " + result.Error);
            }
        }
        finally
        {
            settings.DisableCatalogUpdateOnStartup = prevCatalogUpdate;
            UnityEditor.EditorUtility.SetDirty(settings); // 资产恢复入库原值(构建产物已固化,不受影响)
        }
    }

    /// <summary>增量 Content Update 构建:基于入库的 content_state.bin 只重建变更组(增量 bundle,真机只下增量)。</summary>
    [MenuItem("Box/Phase9/5.2 Publish Update (Content Update)")]
    public static void ContentUpdateBuild()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Phase9Publish] Addressables 未初始化");
            return;
        }
        if (!File.Exists(ContentStatePath))
        {
            Debug.LogError("[Phase9Publish] 缺少 content_state.bin,请先执行全量构建(Publish All)");
            return;
        }
        Debug.Log("[Phase9Publish] 增量 Content Update 构建开始...");
        var result = ContentUpdateScript.BuildContentUpdate(settings, ContentStatePath);
        if (result.Error == null || result.Error.Length == 0)
            Debug.Log("[Phase9Publish] 增量构建成功:ServerData/" + EditorUserBuildSettings.activeBuildTarget + "(增量 bundle)");
        else
            Debug.LogError("[Phase9Publish] 增量构建失败: " + result.Error);
    }
}
