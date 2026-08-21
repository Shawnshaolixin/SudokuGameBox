namespace Sudoku.Core
{
    /// <summary>
    /// 一次生成的结果:谜题(含空格)、完整解、目标难度、算法评分。
    /// </summary>
    public sealed class GeneratedPuzzle
    {
        public SudokuBoard Puzzle { get; }
        public SudokuBoard Solution { get; }
        public Difficulty Difficulty { get; }       // 目标难度(提示数档位)
        public Difficulty RatedDifficulty { get; }  // 算法评分(诊断/后续调优用)

        public GeneratedPuzzle(SudokuBoard puzzle, SudokuBoard solution, Difficulty difficulty, Difficulty ratedDifficulty)
        {
            Puzzle = puzzle;
            Solution = solution;
            Difficulty = difficulty;
            RatedDifficulty = ratedDifficulty;
        }

        public int ClueCount => Puzzle.CountGivens();
    }
}
