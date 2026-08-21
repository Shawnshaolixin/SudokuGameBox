namespace Sudoku.Core
{
    /// <summary>
    /// 解题技巧层级。阶段 A 仅实现显性唯一 / 隐性唯一 / 回溯,
    /// 其余(Pairs、Pointing、X-Wing、Swordfish 等)按 GDD §3.3 属 P1 预留。
    /// </summary>
    public enum Technique
    {
        None = 0,

        /// <summary>显性唯一:某空格的合法候选数只有一个。</summary>
        NakedSingle = 1,

        /// <summary>隐性唯一:某数字在行/列/宫内只能落在唯一空格。</summary>
        HiddenSingle = 2,

        /// <summary>逻辑不可解,需回溯/猜测(阶段 A 视为最难)。</summary>
        Backtracking = 99
    }
}
