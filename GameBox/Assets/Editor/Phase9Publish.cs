using System.IO;
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
        Debug.Log($"[Phase9Publish] 全量远程构建开始(BuildTarget={EditorUserBuildSettings.activeBuildTarget})...");
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
