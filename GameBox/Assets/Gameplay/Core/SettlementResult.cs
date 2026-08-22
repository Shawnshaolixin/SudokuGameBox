using Sudoku.Core;

namespace Box.Gameplay
{
    /// <summary>结算后用户去向(弹窗 Action 回写,由 GameplayView 消费)。</summary>
    public enum SettlementAction
    {
        /// <summary>弹窗被返回键关闭/未选,不导航。</summary>
        None,
        /// <summary>再来一局(同难度)。</summary>
        Next,
        /// <summary>回主菜单。</summary>
        Home,
    }

    /// <summary>一局结束的结算数据(GameplayView 组装,结算弹窗展示)。</summary>
    public sealed class SettlementResult
    {
        public int StarRating;
        public int MistakeCount;
        public int HintsUsed;
        public float TimeSec;
        public Difficulty Difficulty;
        public bool IsDaily;
        public int BestSec;   // 每日挑战最佳秒数(仅 IsDaily 有效,0=首次)

        /// <summary>弹窗关闭后由弹窗回写;None=返回键关闭,不导航。</summary>
        public SettlementAction Action = SettlementAction.None;
    }
}
