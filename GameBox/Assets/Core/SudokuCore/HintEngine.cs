namespace Sudoku.Core
{
    /// <summary>
    /// 提示引擎(纯 C#):优先给出「逻辑单步」(Singles),体验更好;
    /// 逻辑推不动时回溯兜底,给出任意一个可解格。
    /// </summary>
    public static class HintEngine
    {
        /// <summary>一步提示:在 (Row, Col) 填入 Value,并附带所用技巧。</summary>
        public readonly struct Hint
        {
            public readonly int Row;
            public readonly int Col;
            public readonly int Value;
            public readonly Technique Technique;

            public Hint(int row, int col, int value, Technique technique)
            {
                Row = row; Col = col; Value = value; Technique = technique;
            }
        }

        /// <summary>尝试给出一格提示;无解/已解返回 false。</summary>
        public static bool GetHint(SudokuBoard current, out Hint hint)
        {
            if (current == null) throw new System.ArgumentNullException(nameof(current));

            // 优先逻辑单步
            if (LogicSolver.TryFindSingle(current, out int idx, out int val, out Technique t))
            {
                hint = new Hint(SudokuBoard.RowOf(idx), SudokuBoard.ColOf(idx), val, t);
                return true;
            }

            // 回溯兜底:取解中第一个空格的值
            if (SudokuSolver.Solve(current, out var solution))
            {
                for (int i = 0; i < SudokuBoard.CellCount; i++)
                {
                    if (current[i] == 0)
                    {
                        hint = new Hint(SudokuBoard.RowOf(i), SudokuBoard.ColOf(i), solution[i], Technique.Backtracking);
                        return true;
                    }
                }
            }

            hint = default;
            return false;
        }
    }
}
