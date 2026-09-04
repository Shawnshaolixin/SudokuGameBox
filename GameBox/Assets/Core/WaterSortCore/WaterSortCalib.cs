using System;

namespace WaterSort.Core
{
    /// <summary>单档色数的采样汇总(数据记录,供人工判读/工具出表)。</summary>
    public sealed class WaterSortCalibResult
    {
        public int Colors;           // 采样色数
        public int Samples;          // 请求样本数(每个样本散射到"可测实解"或重洗上限 MaxAttempts)
        public int Solved;           // 命中可测实解的样本数(该样本步数计入分布统计)
        public int Unsolved;         // 重洗上限内始终无可测实解的样本数(死局/仅封顶解/命中率过低)

        // 散射结果分类计数(校准判读与 M2.2 吞吐预估用,口径与生成器一致——生成器只收窗口内实解):
        public int PreSolved;        // 散射即终态(0 步废题):生成器被 MinSteps 下界滤掉,校准不计入分布
        public int CapHits;          // 首解封顶(≥AnyBoundCap,无状态 DFS 垃圾漫游解):仍是可解证明,
                                     // 但步数不代表真实难度,生成器被 MaxSteps(≤60)滤掉,校准不计入分布
        public int Timeouts;         // 求解超时散射次数(代理=快筛 400ms 超时;最优=IDA* 限时超时)
        public int ScattersTried;    // 求解散射总次数(PreSolved+CapHits+Timeouts+其余未中尝试+命中)
        public double RealHitRate;   // 单次散射命中可测实解的概率(≈Solved/ScattersTried,生成吞吐预估用)

        public int MinSteps;         // 实解样本的最小步数(无实解为 0)
        public int MaxSteps;         // 实解样本的最大步数
        public double AvgSteps;      // 实解样本的平均步数
        public long TotalMs;         // 采样总耗时(含求解)

        // M2 校准数据任务判读分布形态用(最近秩法,同花顺式分位;无实解全为 0):
        // 定档只看 min/avg/max 会被极端样本带偏,分位给出"区间实际覆盖了哪些比例样本"
        public int P10;              // 实解样本第 10 分位步数
        public int P25;              // 第 25 分位
        public int P50;              // 中位数
        public int P75;              // 第 75 分位
        public int P90;              // 第 90 分位
    }

    /// <summary>
    /// 难度代理校准工具(WS-03「出包前题库采样校准」):批量采样"散射出的可解板"的实测步数分布
    /// ——≥4 色取 SolveAny 首解深度(代理)、≤3 色取 IDA* 精确最优,聚合统计供 M2 校准数据任务
    /// 对照 PRD 默认区间定档后回写配置默认表。
    /// 口径(M2.1 修正,镜像生成器 WaterSortLevelGen.Generate):
    /// 1. 每个样本独立重洗(seedBase+i 为种子),最多 MaxAttempts 次散射;
    /// 2. 只认「可测实解」= 求解成功且 1 ≤ 步数 &lt; AnyBoundCap——封顶首解是 DFS 漫游垃圾解,
    ///    散射即终态是废题,两者均换题重散并单独计数(生成器经步窗过滤等价剔除);
    /// 3. 首个可测实解命中即收,步数进分布统计;全程可复现。
    /// </summary>
    public static class WaterSortCalib
    {
        /// <summary>采样代理深度(≥4 色):每样本 SolveAny 400ms 快筛,记录可测实解深度分布。</summary>
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
            var steps = new System.Collections.Generic.List<int>(samples); // 实解步数留档 → 排序取分位
            for (int i = 0; i < samples; i++)
            {
                var rng = new Random(seedBase + i); // 每样本独立种子序列,可复现
                bool hit = false;
                for (int attempt = 0; attempt < WaterSortLevelGen.MaxAttempts; attempt++)
                {
                    var board = WaterSortLevelGen.RandomScatter(colors, rng);
                    var res = solve(board);
                    r.ScattersTried++;
                    if (res.TimedOut)
                    {
                        r.Timeouts++; // 限时内无解:换题(生成器同判为未过筛)
                        continue;
                    }
                    if (!res.Solved) continue; // 搜索空间耗尽仍无解(死局),换题
                    if (res.Steps == 0)
                    {
                        r.PreSolved++; // 散射即终态:0 步废题,生成器 MinSteps 下界等价剔除
                        continue;
                    }
                    if (res.Steps >= WaterSortSolver.AnyBoundCap)
                    {
                        r.CapHits++; // 封顶垃圾漫游解:可解证明但无法判读难度,生成器 MaxSteps 上界等价剔除
                        continue;
                    }
                    // 可测实解:命中收样
                    r.Solved++;
                    if (res.Steps < min) min = res.Steps;
                    if (res.Steps > max) max = res.Steps;
                    sum += res.Steps;
                    steps.Add(res.Steps);
                    hit = true;
                    break;
                }
                if (!hit) r.Unsolved++;
            }
            sw.Stop();
            r.TotalMs = sw.ElapsedMilliseconds;
            r.MinSteps = r.Solved > 0 ? (int)min : 0;
            r.MaxSteps = r.Solved > 0 ? (int)max : 0;
            r.AvgSteps = r.Solved > 0 ? sum / (double)r.Solved : 0;
            r.RealHitRate = r.ScattersTried > 0 ? r.Solved / (double)r.ScattersTried : 0;
            if (steps.Count > 0)
            {
                steps.Sort();
                r.P10 = Percentile(steps, 0.10);
                r.P25 = Percentile(steps, 0.25);
                r.P50 = Percentile(steps, 0.50);
                r.P75 = Percentile(steps, 0.75);
                r.P90 = Percentile(steps, 0.90);
            }
            return r;
        }

        /// <summary>最近秩分位:排序列表取 q 分位元素(q∈(0,1];样本少时退化为就近秩,不插值)。</summary>
        static int Percentile(System.Collections.Generic.List<int> sorted, double q)
        {
            int idx = (int)System.Math.Ceiling(q * sorted.Count) - 1;
            if (idx < 0) idx = 0;
            return sorted[idx];
        }
    }
}
