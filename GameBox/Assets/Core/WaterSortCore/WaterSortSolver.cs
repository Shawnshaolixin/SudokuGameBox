using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WaterSort.Core
{
    /// <summary>求解结果。</summary>
    public sealed class WaterSortSolveResult
    {
        public bool Solved;                    // 是否找到解
        public int Steps;                      // 步数(最优解找到时=最优步数;失败为 0)
        public bool TimedOut;                  // 是否因超时终止
        public long ElapsedMs;                 // 实际耗时
        public List<WaterSortMove> Solution;   // 解路径(未解决为空)
    }

    /// <summary>
    /// 水排序求解器(正式版,由 Spike 迁移正式化 M1.1):
    /// IDA* 求最优解(≤3 色实时可用)+ SolveAny 任意解(全色数 ≤400ms 快筛)。
    /// 启发式 h = ceil(Σ 每管无序滴数 / 2):"无序滴" = 不在底部同色连续块内的滴,
    /// 一次移动最多让 2 滴归位,该下界保守可采纳。
    /// 剪枝:禁止"立即逆转"上一步——这两步相互抵消,最优解必可去掉,不丢最优性。
    /// BFS 精确对照已移出产品面(仅测试 oracle 用),见 Spike 结论:最优解仅 ≤3 色实时可用。
    /// </summary>
    public static class WaterSortSolver
    {
        private const int MaxDepth = 256; // 求解深度上限(洗牌 80 步的题最优解远小于此)

        /// <summary>IDA* 求解,找最优解,timeLimitMs 超时保护。</summary>
        public static WaterSortSolveResult SolveOptimal(WaterSortBoard start, int timeLimitMs)
        {
            var sw = Stopwatch.StartNew();
            var result = new WaterSortSolveResult { Solution = new List<WaterSortMove>() };
            var path = new WaterSortMove[MaxDepth];
            int bound = Heuristic(start);

            while (bound <= MaxDepth && !result.TimedOut)
            {
                int nextBound = int.MaxValue;
                var solution = new List<WaterSortMove>();
                if (Dfs(start, 0, bound, ref nextBound, path, 0, sw, timeLimitMs, solution, out bool timedOut))
                {
                    sw.Stop();
                    result.Solved = true;
                    result.Steps = solution.Count;
                    result.Solution = solution;
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }
                if (timedOut)
                {
                    result.TimedOut = true;
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }
                bound = nextBound; // 迭代加深:以"最小溢出 f 值"为下一个界
            }
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }

        /// <summary>迭代加深 DFS:返回是否在 bound 内找到解;nextBound 收集最小溢出 f。</summary>
        private static bool Dfs(WaterSortBoard board, int g, int bound, ref int nextBound,
            WaterSortMove[] path, int depth, Stopwatch sw, int timeLimitMs,
            List<WaterSortMove> solution, out bool timedOut)
        {
            timedOut = false;
            int f = g + Heuristic(board);
            if (f > bound)
            {
                if (f < nextBound) nextBound = f;
                return false;
            }
            if (board.IsSolved())
            {
                for (int i = 0; i < depth; i++) solution.Add(path[i]);
                return true;
            }
            if (sw.ElapsedMilliseconds > timeLimitMs)
            {
                timedOut = true;
                return false;
            }

            var moves = board.LegalMoves();
            // 剪枝:禁止立即逆转上一步(两次移动相互抵消,去掉不丢最优解)
            if (depth > 0)
            {
                var prev = path[depth - 1];
                for (int i = moves.Count - 1; i >= 0; i--)
                    if (moves[i].Src == prev.Dst && moves[i].Dst == prev.Src)
                        moves.RemoveAt(i);
            }
            // 优先尝试"倒得多"的移动,加速收敛
            moves.Sort((a, b) => b.Count.CompareTo(a.Count));

            foreach (var m in moves)
            {
                path[depth] = m;
                if (Dfs(board.Apply(m), g + 1, bound, ref nextBound, path, depth + 1,
                        sw, timeLimitMs, solution, out timedOut))
                    return true;
                if (timedOut) return false;
            }
            return false;
        }

        /// <summary>启发式:一次移动最多让 2 滴归位 → ceil(无序滴总数 / 2)。</summary>
        private static int Heuristic(WaterSortBoard b)
        {
            int disordered = 0;
            for (int t = 0; t < b.TubeCount; t++)
            {
                int n = b.TopCount(t);
                if (n == 0) continue;
                byte c = b.Get(t, 0);
                int run = 1;
                while (run < n && b.Get(t, run) == c) run++; // 底部同色连续块长
                disordered += n - run;
            }
            return (disordered + 1) / 2;
        }

        /// <summary>
        /// 快速求解(不保证最优):一次性 bound 冲上限找任意解。
        /// 用途:生成器"可解性验证/高色数难度代理深度"与运行期提示(首解第一步,≤400ms)。
        /// 不适用场景:死局会烧满限时,不能用于最优验证(见 SolveOptimal)。
        /// </summary>
        public static WaterSortSolveResult SolveAny(WaterSortBoard start, int timeLimitMs, int boundCap = 100)
        {
            var sw = Stopwatch.StartNew();
            var result = new WaterSortSolveResult { Solution = new List<WaterSortMove>() };
            var path = new WaterSortMove[MaxDepth];
            int bound = Math.Min(boundCap, MaxDepth); // 一把冲上限:命中即停,不保证最优
            var solution = new List<WaterSortMove>();
            if (Dfs(start, 0, bound, ref bound, path, 0, sw, timeLimitMs, solution, out bool timedOut))
            {
                result.Solved = true;
                result.Steps = solution.Count;
                result.Solution = solution;
            }
            result.TimedOut = timedOut;
            result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }
    }
}
