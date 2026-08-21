using System.IO;
using System.Linq;
using UnityEditor;
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

    static void BuildAndroid(bool aab)
    {
        EditorUserBuildSettings.buildAppBundle = aab;

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
