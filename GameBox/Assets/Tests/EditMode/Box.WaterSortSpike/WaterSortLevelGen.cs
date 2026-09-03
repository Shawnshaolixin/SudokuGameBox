using System;
using System.Collections.Generic;

namespace Box.WaterSortSpike
{
    /// <summary>
    /// 关卡生成器(Spike 版):随机构造合法混合板 + 玩家规则求解器验证可解性,不合格换种子重试。
    ///
    /// 为何不做"从终态反向洗牌":玩家规则下满管(4 滴同色)禁止倒入空管,终态无任何合法移动,
    /// 洗牌路径必然依赖"生成期放宽规则(满块可倒空管)"的不可逆步骤,洗出的板玩家规则下大量锁死。
    /// 因此采用正向方案——随机散射出混合板,再用玩家求解器验证,通过即收
    /// (19 文档 v0.4 WS-02:关卡全预生成,设备端无生成器;本 Spike 验证该正向路线的可解性指标)。
    /// </summary>
    public static class WaterSortLevelGen
    {
        /// <summary>
        /// 随机散射:每色 4 滴滴序整体洗乱后按 4 滴切管(恰 colors 个满管)+ 2 空管。
        /// 同色滴乱序后可能聚堆或分散,形成真实中间态;极低概率散射回"全满同色"终态(由 minSteps 过滤)。
        /// </summary>
        private static WaterSortBoard RandomScatter(int colors, Random rng)
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

        /// <summary>
        /// 生成一关:随机散射 + 玩家规则求解验证,要求"可解且步数 ≥ minSteps"(过滤散射回终态的废题)。
        /// 验证用 SolveAny(任意解,400ms 快筛):死局/极难局快速淘汰,不烧最优解验证的时间。
        /// steps 参数暂为占位——难度落档(WS-03 实测步数区间,≤3 色最优/高色代理)属正式生成器职责,
        /// 本 Spike 只验证"正向散射 + 任意解快筛"的路子可行;返回 null 表示多次尝试仍未过验证。
        /// </summary>
        public static WaterSortBoard Generate(int colors, int steps, int seed, int minSteps = 5)
        {
            var rng = new Random(seed);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var board = RandomScatter(colors, rng);
                var r = WaterSortSolver.SolveAny(board, 400);
                if (r.Solved && r.Steps >= minSteps) return board;
            }
            return null;
        }
    }
}
