using Sudoku.Core;

namespace Sudoku.Core.Tests
{
    /// <summary>测试共用工具与已知谜题。</summary>
    internal static class Fixtures
    {
        /// <summary>
        /// 用 9 行字符串构造棋盘,'.' 或 '0' 表示空格,字符 '1'~'9' 为给定数。
        /// </summary>
        public static SudokuBoard FromString(params string[] rows)
        {
            var cells = new int[SudokuBoard.CellCount];
            for (int r = 0; r < SudokuBoard.Size; r++)
            {
                for (int c = 0; c < SudokuBoard.Size; c++)
                {
                    char ch = rows[r][c];
                    cells[SudokuBoard.Index(r, c)] = (ch >= '1' && ch <= '9') ? (ch - '0') : 0;
                }
            }
            return new SudokuBoard(cells);
        }

        // 经典唯一解谜题(30 个提示数)
        public static readonly string[] ClassicPuzzleRows =
        {
            "53..7....",
            "6..195...",
            ".98....6.",
            "8...6...3",
            "4..8.3..1",
            "7...2...6",
            ".6....28.",
            "...419..5",
            "....8..79"
        };

        // 上述谜题的完整解
        public static readonly string[] ClassicSolutionRows =
        {
            "534678912",
            "672195348",
            "198342567",
            "859761423",
            "426853791",
            "713924856",
            "961537284",
            "287419635",
            "345286179"
        };

        public static SudokuBoard ClassicPuzzle => FromString(ClassicPuzzleRows);
        public static SudokuBoard ClassicSolution => FromString(ClassicSolutionRows);
    }
}
