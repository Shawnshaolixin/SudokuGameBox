using System;
using System.IO;
using UnityEditor;
using HybridCLR.Editor;              // SettingsUtil
using HybridCLR.Editor.Installer;    // InstallerController / BashUtil

/// <summary>
/// Phase 9 9-1:HybridCLR C++ 运行时无头安装入口(10 文档 §16 9-1)。
/// 复刻 Gate1 最终配方(Gate1Install + Gate1Reinstall 合并版),在正式工程内可重跑、可验障。
///
/// 分支要点(gitee 实测,2026-08-21 Gate1):
///  - hybridclr 核心用分支 `6000.3.x`——官方清单写的是 GitHub 分支名 v6000.3.x-8.13.0,
///    gitee 不存在;gitee 的 tag v8.13.0 能装上但 C++ 与 6000.3.20f1 的 il2cpp API 不兼容
///    (报 no member named 'GetFieldDefinitionFromTypeDefAndFieldIndex'),必须用 6000.3.x 分支;
///  - il2cpp_plus 用 tag `v6000.3.x-8.14.0`(gitee 存在,无此问题)。
/// 官方 InstallerController.Install 是实例方法,-executeMethod 只接受静态方法 → 包一层静态入口。
/// 用法:unity -batchmode -projectPath GameBox -executeMethod Phase9HybridCLRInstall.Install
/// </summary>
public static class Phase9HybridCLRInstall
{
    private const string HybridclrUrl = "https://gitee.com/focus-creative-games/hybridclr.git";
    private const string HybridclrBranch = "6000.3.x";
    private const string Il2CppPlusUrl = "https://gitee.com/focus-creative-games/il2cpp_plus.git";
    private const string Il2CppPlusTag = "v6000.3.x-8.14.0";

    public static void Install()
    {
        try
        {
            var ctl = new InstallerController();
            string workDir = SettingsUtil.HybridCLRDataDir;
            Directory.CreateDirectory(workDir);

            // 1) clone hybridclr 核心源码(gitee 6000.3.x 分支,与 6000.3.20f1 il2cpp API 匹配)
            string hybridclrRepoDir = $"{workDir}/hybridclr_repo";
            BashUtil.RemoveDir(hybridclrRepoDir);
            int rc1 = BashUtil.RunCommand(workDir, "git",
                new[] { "clone", "-b", HybridclrBranch, "--depth", "1", HybridclrUrl, hybridclrRepoDir });
            if (rc1 != 0 || !Directory.Exists(hybridclrRepoDir))
            {
                throw new Exception($"clone hybridclr fail. url: {HybridclrUrl} branch: {HybridclrBranch} rc={rc1}");
            }

            // 2) clone il2cpp_plus(补丁版 il2cpp,tag v6000.3.x-8.14.0)
            string il2cppPlusRepoDir = $"{workDir}/il2cpp_plus_repo";
            BashUtil.RemoveDir(il2cppPlusRepoDir);
            int rc2 = BashUtil.RunCommand(workDir, "git",
                new[] { "clone", "-b", Il2CppPlusTag, "--depth", "1", Il2CppPlusUrl, il2cppPlusRepoDir });
            if (rc2 != 0 || !Directory.Exists(il2cppPlusRepoDir))
            {
                throw new Exception($"clone il2cpp_plus fail. url: {Il2CppPlusUrl} tag: {Il2CppPlusTag} rc={rc2}");
            }

            // 3) 把 hybridclr 核心移入 il2cpp_plus/libil2cpp/hybridclr(InstallFromLocal 的源目录结构要求)
            string hybridclrSrc = $"{hybridclrRepoDir}/hybridclr";
            string dest = $"{il2cppPlusRepoDir}/libil2cpp/hybridclr";
            BashUtil.RemoveDir(dest); // 容忍重入场景
            Directory.Move(hybridclrSrc, dest);

            // 3.5) 预建 generated 目录(InstallFromLocal 的 WriteLocalVersion 需要;GenerateAll 会填内容)
            Directory.CreateDirectory($"{dest}/generated");
            UnityEngine.Debug.Log($"[Phase9Install] hybridclr source -> {dest}");

            // 4) 安装:复制编辑器 il2cpp → HybridCLRData/LocalIl2CppData-WindowsEditor,替换 libil2cpp
            ctl.InstallFromLocal($"{il2cppPlusRepoDir}/libil2cpp");

            // 5) 验证并退出(0=成功 / 1=失败)
            if (ctl.HasInstalledHybridCLR())
            {
                UnityEngine.Debug.Log("[Phase9Install] HybridCLR install OK");
                EditorApplication.Exit(0);
            }
            else
            {
                UnityEngine.Debug.LogError("[Phase9Install] HybridCLR install FAILED (HasInstalledHybridCLR == false)");
                EditorApplication.Exit(1);
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("[Phase9Install] EXCEPTION: " + e);
            EditorApplication.Exit(1);
        }
    }
}
