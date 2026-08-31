using System.Diagnostics.CodeAnalysis;

namespace Box.ModuleBridge
{
    /// <summary>
    /// 模式条件桥锚点(Phase 9 9-2,见 10 文档 §16.3)。
    ///
    /// 所在程序集 defineConstraints=["!HYBRIDCLR_UNITY"]:
    ///  v1.1 构建(PrepareV11 注入 HYBRIDCLR_UNITY 符号)时本程序集整体不编译,
    ///  消除对主包中已被 FilterHotFixAssemblies 剔除的热更程序集的悬垂引用;
    ///  v1.0 构建(无符号)时本类编译,对 SudokuModule 持强引用,
    ///  防 IL2CPP 链接器把热更程序集视为"无引用"而整包裁剪。
    /// </summary>
    public static class ModuleBridgeAnchor
    {
        // 仅作强引用锚点:让链接器认为 SudokuModule 被 AOT 侧引用,不可裁剪
        [SuppressMessage("CodeQuality", "IDE0051", Justification = "仅供链接器保留引用")]
        private static readonly System.Type AnchorType = typeof(Box.HotUpdate.Sudoku.SudokuModule);
    }
}
