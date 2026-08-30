using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;          // NamedBuildTarget(v1.1 符号操作)
using UnityEditor.Build.Reporting;
using UnityEngine;
using HybridCLR.Editor;           // SettingsUtil(Phase 9:v1.0 兜底 / v1.1 状态检查)
using HybridCLR.Editor.Commands; // PrebuildCommand.GenerateAll(Phase 9:9-1)

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

    // ---------- Phase 9 v1.1(HybridCLR 热更)常量 ----------
    /// <summary>模式条件桥符号:定义时 AOT 侧 Box.ModuleBridge(defineConstraints 排除)不编译,热更程序集被 Filter 过滤。</summary>
    const string V11Symbol = "HYBRIDCLR_UNITY";
    /// <summary>本机 NDK r27c 路径(Jenkins 用环境变量 ANDROID_NDK_ROOT 注入,本地 CLI 用 EditorPrefs 双保险,17 文档)。</summary>
    const string NdkR27cPath = "D:/Projects/AI/AndroidNDK/android-ndk-r27c";

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
        // Phase 9 9-1 v1.0 幂等兜底:HybridCLR enable 必须为 false(D-2 开关,见 Phase9HybridCLRSetup)。
        // v1.1 构建异常中断可能残留 enable=true,会让 FilterHotFixAssemblies 静默过滤热更程序集
        // (产物缺玩法代码) —— 每次 v1.0 构建显式复位 + 残留符号硬失败,显式失败优于静默污染。
        SettingsUtil.Enable = false;
        if (HasV11Symbol())
        {
            Debug.LogError("[BuildScript] 检测到残留 HYBRIDCLR_UNITY 符号(上次 v1.1 构建未正常收尾),"
                + "请先跑 BuildScript.BuildV11 或手动移除该符号后重试");
            EditorApplication.Exit(1);
            return;
        }
        BuildAndroidInternalCore(scenes, aab);
    }

    /// <summary>构建核心(签名纪律 + BuildPlayer + 产物命名);v1.0 与 v1.1(BuildV11)共用。</summary>
    static void BuildAndroidInternalCore(string[] scenes, bool aab)
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
        // 产物文件名带 构建类型/版本号/日期 标签(2026-08-30 用户要求):每次构建不覆盖旧产物,
        // 便于回滚与存档;例 Rovilo-release-v11-20260830-1510.aab。应用显示名不受文件名影响。
        var label = aab ? "release" : "debug";
        var vc = PlayerSettings.Android.bundleVersionCode;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        var output = Path.Combine(OutputDir, $"Rovilo-{label}-v{vc}-{stamp}{ext}");

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

    // ==================== Phase 9 v1.1 热更构建(HybridCLR,双阶段 CLI) ====================
    // 阶段 A PrepareV11:切 Android + NDK 偏好 + enable=true + 名单 + HYBRIDCLR_UNITY 符号(保存退出);
    // 阶段 B BuildV11:GenerateAll 六步 → Addressables → 构建(签名纪律继承 Core) → finally 恢复 v1.0 语义。
    // 拆两阶段的原因:GenerateAll 的 StripAOTDlls 需要 已安装运行时 + Android target,
    // 且 MethodBridge 的 DEVELOPMENT flag 须与最终构建一致(GenerateAll 与构建同一会话内完成)。
    // CLI:
    //   unity -batchmode -quit -executeMethod BuildScript.PrepareV11
    //   unity -batchmode -quit -executeMethod BuildScript.BuildV11   (需 BOX_KEYSTORE_PASS)

    [MenuItem("Box/Build/Android AAB v1.1 (hybridclr, CLI 双阶段)")]
    public static void BuildAndroidAabV11()
    {
        if (!Application.isBatchMode)
        {
            // GUI 下 v1.1 是双阶段有状态流程,提示走 CLI(与 CI 行为一致,避免 GUI 状态残留)
            Debug.LogWarning("[BuildScript] v1.1 构建请用 CLI 双阶段:PrepareV11 → BuildV11(见方法注释)");
            return;
        }
        BuildV11(aab: true);
    }

    /// <summary>
    /// v1.1 中间态验证专用:APK(免签名,libil2cpp.so 与 AAB 一致,9-1 符号验证用)。
    /// CLI:PrepareV11 → BuildV11Apk
    /// </summary>
    [MenuItem("Box/Build/Android APK v1.1 (hybridclr, 中间态验证)")]
    public static void BuildV11Apk()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[BuildScript] v1.1 构建请用 CLI 双阶段:PrepareV11 → BuildV11Apk");
            return;
        }
        BuildV11(aab: false);
    }

    /// <summary>v1.1 阶段 A:环境就绪(D-2 开关 enable=true + 名单 + 模式符号 + NDK 偏好)。</summary>
    public static void PrepareV11()
    {
        try
        {
            // GenerateAll 与 StripAOTDlls 依赖 activeBuildTarget=Android(从当前 target 解析产物目录)
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Debug.LogError("[BuildScript] PrepareV11: 切换到 Android target 失败");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            // NDK r27c 显式偏好(本地 CLI 双保险;Jenkins 走 ANDROID_NDK_ROOT 环境变量,17 文档)
            // Unity 6000 的 NDK 偏好键带版本后缀(AndroidNdkRootR27C),两个键都写防解析差异
            EditorPrefs.SetString("AndroidNdkRoot", NdkR27cPath);
            EditorPrefs.SetString("AndroidNdkRootR27C", NdkR27cPath);
            EditorPrefs.SetBool("NdkUseEmbedded", false);

            // D-2 开关:enable=true + 热更名单(Phase9HybridCLRSetup.SetV11)
            Phase9HybridCLRSetup.SetV11();
            AddV11Symbol(); // 模式条件桥符号(9-2 起 Box.ModuleBridge 据此排除编译)

            Debug.Log($"[BuildScript] PrepareV11 done: enable=true, NDK={NdkR27cPath}, symbol={V11Symbol}");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[BuildScript] PrepareV11 EXCEPTION: " + e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>v1.1 阶段 B 入口(AAB,上架形态;需 BOX_KEYSTORE_PASS 注入签名)。</summary>
    public static void BuildV11() => BuildV11(aab: true);

    /// <summary>
    /// v1.1 阶段 B 核心:GenerateAll → Addressables → 构建;finally 恢复 v1.0 语义。
    /// aab=true 走上传签名纪律(拒绝 debug 签名包);aab=false 为中间态验证(免签名)。
    /// </summary>
    static void BuildV11(bool aab)
    {
        // 防裸跑:未经过 PrepareV11 直接构建 = enable=false 或符号缺失,GenerateAll 结果不可预期
        if (!SettingsUtil.Enable || !HasV11Symbol())
        {
            Debug.LogError("[BuildScript] BuildV11: 请先执行 PrepareV11(enable=true + HYBRIDCLR_UNITY 缺失)");
            EditorApplication.Exit(1);
            return;
        }
        try
        {
            // 1) GenerateAll 六步:CompileDll → Il2CppDef → LinkXml → StripAOTDlls → MethodBridge → AOTGenericReferences
            PrebuildCommand.GenerateAll();

            // StripAOTDlls 内部临时置 exportAsGoogleAndroidProject=true(导出 Android 工程跑 il2cpp),
            // 显式复位防残留(残留会把后续构建从 AAB 变成工程导出)
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

            // 2) Addressables 资源(先代码后资源,代码失败早暴露)
            try
            {
                AddressableAssetSettings.BuildPlayerContent();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[BuildScript] BuildV11 Addressables BuildPlayerContent failed: " + e.Message);
                EditorApplication.Exit(1);
                return;
            }

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[BuildScript] BuildV11: Build Settings 无启用的场景");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(OutputDir);
            // 3) 复用 v1.0 构建核心(aab=true 时含上传签名纪律)
            BuildAndroidInternalCore(scenes, aab);
        }
        finally
        {
            // 4) 恢复 v1.0 语义:enable=false + 移除符号 —— 异常中断也不残留污染 v1.0 构建链
            SettingsUtil.Enable = false;
            RemoveV11Symbol();
            Debug.Log("[BuildScript] BuildV11 收尾:enable=false + 移除 HYBRIDCLR_UNITY(已恢复 v1.0 语义)");
        }
    }

    /// <summary>读 Android 平台脚本符号(Unity 6000 的 GetScriptingDefineSymbols 返回分号分隔 string)。</summary>
    static string GetAndroidDefines() => PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);

    /// <summary>Android 平台符号是否含 HYBRIDCLR_UNITY(按 ; 分词,防子串误判如 XX_HYBRIDCLR_UNITY_XX)。</summary>
    static bool HasV11Symbol() =>
        GetAndroidDefines().Split(';').Contains(V11Symbol);

    /// <summary>给 Android 平台加 HYBRIDCLR_UNITY 符号(v1.1 模式条件桥开关)。</summary>
    static void AddV11Symbol()
    {
        var defines = GetAndroidDefines().Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (!defines.Contains(V11Symbol)) defines.Add(V11Symbol);
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, string.Join(";", defines));
    }

    /// <summary>从 Android 平台移除 HYBRIDCLR_UNITY 符号(v1.0 语义恢复)。</summary>
    static void RemoveV11Symbol()
    {
        var defines = GetAndroidDefines().Split(';').Where(s => !string.IsNullOrEmpty(s) && s != V11Symbol);
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, string.Join(";", defines));
    }
}
