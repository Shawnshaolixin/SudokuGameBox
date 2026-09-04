using System;
using System.Collections.Generic;

namespace WaterSort.Core
{
    /// <summary>
    /// 生成规格(一档难度):颜色数区间 + 实测步数接受区间(含界)。
    /// 步数语义随色数切换——≤3 色为 IDA* 精确最优步数;≥4 色为 SolveAny 首解深度代理
    /// (Spike 结论,19 文档 WS-02/WS-03)。区间数值由运营配置默认表供给(难度 ↔ 数值映射不进本程序集)。
    /// </summary>
    public sealed class WaterSortGenSpec
    {
        public WaterSortDifficulty Difficulty; // 落档结果标签(随 DTO 入库)
        public int MinColors = 3;              // 色数区间(含界)
        public int MaxColors = 3;
        public int MinSteps = 5;               // 步数区间下界:过滤"散射即近乎完成"的废题
        public int MaxSteps = 100;             // 步数区间上界:按难档精控
    }

    /// <summary>生成结果。</summary>
    public sealed class WaterSortGenResult
    {
        public bool Succeeded;                 // 是否命中(MaxAttempts 内散射+验证通过)
        public WaterSortBoard Board;           // 失败为 null
        public int Colors;                     // 命中的色数(规格区间内随机)
        public int MeasuredSteps;              // 落档实测步数(见规格说明的语义)
        public bool MeasuredOptimal;           // true=精确最优(≤3 色);false=代理深度(≥4 色)
        public int SeedUsed;                   // 复现用种子
        public int Attempts;                   // 实际重洗次数
    }

    /// <summary>
    /// 关卡生成器(正式版):随机构造合法混合板 + 玩家规则求解器验证可解性 + 难度落档,不合格换种子重试。
    ///
    /// 为何不做"从终态反向洗牌":玩家规则下满管(4 滴同色)禁止倒入空管,终态无任何合法移动,
    /// 洗牌路径必然依赖"生成期放宽规则(满块可倒空管)"的不可逆步骤,洗出的板玩家规则下大量锁死。
    /// 因此采用正向方案——随机散射出混合板,再用玩家求解器验证,通过即收
    /// (19 文档 v0.4 WS-02:关卡全预生成,设备端无生成器)。
    ///
    /// 落档(Spike 故意留白,正式版补齐,WS-03):
    /// 每块板先 SolveAny 400ms 快筛可解性 → ≤3 色再 IDA* 求精确最优步数,≥4 色直接以快筛首解深度为代理
    /// → 实测值落在规格步数区间内才收,确保题面难度贴合档位,避免"标 Hard 实则几步收工"。
    /// 代理深度的系统偏差(首解普遍非最优、偏深)由难度代理校准工具采样统计后回写配置区间校正(校准职责在 Calib + M2 数据任务)。
    /// </summary>
    public static class WaterSortLevelGen
    {
        public const int SolveScreenMs = 400;  // 可解性快筛限时(全色数:Spike 10 色亦 ≤400ms)
        public const int OptimalMs = 2000;     // ≤3 色精确最优限时(Spike 3 色实测 max 617ms,留余量)
        public const int MaxAttempts = 40;     // 单关重洗上限(同 Spike)

        /// <summary>生成一关:散射 → 快筛 → (≤3 色)精确测步 → 步数落档;同 seed 结果完全可复现。</summary>
        public static WaterSortGenResult Generate(WaterSortGenSpec spec, int seed)
        {
            if (spec == null || spec.MinColors < 3 || spec.MinColors > spec.MaxColors)
                throw new ArgumentException($"非法生成规格:色数区间须 ≥3 且递增,实际 [{spec?.MinColors},{spec?.MaxColors}]");
            var rng = new Random(seed);
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                // 色数在规格区间内随机(消耗一抽,同种子可复现);散射出的板在此色数下求解验证
                int colors = spec.MinColors == spec.MaxColors
                    ? spec.MinColors
                    : rng.Next(spec.MinColors, spec.MaxColors + 1);
                var board = RandomScatter(colors, rng);
                var screen = WaterSortSolver.SolveAny(board, SolveScreenMs);
                if (!screen.Solved) continue; // 快筛未过(死局/超时)直接换题

                bool optimal = colors <= 3; // Spike 结论:最优解仅 ≤3 色实时可用
                int measured = screen.Steps;
                if (optimal)
                {
                    var opt = WaterSortSolver.SolveOptimal(board, OptimalMs);
                    if (!opt.Solved) continue; // 快筛已过仍解不出(极小概率),保守换题
                    measured = opt.Steps;
                }
                if (measured < spec.MinSteps || measured > spec.MaxSteps) continue; // 落档不中换题
                return new WaterSortGenResult
                {
                    Succeeded = true,
                    Board = board,
                    Colors = colors,
                    MeasuredSteps = measured,
                    MeasuredOptimal = optimal,
                    SeedUsed = seed,
                    Attempts = attempt + 1,
                };
            }
            return new WaterSortGenResult { Succeeded = false, SeedUsed = seed, Attempts = MaxAttempts };
        }

        /// <summary>
        /// 随机散射:每色 4 滴滴序整体洗乱后按 4 滴切管(恰 colors 个满管)+ 2 空管。
        /// 同色滴乱序后可能聚堆或分散,形成真实中间态;极低概率散射回"全满同色"终态(由 MinSteps 过滤)。
        /// internal:仅生成器与校准工具(同程序集)需要。
        /// </summary>
        internal static WaterSortBoard RandomScatter(int colors, Random rng)
        {
            int totalDrops = colors * WaterSortBoard.Capacity;
            var drops = new byte[totalDrops];
            int idx = 0;
            for (int c = 1; c <= colors; c++)
                for (int i = 0; i < WaterSortBoard.Capacity; i++)
                    drops[idx++] = (byte)c;
            // Fisher-Yates 洗乱滴序
            for (int i = totalDrops - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (drops[i], drops[j]) = (drops[j], drops[i]);
            }

            var tubes = new List<int[]>(colors + 2);
            for (int t = 0; t < colors; t++)
            {
                var tube = new int[WaterSortBoard.Capacity];
                for (int d = 0; d < WaterSortBoard.Capacity; d++) tube[d] = drops[t * WaterSortBoard.Capacity + d];
                tubes.Add(tube);
            }
            tubes.Add(new int[0]); // 2 支空管
            tubes.Add(new int[0]);
            return new WaterSortBoard(colors, 2, tubes.ToArray());
        }
    }
}
