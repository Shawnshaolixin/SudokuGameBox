using Sudoku.Core;

namespace Box.Gameplay
{
    /// <summary>谜题工厂:普通难度生成 + 每日挑战(日期种子确定性)。</summary>
    public static class PuzzleFactory
    {
        /// <summary>生成指定难度谜题(唯一解,可解)。</summary>
        public static GeneratedPuzzle Create(Difficulty difficulty) => new SudokuGenerator().Generate(difficulty);

        /// <summary>每日挑战:同一 seed 永远生成同一题(日期种子 → 每日一题)。</summary>
        public static GeneratedPuzzle CreateDaily(int seed) => new SudokuGenerator(seed).Generate(Difficulty.Medium);
    }
}
