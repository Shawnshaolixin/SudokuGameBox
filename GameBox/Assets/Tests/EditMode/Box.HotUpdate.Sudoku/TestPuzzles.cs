using Box.HotUpdate.Sudoku;
using Sudoku.Core;

namespace Box.HotUpdate.Sudoku.Tests
{
    /// <summary>测试用固定谜题:手写合法终盘(每行/列/宫均为 1-9 轮换),按需挖洞。</summary>
    public static class TestPuzzles
    {
        // 经典轮换终盘(已验证:行/列/宫均合法)
        public static readonly int[] FinishedBoard =
        {
            1,2,3,4,5,6,7,8,9,
            4,5,6,7,8,9,1,2,3,
            7,8,9,1,2,3,4,5,6,
            2,3,4,5,6,7,8,9,1,
            5,6,7,8,9,1,2,3,4,
            8,9,1,2,3,4,5,6,7,
            3,4,5,6,7,8,9,1,2,
            6,7,8,9,1,2,3,4,5,
            9,1,2,3,4,5,6,7,8,
        };

        /// <summary>从终盘挖洞构造谜题(洞即玩家可填格);不保证唯一解,测试只依赖 Solution 对照。</summary>
        public static GeneratedPuzzle MakePuzzle(params int[] holes)
        {
            var solution = new SudokuBoard(FinishedBoard);
            var puzzle = solution.Clone();
            foreach (int h in holes) puzzle[h] = 0;
            return new GeneratedPuzzle(puzzle, solution, Difficulty.Easy, Difficulty.Easy);
        }

        /// <summary>无洞谜题(全部给定,用于"给定格不可改"等测试)。</summary>
        public static GeneratedPuzzle FullPuzzle() => MakePuzzle();
    }

    /// <summary>测试时钟:可控时间推进。</summary>
    public sealed class FakeClock : IClock
    {
        public float Now { get; set; } = 100f;
        public void Advance(float seconds) => Now += seconds;
    }
}
