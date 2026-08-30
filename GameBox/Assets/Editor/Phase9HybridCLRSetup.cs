using System;
using UnityEditor;
using HybridCLR.Editor.Settings; // HybridCLRSettings

/// <summary>
/// Phase 9 9-1:HybridCLR 设置资产管理(10 文档 §16 9-1)。
///
/// D-2 双模式构建开关 = ProjectSettings/HybridCLRSettings.asset 的 enable 字段:
///  v1.0(纯 AOT 自包含) = enable=false —— FilterHotFixAssemblies 不介入、原版 il2cpp;
///  v1.1(热更主线)     = enable=true  —— 过滤热更程序集 + CheckSettings 把
///                                       UNITY_IL2CPP_PATH 指向 hybridclr 运行时。
/// 包默认 enable=true(FilterHotFixAssemblies 会自动过滤名单内程序集),
/// 首次创建资产必须显式置 false 并入库 —— 漏置会把 v1.0 构建链带偏(热更程序集消失)。
/// </summary>
public static class Phase9HybridCLRSetup
{
    /// <summary>
    /// 热更程序集名单(无 .dll 后缀,与热更 asmdef 名字一一对应)。
    /// 9-1 阶段先只有 Sudoku;9-2 建 Box.HotUpdate.Core 后补入首项。
    /// </summary>
    public static readonly string[] HotUpdateAssemblies = { "Box.HotUpdate.Sudoku" };

    /// <summary>v1.0 语义:enable=false + 名单(Filter 不介入,热更程序集照常编译进主包)。</summary>
    public static void SetupV10()
    {
        var s = HybridCLRSettings.Instance;
        s.enable = false;
        s.hotUpdateAssemblies = HotUpdateAssemblies;
        HybridCLRSettings.Save();
        UnityEngine.Debug.Log($"[Phase9Setup] HybridCLRSettings.asset: enable=false, " +
                              $"hotUpdateAssemblies=[{string.Join(", ", HotUpdateAssemblies)}]");
    }

    /// <summary>v1.1 语义:enable=true + 名单(GenerateAll 与热更构建的前置条件)。</summary>
    public static void SetV11()
    {
        var s = HybridCLRSettings.Instance;
        s.enable = true;
        s.hotUpdateAssemblies = HotUpdateAssemblies;
        HybridCLRSettings.Save();
        UnityEngine.Debug.Log($"[Phase9Setup] enable=true, " +
                              $"hotUpdateAssemblies=[{string.Join(", ", HotUpdateAssemblies)}]");
    }
}
