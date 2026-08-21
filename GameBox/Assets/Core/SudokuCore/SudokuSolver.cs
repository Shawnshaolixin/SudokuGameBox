namespace Sudoku.Core
{
    /// <summary>
    /// 数独求解器(纯 C#):
    /// - Solve:求任意一个解;
    /// - CountSolutions:统计解数(带上限提前退出),用于唯一解校验;
    /// - HasUniqueSolution:是否唯一解。
    /// 采用「最少候选优先(MRV)」的回溯,正确性优先,性能对 81 格规模足够。
    /// </summary>
    public static class SudokuSolver
    {
        /// <summary>尝试求解;成功返回 true 并输出解,无解(含冲突盘面)返回 false。</summary>
        public static bool Solve(SudokuBoard board, out SudokuBoard solution)
        {
            if (board == null) throw new System.ArgumentNullException(nameof(board));
            // 输入本身已冲突(行/列/宫存在重复数字)则直接判无解,
            // 避免把非法盘面当成可解盘面继续回溯。
            if (board.HasConflicts()) { solution = null; return false; }
            var cells = board.ToArray();
            if (!SolveCore(cells))
            {
                solution = null;
                return false;
            }
            solution = new SudokuBoard(cells);
            return true;
        }

        /// <summary>统计解数,达到 limit 后提前返回(默认 2,用于唯一解判断)。</summary>
        public static int CountSolutions(SudokuBoard board, int limit = 2)
        {
            if (board == null) throw new System.ArgumentNullException(nameof(board));
            if (limit < 1) return 0;
            if (board.HasConflicts()) return 0; // 输入本身冲突 => 无解
            return CountCore(board.ToArray(), limit);
        }

        public static bool HasUniqueSolution(SudokuBoard board) => CountSolutions(board, 2) == 1;

        // 求第一个解(回溯 + MRV)
        private static bool SolveCore(int[] cells)
        {
            int best = -1, bestCount = int.MaxValue;
            for (int i = 0; i < SudokuBoard.CellCount; i++)
            {
                if (cells[i] != 0) continue;
                int mask = CandidateMath.GetMask(cells, i);
                if (mask == 0) return false;             // 该格无候选 => 死路
                int count = CandidateMath.PopCount(mask);
                if (count < bestCount) { bestCount = count; best = i; if (count == 1) break; }
            }
            if (best == -1) return true;                 // 已填满

            int m = CandidateMath.GetMask(cells, best);
            for (int d = 1; d <= SudokuBoard.Size; d++)
            {
                if ((m & (1 << d)) == 0) continue;
                cells[best] = d;
                if (SolveCore(cells)) return true;
                cells[best] = 0;
            }
            return false;
        }

        // 统计解数(带上限提前退出)
        private static int CountCore(int[] cells, int limit)
        {
            int best = -1, bestCount = int.MaxValue;
            for (int i = 0; i < SudokuBoard.CellCount; i++)
            {
                if (cells[i] != 0) continue;
                int mask = CandidateMath.GetMask(cells, i);
                if (mask == 0) return 0;
                int count = CandidateMath.PopCount(mask);
                if (count < bestCount) { bestCount = count; best = i; if (count == 1) break; }
            }
            if (best == -1) return 1;                    // 已填满,记一个解

            int total = 0;
            int m = CandidateMath.GetMask(cells, best);
            for (int d = 1; d <= SudokuBoard.Size; d++)
            {
                if ((m & (1 << d)) == 0) continue;
                cells[best] = d;
                total += CountCore(cells, limit);
                cells[best] = 0;
                if (total >= limit) return total;
            }
            return total;
        }
    }
}
