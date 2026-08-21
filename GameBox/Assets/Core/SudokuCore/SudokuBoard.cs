using System;
using System.Collections.Generic;

namespace Sudoku.Core
{
    /// <summary>
    /// 9x9 标准数独棋盘的数据模型(纯 C#,不依赖 Unity)。
    /// 内部用长度 81 的一维数组存储:0 表示空格,1~9 表示已填数字。
    /// 提供读写、坐标换算、合法性校验、候选数计算、深拷贝等基础能力。
    /// </summary>
    public sealed class SudokuBoard : IEquatable<SudokuBoard>
    {
        public const int Size = 9;                  // 行/列格子数
        public const int BoxSize = 3;               // 宫(Box)的边长
        public const int CellCount = Size * Size;   // 总格子数 81

        private readonly int[] _cells;

        // ---------- 构造 ----------
        public SudokuBoard()
        {
            _cells = new int[CellCount];
        }

        /// <summary>用 81 长度数组构造(内部深拷贝,外部后续修改不影响本对象)。</summary>
        public SudokuBoard(int[] cells)
        {
            if (cells == null || cells.Length != CellCount)
                throw new ArgumentException($"棋盘必须恰好包含 {CellCount} 个格子。", nameof(cells));
            _cells = (int[])cells.Clone();
        }

        private SudokuBoard(SudokuBoard other)
        {
            _cells = (int[])other._cells.Clone();
        }

        // ---------- 坐标换算(静态工具) ----------
        public static int Index(int row, int col) => row * Size + col;
        public static int RowOf(int index) => index / Size;
        public static int ColOf(int index) => index % Size;
        public static int BoxOf(int row, int col) => (row / BoxSize) * BoxSize + (col / BoxSize);

        // ---------- 读写 ----------
        public int this[int row, int col]
        {
            get => _cells[Index(row, col)];
            set => _cells[Index(row, col)] = value;
        }

        public int this[int index]
        {
            get => _cells[index];
            set => _cells[index] = value;
        }

        public int Get(int row, int col) => this[row, col];
        public void Set(int row, int col, int value) => this[row, col] = value;
        public void Clear(int row, int col) => this[row, col] = 0;
        public bool IsEmpty(int index) => _cells[index] == 0;
        public bool IsEmpty(int row, int col) => this[row, col] == 0;

        /// <summary>返回底层数组的副本,避免外部直接修改内部状态。</summary>
        public int[] ToArray() => (int[])_cells.Clone();

        /// <summary>统计已填数字个数(即提示数)。</summary>
        public int CountGivens()
        {
            int count = 0;
            for (int i = 0; i < CellCount; i++)
                if (_cells[i] != 0) count++;
            return count;
        }

        public SudokuBoard Clone() => new SudokuBoard(this);

        // ---------- 校验 ----------
        /// <summary>判断在 (row, col) 填入 value 是否与同行/列/宫冲突(忽略该格自身)。</summary>
        public bool IsValidPlacement(int row, int col, int value)
        {
            if (value < 1 || value > Size) return false;

            int rowStart = row * Size;
            for (int c = 0; c < Size; c++)
                if (c != col && _cells[rowStart + c] == value) return false;

            for (int r = 0; r < Size; r++)
                if (r != row && _cells[Index(r, col)] == value) return false;

            int boxRow = (row / BoxSize) * BoxSize;
            int boxCol = (col / BoxSize) * BoxSize;
            for (int r = boxRow; r < boxRow + BoxSize; r++)
                for (int c = boxCol; c < boxCol + BoxSize; c++)
                    if ((r != row || c != col) && _cells[Index(r, c)] == value) return false;

            return true;
        }

        /// <summary>是否存在行/列/宫内重复数字冲突(忽略空格)。</summary>
        public bool HasConflicts()
        {
            // 行
            for (int r = 0; r < Size; r++)
            {
                int seen = 0;
                for (int c = 0; c < Size; c++)
                {
                    int v = this[r, c];
                    if (v == 0) continue;
                    int bit = 1 << v;
                    if ((seen & bit) != 0) return true;
                    seen |= bit;
                }
            }
            // 列
            for (int c = 0; c < Size; c++)
            {
                int seen = 0;
                for (int r = 0; r < Size; r++)
                {
                    int v = this[r, c];
                    if (v == 0) continue;
                    int bit = 1 << v;
                    if ((seen & bit) != 0) return true;
                    seen |= bit;
                }
            }
            // 宫
            for (int br = 0; br < BoxSize; br++)
            for (int bc = 0; bc < BoxSize; bc++)
            {
                int seen = 0;
                for (int r = br * BoxSize; r < br * BoxSize + BoxSize; r++)
                for (int c = bc * BoxSize; c < bc * BoxSize + BoxSize; c++)
                {
                    int v = this[r, c];
                    if (v == 0) continue;
                    int bit = 1 << v;
                    if ((seen & bit) != 0) return true;
                    seen |= bit;
                }
            }
            return false;
        }

        /// <summary>是否已填满且无冲突(即合法终盘)。</summary>
        public bool IsSolved()
        {
            for (int i = 0; i < CellCount; i++)
                if (_cells[i] == 0) return false;
            return !HasConflicts();
        }

        /// <summary>计算某格的合法候选数(已填则返回空列表)。</summary>
        public List<int> GetCandidates(int row, int col)
        {
            var result = new List<int>();
            if (!IsEmpty(row, col)) return result;
            for (int v = 1; v <= Size; v++)
                if (IsValidPlacement(row, col, v)) result.Add(v);
            return result;
        }

        // ---------- 相等性(值比较) ----------
        public bool Equals(SudokuBoard other)
        {
            if (other is null) return false;
            for (int i = 0; i < CellCount; i++)
                if (_cells[i] != other._cells[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as SudokuBoard);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < CellCount; i++)
                    hash = hash * 31 + _cells[i];
                return hash;
            }
        }
    }
}
