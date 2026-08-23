using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 构建双入口(10 文档 Phase 1-3 / 11 文档 §4.9 checklist 第 9 步):
///   BuildAndroidApk —— 本地测试
///   BuildAndroidAab —— 上架(buildAppBundle = true)
/// CLI 无头调用:
///   unity build GameBox --target Android --execute-method BuildScript.BuildAndroidAab
/// </summary>
public static class BuildScript
{
    const string OutputDir = "Build/Android";

    [MenuItem("Box/Build/Android APK (local)")]
    public static void BuildAndroidApk() => BuildAndroid(aab: false);

    [MenuItem("Box/Build/Android AAB (release)")]
    public static void BuildAndroidAab() => BuildAndroid(aab: true);

    /// <summary>
    /// 一次会话产出 APK + AAB 双产物(本地测试 + 上架,Phase 6.5 CI-3 用):
    /// Addressables 只构建一次,两次 BuildPlayer 复用 —— 比两次 CLI 调用省一次资源管线构建。
    /// CLI 无头调用:
    ///   unity build GameBox --target Android --execute-method BuildScript.BuildAndroidApkAndAab
    /// </summary>
    [MenuItem("Box/Build/Android APK + AAB (test + release)")]
    public static void BuildAndroidApkAndAab()
    {
        // Phase 6:Addressables 资源先构建(分组 Core/UI_Local/Art_Audio;UI 经 Addressables 加载)
        // m_BuildAddressablesWithPlayerBuild=0 → 需显式构建,否则产物内缺 bundle 运行时加载失败
        try
        {
            AddressableAssetSettings.BuildPlayerContent();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[BuildScript] Addressables BuildPlayerContent failed: " + e.Message);
            EditorApplication.Exit(1);
            return;
        }

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[BuildScript] No enabled scenes in Build Settings.");
            EditorApplication.Exit(1);
            return;
        }

        Directory.CreateDirectory(OutputDir);
        BuildAndroidInternal(scenes, aab: false); // 本地测试 APK
        BuildAndroidInternal(scenes, aab: true);  // 上架 AAB
    }

    static void BuildAndroid(bool aab)
    {
        // Phase 6:Addressables 资源先构建(分组 Core/UI_Local/Art_Audio;UI 经 Addressables 加载)
        // m_BuildAddressablesWithPlayerBuild=0 → 需显式构建,否则 AAB 内缺 bundle 运行时加载失败
        try
        {
            AddressableAssetSettings.BuildPlayerContent();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[BuildScript] Addressables BuildPlayerContent failed: " + e.Message);
            EditorApplication.Exit(1);
            return;
        }

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[BuildScript] No enabled scenes in Build Settings.");
            EditorApplication.Exit(1);
            return;
        }

        Directory.CreateDirectory(OutputDir);
        BuildAndroidInternal(scenes, aab);
    }

    static void BuildAndroidInternal(string[] scenes, bool aab)
    {
        EditorUserBuildSettings.buildAppBundle = aab;

        var ext = aab ? ".aab" : ".apk";
        var output = Path.Combine(OutputDir, "GameBox" + ext);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            locationPathName = output,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildScript] SUCCESS -> {output} ({summary.totalSize / (1024f * 1024f):F1} MB)");
        }
        else
        {
            Debug.LogError($"[BuildScript] FAILED: {summary.result} ({summary.totalErrors} errors)");
            EditorApplication.Exit(1);
        }
    }
}
