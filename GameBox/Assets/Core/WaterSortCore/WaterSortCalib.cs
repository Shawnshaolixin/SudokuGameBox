using System;

namespace WaterSort.Core
{
    /// <summary>单档色数的采样汇总(数据记录,供人工判读/工具出表)。</summary>
    public sealed class WaterSortCalibResult
    {
        public int Colors;           // 采样色数
        public int Samples;          // 请求样本数
        public int Solved;           // 命中可解并测得深度的样本数(未解样本计入下面三类差额)
        public int Unsolved;         // MaxAttempts 内散射始终不过快筛/超时
        public int MinSteps;         // 命中样本的最小实测步数(无命中为 0)
        public int MaxSteps;         // 命中样本的最大实测步数
        public double AvgSteps;      // 命中样本的平均实测步数
        public long TotalMs;         // 采样总耗时(含求解)
        public int TimedOutOnSolve;  // 快筛通过但正式求解超时(仅最优模式可能)
    }

    /// <summary>
    /// 难度代理校准工具(WS-03「出包前题库采样校准」的雏形,M1.1):
    /// 批量采样"散射出的可解板"的实测步数分布——≥4 色取 SolveAny 首解深度(代理)、≤3 色取 IDA* 精确最优,
    /// 聚合统计供 M2 正式校准数据任务对照 PRD 默认区间定档后回写配置默认表。
    /// 每个样本独立重洗(seedBase+i 为种子),最多 MaxAttempts 次散射取首个可解板,保证可复现。
    /// </summary>
    public static class WaterSortCalib
    {
        /// <summary>采样代理深度(≥4 色):每样本 SolveAny 400ms 快筛,记录首解深度分布。</summary>
        public static WaterSortCalibResult SampleProxyDepth(int colors, int seedBase, int samples)
        {
            return Sample(colors, seedBase, samples,
                board => WaterSortSolver.SolveAny(board, WaterSortLevelGen.SolveScreenMs));
        }

        /// <summary>采样精确最优步数(≤3 色):每样本 IDA*,限时默认与生成器一致。</summary>
        public static WaterSortCalibResult SampleOptimalSteps(int colors, int seedBase, int samples,
            int timeLimitMs = WaterSortLevelGen.OptimalMs)
        {
            return Sample(colors, seedBase, samples, board => WaterSortSolver.SolveOptimal(board, timeLimitMs));
        }

        private static WaterSortCalibResult Sample(int colors, int seedBase, int samples,
            Func<WaterSortBoard, WaterSortSolveResult> solve)
        {
            if (colors < 3) throw new ArgumentException($"采样色数须 ≥3,实际 {colors}");
            var r = new WaterSortCalibResult { Colors = colors, Samples = samples };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long min = long.MaxValue, max = 0, sum = 0;
            for (int i = 0; i < samples; i++)
            {
                var rng = new Random(seedBase + i); // 每样本独立种子序列,可复现
                bool hit = false;
                for (int attempt = 0; attempt < WaterSortLevelGen.MaxAttempts; attempt++)
                {
                    var board = WaterSortLevelGen.RandomScatter(colors, rng);
                    var res = solve(board);
                    if (res.Solved)
                    {
                        r.Solved++;
                        if (res.Steps < min) min = res.Steps;
                        if (res.Steps > max) max = res.Steps;
                        sum += res.Steps;
                        hit = true;
                        break;
                    }
                    if (res.TimedOut) r.TimedOutOnSolve++; // 仅最优模式可能(快筛 400ms 已过)
                }
                if (!hit) r.Unsolved++;
            }
            sw.Stop();
            r.TotalMs = sw.ElapsedMilliseconds;
            r.MinSteps = r.Solved > 0 ? (int)min : 0;
            r.MaxSteps = r.Solved > 0 ? (int)max : 0;
            r.AvgSteps = r.Solved > 0 ? sum / (double)r.Solved : 0;
            return r;
        }
    }
}
