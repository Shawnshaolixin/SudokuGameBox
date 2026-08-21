using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// 一次性 Player Settings 配置(11 文档 §4.9 checklist 第 8 步 / 10 文档 Phase 1-2)。
/// 由 CLI 无头调用:unity run GameBox -batchmode -quit -executeMethod ProjectSetup.ApplyAndroidSettings
/// </summary>
public static class ProjectSetup
{
    [MenuItem("Box/Setup/Apply Android Settings")]
    public static void ApplyAndroidSettings()
    {
        // IL2CPP(11 文档 §9.2:上架必需;旧基线 Mono)
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

        // ARM64(11 文档 §1.4:仅 ARMv7 是 Play 硬阻塞;模板默认已是 ARM64,显式写死防回归)
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // minSdk 25(D-13 条款触发:Unity 6000.3 最低支持 API = 25,设 24 已 obsolete
        // 且构建强制回写 25;以下一次 release 会变 error 为由,直接用 25)
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;

        // targetSdk 35(跟随 Play 当前要求;Unity 6 下拉应为可选 35/36)
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;

        // managedStrippingLevel: Medium(11 文档 P-1 一并设定;HybridCLR 阶段再按 link.xml 调整)
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);

        // .NET Standard 2.1(HybridCLR 前置项;Unity 6 枚举名为 NET_Standard)
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);

        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectSetup] applied: IL2CPP / ARM64 / minSdk24 / targetSdk35 / Stripping-Medium / NET_Standard_2_1");
    }
}
