using System.Collections.Generic;
using NUnit.Framework;
using WaterSort.Core;

namespace WaterSort.Core.Tests
{
    /// <summary>求解器正确性:小规模 BFS 精确对照 + 生成题可解性(由 Spike 迁移,用例语义零删改)。</summary>
    [TestFixture]
    public class WaterSortSolverTests
    {
        /// <summary>
        /// BFS 精确最短步数(测试 oracle 专用——产品面已不收 BFS,正式核心只留 IDA*/SolveAny)。
        /// 状态爆炸前仅用于 ≤4 色小规模对照。
        /// </summary>
        private static int SolveBfs(WaterSortBoard start)
        {
            var visited = new HashSet<string> { start.EncodeKey() };
            var queue = new Queue<WaterSortBoard>();
            queue.Enqueue(start);
            int steps = 0;
            while (queue.Count > 0)
            {
                int level = queue.Count;
                for (int i = 0; i < level; i++)
                {
                    var cur = queue.Dequeue();
                    if (cur.IsSolved()) return steps;
                    foreach (var m in cur.LegalMoves())
                    {
                        var next = cur.Apply(m);
                        if (visited.Add(next.EncodeKey())) queue.Enqueue(next);
                    }
                }
                steps++;
            }
            return -1; // 不可解(反向洗牌生成的题不会发生)
        }

        /// <summary>
        /// IDA* 最优步数与 BFS 精确值对照(验证启发式可采纳,3 色 10 题)。
        /// 收窄到 3 色的原因(Spike 结论):≥4 色最优解不可实时(4 色约 70% 超 3s),
        /// 自动化对照只保留能稳定实时完成的范围;更高色数路线见 19 文档 WS-02/WS-03(代理指标)。
        /// 落档区间 [5,100] 与 Spike 验收语义一致(minSteps=5 无上限):首解 ≤ boundCap=100 恒命中。
        /// </summary>
        [Test]
        public void Idastar_MatchesBfs_3Colors()
        {
            const int colors = 3;
            var spec = new WaterSortGenSpec { Difficulty = WaterSortDifficulty.Easy, MinColors = colors, MaxColors = colors, MinSteps = 5, MaxSteps = 100 };
            var mismatches = new List<string>();
            for (int s = 0; s < 10; s++)
            {
                var g = WaterSortLevelGen.Generate(spec, seed: 9000 + colors * 100 + s);
                Assert.IsTrue(g.Succeeded, $"{colors}色 s{s}:生成失败(未过可解性验证)");
                int bfs = SolveBfs(g.Board);
                var r = WaterSortSolver.SolveOptimal(g.Board, 5000);
                Assert.IsTrue(r.Solved, $"{colors}色 s{s}:IDA* 未解出");
                if (bfs != r.Steps)
                    mismatches.Add($"{colors}色 s{s}:BFS={bfs} IDA*={r.Steps}");
            }
            Assert.IsEmpty(mismatches, "IDA* 与 BFS 最优步数不一致:\n" + string.Join("\n", mismatches));
        }

        [Test]
        public void Generated10Color_AllPassSolvableCheck()
        {
            // Hard 档最坏规格(10 色 12 管):随机散射题应全部通过生成器内置的 SolveAny 可解性快筛。
            // 注意:不做 SolveOptimal 复核——最优解 10 色不可实时(Spike 结论),生成期验证 = SolveAny。
            var spec = new WaterSortGenSpec { Difficulty = WaterSortDifficulty.Hard, MinColors = 10, MaxColors = 10, MinSteps = 5, MaxSteps = 100 };
            for (int s = 0; s < 5; s++)
            {
                var g = WaterSortLevelGen.Generate(spec, seed: 5000 + s);
                Assert.IsTrue(g.Succeeded, $"10色 s{s}:生成失败(40 次重洗仍未过可解性验证)");
                Assert.IsFalse(g.MeasuredOptimal, "10 色落档应走代理深度而非精确最优");
            }
        }
    }
}
