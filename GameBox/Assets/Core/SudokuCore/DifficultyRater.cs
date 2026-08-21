using System;

namespace Sudoku.Core
{
    /// <summary>
    /// 难度评分(阶段 A 近似版):
    /// 用 Singles 逻辑求解器尽量推进,推不动则视为需要回溯(猜测)。
    /// 最终难度 = 「所需最高技巧 + 提示数区间」的映射。
    /// 完整技巧阶梯(Naked/Hidden Pair、Pointing、X-Wing 等)属 P1,见 GDD §3.3。
    /// </summary>
    public static class DifficultyRater
    {
        /// <summary>对谜题评分。输入应为一个合法谜题(允许含空格)。</summary>
        public static Difficulty Rate(SudokuBoard puzzle)
        {
            if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));

            var cells = puzzle.ToArray();
            Technique hardest = Technique.None;

            while (true)
            {
                if (IsFull(cells)) break;

                if (LogicSolver.TryFindSingleCore(cells, out int idx, out int val, out Technique t))
                {
                    cells[idx] = val;
                    if (t > hardest) hardest = t;
                }
                else
                {
                    hardest = Technique.Backtracking; // Singles 推不动 => 需要猜测
                    break;
                }
            }

            return MapToDifficulty(hardest, puzzle.CountGivens());
        }

        /// <summary>将「最高技巧 + 提示数」映射为难度档位。</summary>
        public static Difficulty MapToDifficulty(Technique hardest, int givens)
        {
            switch (hardest)
            {
                case Technique.None:
                case Technique.NakedSingle:
                case Technique.HiddenSingle:
                    // 仅凭 Singles 可解:难度主要由提示数决定(空位越少越简单)
                    if (givens >= 45) return Difficulty.Beginner;
                    if (givens >= 36) return Difficulty.Easy;
                    if (givens >= 30) return Difficulty.Medium;
                    return Difficulty.Hard;

                default: // Backtracking(需要猜测)
                    if (givens >= 32) return Difficulty.Hard;
                    if (givens >= 26) return Difficulty.Expert;
                    return Difficulty.Master;
            }
        }

        /// <summary>返回某难度对应的目标提示数(生成器挖洞用,含随机波动)。</summary>
        public static int TargetClueCount(Difficulty difficulty, Random rng)
        {
            switch (difficulty)
            {
                case Difficulty.Beginner: return rng.Next(45, 51); // 45~50
                case Difficulty.Easy:     return rng.Next(36, 44); // 36~43
                case Difficulty.Medium:   return rng.Next(32, 37); // 32~36
                case Difficulty.Hard:     return rng.Next(27, 33); // 27~32
                case Difficulty.Expert:   return rng.Next(24, 28); // 24~27
                case Difficulty.Master:   return rng.Next(22, 26); // 22~25
                default:                  return rng.Next(32, 40);
            }
        }

        private static bool IsFull(int[] cells)
        {
            for (int i = 0; i < SudokuBoard.CellCount; i++)
                if (cells[i] == 0) return false;
            return true;
        }
    }
}
