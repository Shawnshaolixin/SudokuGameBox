namespace Sudoku.Core
{
    /// <summary>
    /// 候选数位掩码工具(内部使用):用 int 的 bit1~bit9 表示数字 1~9 是否可填。
    /// 供求解器 / 逻辑求解器 / 生成器 / 难度评分共用,避免重复实现。
    /// </summary>
    internal static class CandidateMath
    {
        /// <summary>bit1~bit9 全置位,即数字 1~9 全部可填。</summary>
        public const int FullMask =
            (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5) |
            (1 << 6) | (1 << 7) | (1 << 8) | (1 << 9);

        /// <summary>计算 index 处可填数字的位掩码(bit d = 1 表示 d 可填)。</summary>
        public static int GetMask(int[] cells, int index)
        {
            int used = 0;
            int row = SudokuBoard.RowOf(index);
            int col = SudokuBoard.ColOf(index);

            int rowStart = row * SudokuBoard.Size;
            for (int c = 0; c < SudokuBoard.Size; c++) used |= 1 << cells[rowStart + c];
            for (int r = 0; r < SudokuBoard.Size; r++) used |= 1 << cells[SudokuBoard.Index(r, col)];

            int boxRow = (row / SudokuBoard.BoxSize) * SudokuBoard.BoxSize;
            int boxCol = (col / SudokuBoard.BoxSize) * SudokuBoard.BoxSize;
            for (int r = boxRow; r < boxRow + SudokuBoard.BoxSize; r++)
            for (int c = boxCol; c < boxCol + SudokuBoard.BoxSize; c++)
                used |= 1 << cells[SudokuBoard.Index(r, c)];

            return FullMask & ~used;
        }

        /// <summary>掩码中置位的个数(即候选数数量)。</summary>
        public static int PopCount(int mask)
        {
            int n = 0;
            while (mask != 0) { n += mask & 1; mask >>= 1; }
            return n;
        }

        /// <summary>返回掩码中最小的数字;掩码为空返回 0。</summary>
        public static int FirstDigit(int mask)
        {
            for (int d = 1; d <= SudokuBoard.Size; d++)
                if ((mask & (1 << d)) != 0) return d;
            return 0;
        }
    }
}
