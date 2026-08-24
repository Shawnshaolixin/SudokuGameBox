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

        // Phase 7 7-3:上架产物(AAB)应用上传签名;密码经环境变量注入(CI 凭据模式),
        // 构建完成立即清除 —— 防止密码写入 ProjectSettings.asset 进 git(15 号文档 §4.3 步骤 4)
        bool signed = false;
        if (aab)
        {
            signed = ApplyReleaseSigningIfAvailable();
        }

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

        try
        {
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
        finally
        {
            if (signed) ClearReleaseSigning(); // 无论成败都清除密码字段
        }
    }

    /// <summary>
    /// 应用上传签名(upload.keystore,Play App Signing 的 upload key):
    /// keystore 存在于 Build/keystore 且环境变量 BOX_KEYSTORE_PASS 提供密码时生效;
    /// 密码不进代码/不进 git,由本机环境或 Jenkins 凭据注入。
    /// </summary>
    static bool ApplyReleaseSigningIfAvailable()
    {
        var keystore = Path.GetFullPath("Build/keystore/upload.keystore");
        if (!File.Exists(keystore))
        {
            Debug.Log("[BuildScript] 未找到上传 keystore,本次 AAB 使用 debug 签名(仅本地测试,不可上架)");
            return false;
        }
        var storePass = System.Environment.GetEnvironmentVariable("BOX_KEYSTORE_PASS");
        if (string.IsNullOrEmpty(storePass))
        {
            Debug.LogWarning("[BuildScript] 存在 upload.keystore 但未设置环境变量 BOX_KEYSTORE_PASS,跳过签名(AAB 不可上架)");
            return false;
        }

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystore;
        PlayerSettings.Android.keystorePass = storePass;
        PlayerSettings.Android.keyaliasName = "sudoku";
        var keyPass = System.Environment.GetEnvironmentVariable("BOX_KEY_PASS");
        PlayerSettings.Android.keyaliasPass = string.IsNullOrEmpty(keyPass) ? storePass : keyPass;
        Debug.Log("[BuildScript] 已应用上传签名 upload.keystore(alias: sudoku)");
        return true;
    }

    /// <summary>构建后清除签名密码字段(防泄露;签名时 apply 的密码仅本会话有效)。</summary>
    static void ClearReleaseSigning()
    {
        PlayerSettings.Android.useCustomKeystore = false;
        PlayerSettings.Android.keystorePass = string.Empty;
        PlayerSettings.Android.keyaliasPass = string.Empty;
        Debug.Log("[BuildScript] 已清除签名密码字段(useCustomKeystore=false,防入库)");
    }
}
