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
    public static void BuildAndroidAab()
    {
        // 编辑器 GUI:弹窗确认版本号 + 密码后构建(用户自己点,2026-08-29);
        // batchmode(CLI/CI):环境变量已由调用方注入,直接走原逻辑不弹窗
        if (!Application.isBatchMode)
        {
            BuildReleaseDialog.ShowDialog();
            return;
        }
        BuildAndroidAabCore();
    }

    /// <summary>实际构建核心(弹窗与 CLI 共用;弹窗不直接调菜单入口防递归弹窗)。</summary>
    internal static void BuildAndroidAabCore() => BuildAndroid(aab: true);

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
        EnsureJdkConfigured(); // 无头构建兜底 JDK 偏好(见方法注释)

        // Phase 7 7-3:上架产物(AAB)应用上传签名;密码经环境变量注入(CI 凭据模式),
        // 构建完成立即清除 —— 防止密码写入 ProjectSettings.asset 进 git(15 号文档 §4.3 步骤 4)
        // AAB 必须带上传签名:签名不可用直接中止构建,拒绝静默产出 debug 签名包
        // (2026-08-26 曾静默回退 debug 签名被 Play Console 拒收)
        bool signed = false;
        if (aab)
        {
            signed = ApplyReleaseSigningIfAvailable();
            if (!signed)
            {
                Debug.LogError("[BuildScript] AAB 未应用上传签名,中止构建(拒绝产出 debug 签名包)");
                EditorApplication.Exit(1);
                return;
            }
        }

        var ext = aab ? ".aab" : ".apk";
        // 产物文件名与应用名保持一致(Rovilo),避免与工程目录 GameBox 混淆;仅影响文件名,不影响应用显示名
        var output = Path.Combine(OutputDir, "Rovilo" + ext);

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
        // keystore 固定在仓库根 Build/keystore(与 GameBox 工程平级,.gitignore 忽略)。
        // 不能用相对 CWD 的路径:CLI 启动的 CWD 因调用方而异(编辑器菜单=工程目录/CI=工作区),
        // 解析错会静默回退 debug 签名(2026-08-26 Play 拒收事故根因)。
        var projectRoot = new DirectoryInfo(Application.dataPath).Parent!; // .../GameBox
        var repoRoot = projectRoot.Parent!;                                // 仓库根
        var keystore = Path.Combine(repoRoot.FullName, "Build", "keystore", "upload.keystore");
        if (!File.Exists(keystore))
        {
            Debug.LogError($"[BuildScript] 未找到上传 keystore:{keystore}");
            return false;
        }
        var storePass = System.Environment.GetEnvironmentVariable("BOX_KEYSTORE_PASS");
        if (string.IsNullOrEmpty(storePass))
        {
            Debug.LogError("[BuildScript] 存在 upload.keystore 但未设置环境变量 BOX_KEYSTORE_PASS");
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

    /// <summary>
    /// 无头构建兜底:batchmode 下 JDK 偏好为空时 Android 构建直接报 "JDK not found"
    /// (GUI 编辑器会自动兜底内置 OpenJDK,batchmode 不会,2026-08-26 CLI 构建实测;
    /// 偏好键 JdkPath 取自 UnityEditor.Android.Extensions.dll 反编译确认)。
    /// 与 NDK 走 ANDROID_NDK_ROOT 同理(见 Jenkinsfile),构建前把内置 OpenJDK 写入 EditorPrefs。
    /// </summary>
    static void EnsureJdkConfigured()
    {
        if (!string.IsNullOrEmpty(EditorPrefs.GetString("JdkPath"))) return;
        var editorRoot = Path.GetDirectoryName(EditorApplication.applicationPath);
        var bundledJdk = Path.Combine(editorRoot, "Data", "PlaybackEngines", "AndroidPlayer", "OpenJDK");
        if (Directory.Exists(bundledJdk))
        {
            EditorPrefs.SetString("JdkPath", bundledJdk);
            Debug.Log($"[BuildScript] JDK 偏好为空,已写入内置 OpenJDK:{bundledJdk}");
        }
        else
        {
            Debug.LogError($"[BuildScript] 未找到内置 OpenJDK:{bundledJdk},请在 Preferences → External Tools 手动配置");
        }
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
