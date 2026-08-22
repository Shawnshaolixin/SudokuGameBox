using Sudoku.Core;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 场景间传参(静态):主菜单选难度/每日挑战 → Gameplay 场景读取。
    /// v1.0 单机单局制,静态足够;v1.1 热更/存档落地后由 Phase 5 存档替代入口。
    /// </summary>
    public static class GameContext
    {
        public static Difficulty Difficulty { get; private set; } = Difficulty.Easy;
        public static int DailySeed { get; private set; }
        public static bool IsDaily { get; private set; }

        /// <summary>普通对局:主菜单难度选择后设置。</summary>
        public static void SetNormalGame(Difficulty difficulty)
        {
            Difficulty = difficulty;
            IsDaily = false;
        }

        /// <summary>每日挑战:日期种子(每日一题,同 seed 确定性)。</summary>
        public static void SetDaily(int seed)
        {
            DailySeed = seed;
            IsDaily = true;
        }

        public static void Reset() => IsDaily = false;
    }
}
