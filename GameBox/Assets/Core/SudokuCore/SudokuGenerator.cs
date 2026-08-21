using System;

namespace Sudoku.Core
{
    /// <summary>
    /// 数独谜题生成器(纯 C#):
    /// 1) 随机生成完整合法终盘;
    /// 2) 按目标提示数挖洞(可选 180° 对称),每步都保证唯一解;
    /// 3) 返回谜题、解与目标难度(阶段 A 难度以提示数档位为准)。
    /// </summary>
    public sealed class SudokuGenerator
    {
        private static int _seedCounter; // 让连续新建的生成器种子尽量不同

        private readonly Random _random;

        public SudokuGenerator() : this(Environment.TickCount ^ _seedCounter++) { }

        public SudokuGenerator(int seed)
        {
            _random = new Random(seed);
        }

        public SudokuGenerator(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>生成一个随机完整合法终盘(9x9 全填且无冲突)。</summary>
        public SudokuBoard GenerateSolvedBoard()
        {
            var cells = new int[SudokuBoard.CellCount];
            FillBoard(cells);
            return new SudokuBoard(cells);
        }

        /// <summary>
        /// 生成指定难度的谜题,结果保证唯一解。
        /// </summary>
        /// <param name="difficulty">目标难度(决定提示数档位)。</param>
        /// <param name="symmetric">是否按 180° 旋转对称挖洞。</param>
        public GeneratedPuzzle Generate(Difficulty difficulty, bool symmetric = true)
        {
            var solution = GenerateSolvedBoard();
            int targetClues = DifficultyRater.TargetClueCount(difficulty, _random);
            var puzzle = DigHoles(solution, targetClues, symmetric);

            // DigHoles 每步都校验唯一解,结果必然唯一;此处兜底再校验一次。
            if (!SudokuSolver.HasUniqueSolution(puzzle))
                puzzle = solution.Clone();

            var rated = DifficultyRater.Rate(puzzle);
            return new GeneratedPuzzle(puzzle, solution, difficulty, rated);
        }

        // 随机完整终盘:最少候选优先 + 随机候选顺序的回溯
        private bool FillBoard(int[] cells)
        {
            int best = -1, bestCount = int.MaxValue;
            for (int i = 0; i < SudokuBoard.CellCount; i++)
            {
                if (cells[i] != 0) continue;
                int mask = CandidateMath.GetMask(cells, i);
                if (mask == 0) return false;
                int count = CandidateMath.PopCount(mask);
                if (count < bestCount) { bestCount = count; best = i; if (count == 1) break; }
            }
            if (best == -1) return true; // 已填满

            int m = CandidateMath.GetMask(cells, best);
            var digits = new int[SudokuBoard.Size];
            int n = 0;
            for (int d = 1; d <= SudokuBoard.Size; d++)
                if ((m & (1 << d)) != 0) digits[n++] = d;
            Shuffle(digits, n);

            for (int k = 0; k < n; k++)
            {
                cells[best] = digits[k];
                if (FillBoard(cells)) return true;
                cells[best] = 0;
            }
            return false;
        }

        // 挖洞:逐格尝试移除,移除后仍保持唯一解才保留
        private SudokuBoard DigHoles(SudokuBoard solution, int targetClues, bool symmetric)
        {
            var puzzle = solution.Clone();
            var order = ShuffledIndices();
            int toRemove = SudokuBoard.CellCount - targetClues;
            int removed = 0;

            foreach (int idx in order)
            {
                if (removed >= toRemove) break;
                if (puzzle[idx] == 0) continue;

                int counterpart = symmetric ? (SudokuBoard.CellCount - 1 - idx) : -1;
                bool hasPair = counterpart >= 0 && counterpart != idx && puzzle[counterpart] != 0;

                int value = puzzle[idx];
                int pairValue = hasPair ? puzzle[counterpart] : 0;

                puzzle[idx] = 0;
                if (hasPair) puzzle[counterpart] = 0;

                if (SudokuSolver.HasUniqueSolution(puzzle))
                {
                    removed += hasPair ? 2 : 1;
                }
                else
                {
                    // 破坏唯一解,回滚
                    puzzle[idx] = value;
                    if (hasPair) puzzle[counterpart] = pairValue;
                }
            }
            return puzzle;
        }

        private int[] ShuffledIndices()
        {
            var arr = new int[SudokuBoard.CellCount];
            for (int i = 0; i < arr.Length; i++) arr[i] = i;
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }

        private void Shuffle(int[] arr, int count)
        {
            for (int i = count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
        }
    }
}
