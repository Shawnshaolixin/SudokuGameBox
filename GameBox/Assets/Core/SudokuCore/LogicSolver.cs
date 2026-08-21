namespace Sudoku.Core
{
    /// <summary>
    /// 逻辑求解器(纯 C#):目前实现「显性唯一(Naked Single)」与「隐性唯一(Hidden Single)」两步,
    /// 用于提示引擎与难度评分。更高阶技巧(Pairs / Pointing / X-Wing 等)属 P1,见 GDD §3.3。
    /// </summary>
    public static class LogicSolver
    {
        /// <summary>尝试用 Singles 推进一步;成功返回 true 并输出该步。</summary>
        public static bool TryFindSingle(SudokuBoard board, out int index, out int value, out Technique technique)
        {
            if (board == null) throw new System.ArgumentNullException(nameof(board));
            return TryFindSingleCore(board.ToArray(), out index, out value, out technique);
        }

        // 内部版本:直接操作 int[],避免每次复制棋盘。
        internal static bool TryFindSingleCore(int[] cells, out int index, out int value, out Technique technique)
        {
            index = -1; value = 0; technique = Technique.None;

            // 1) Naked Single:某空格的候选数唯一
            for (int i = 0; i < SudokuBoard.CellCount; i++)
            {
                if (cells[i] != 0) continue;
                int mask = CandidateMath.GetMask(cells, i);
                if (mask == 0) return false;                 // 已无候选,盘面冲突
                if (CandidateMath.PopCount(mask) == 1)
                {
                    index = i;
                    value = CandidateMath.FirstDigit(mask);
                    technique = Technique.NakedSingle;
                    return true;
                }
            }

            // 2) Hidden Single:某数字在行/列/宫内只能落在唯一空格
            for (int unit = 0; unit < SudokuBoard.Size; unit++)
            {
                if (FindHiddenSingleInRow(cells, unit, out index, out value)) { technique = Technique.HiddenSingle; return true; }
                if (FindHiddenSingleInCol(cells, unit, out index, out value)) { technique = Technique.HiddenSingle; return true; }
                if (FindHiddenSingleInBox(cells, unit, out index, out value)) { technique = Technique.HiddenSingle; return true; }
            }

            return false;
        }

        private static bool FindHiddenSingleInRow(int[] cells, int row, out int index, out int value)
        {
            index = -1; value = 0;
            int rowStart = row * SudokuBoard.Size;
            for (int d = 1; d <= SudokuBoard.Size; d++)
            {
                bool placed = false; int found = -1, count = 0;
                for (int c = 0; c < SudokuBoard.Size; c++)
                {
                    int idx = rowStart + c;
                    if (cells[idx] == d) { placed = true; continue; }
                    if (cells[idx] != 0) continue;
                    if ((CandidateMath.GetMask(cells, idx) & (1 << d)) != 0) { count++; found = idx; }
                }
                if (!placed && count == 1) { index = found; value = d; return true; }
            }
            return false;
        }

        private static bool FindHiddenSingleInCol(int[] cells, int col, out int index, out int value)
        {
            index = -1; value = 0;
            for (int d = 1; d <= SudokuBoard.Size; d++)
            {
                bool placed = false; int found = -1, count = 0;
                for (int r = 0; r < SudokuBoard.Size; r++)
                {
                    int idx = SudokuBoard.Index(r, col);
                    if (cells[idx] == d) { placed = true; continue; }
                    if (cells[idx] != 0) continue;
                    if ((CandidateMath.GetMask(cells, idx) & (1 << d)) != 0) { count++; found = idx; }
                }
                if (!placed && count == 1) { index = found; value = d; return true; }
            }
            return false;
        }

        private static bool FindHiddenSingleInBox(int[] cells, int box, out int index, out int value)
        {
            index = -1; value = 0;
            int boxRow = (box / SudokuBoard.BoxSize) * SudokuBoard.BoxSize;
            int boxCol = (box % SudokuBoard.BoxSize) * SudokuBoard.BoxSize;
            for (int d = 1; d <= SudokuBoard.Size; d++)
            {
                bool placed = false; int found = -1, count = 0;
                for (int r = boxRow; r < boxRow + SudokuBoard.BoxSize; r++)
                for (int c = boxCol; c < boxCol + SudokuBoard.BoxSize; c++)
                {
                    int idx = SudokuBoard.Index(r, c);
                    if (cells[idx] == d) { placed = true; continue; }
                    if (cells[idx] != 0) continue;
                    if ((CandidateMath.GetMask(cells, idx) & (1 << d)) != 0) { count++; found = idx; }
                }
                if (!placed && count == 1) { index = found; value = d; return true; }
            }
            return false;
        }
    }
}
