using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace Box.WaterSortSpike.Tests
{
    /// <summary>规则正确性单测(不依赖引擎,后续可随正式实现迁移)。</summary>
    [TestFixture]
    public class WaterSortRulesTests
    {
        /// <summary>构造 3 色 5 管棋盘:前 3 管混合/纯色,后 2 管为空。</summary>
        private static WaterSortBoard Board3(params int[][] tubes) => new WaterSortBoard(3, 2, tubes);

        private static bool HasMove(List<WaterSortMove> moves, int src, int dst, int count)
        {
            foreach (var m in moves)
                if (m.Src == src && m.Dst == dst && m.Count == count) return true;
            return false;
        }

        [Test]
        public void SolvedBoard_IsSolved() => Assert.IsTrue(new WaterSortBoard(3, 2).IsSolved());

        [Test]
        public void MixedTube_NotSolved() =>
            Assert.IsFalse(Board3(new[] { 1, 1, 1, 2 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3, 3 }).IsSolved());

        [Test]
        public void EmptyAndFullMixed_NotSolved() =>
            Assert.IsFalse(Board3(new[] { 1, 1, 1, 1 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3 }, new[] { 3 }).IsSolved());

        [Test]
        public void PourIntoEmpty_Ok()
        {
            // 管0 顶块 [2] 倒入空管3:整块全倒,管0 剩 3 滴颜色 1
            var board = Board3(new[] { 1, 1, 1, 2 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3, 3 });
            var after = board.Apply(new WaterSortMove(0, 3, 1));
            Assert.AreEqual(1, after.TopColor(0));
            Assert.AreEqual(3, after.TopCount(0));
            Assert.AreEqual(2, after.Get(3, 0));
            Assert.AreEqual(1, after.TopCount(3));
        }

        [Test]
        public void PourSameColor_Ok()
        {
            // 合法板(每色恰 4 滴):颜色1 = 管0×3 + 管3×1,颜色2 = 管0×1 + 管1×3,颜色3 完满
            // 两步聚合:管0 顶 2 → 管1(同色补满);管0 的 [1,1,1] → 管3([1] 同色补齐) → 全场聚合
            var board = Board3(new[] { 1, 1, 1, 2 }, new[] { 2, 2, 2 }, new[] { 3, 3, 3, 3 }, new[] { 1 });
            Assert.IsTrue(HasMove(board.LegalMoves(), 0, 1, 1), "同色顶不满管应可倒入");
            var after1 = board.Apply(new WaterSortMove(0, 1, 1));
            Assert.AreEqual(4, after1.TopCount(1)); // 管1 聚合完成
            Assert.AreEqual(1, after1.TopColor(0)); // 管0 顶部露出颜色1
            Assert.IsTrue(HasMove(after1.LegalMoves(), 0, 3, 3), "同色块应可补齐剩余空位");
            Assert.IsTrue(after1.Apply(new WaterSortMove(0, 3, 3)).IsSolved());
        }

        [Test]
        public void PartialPour_WhenCapLessThanRun()
        {
            // 源管顶块长 3,目标只剩 1 位 → 只倒 1 滴,源管留 2 滴
            var board = Board3(new[] { 2, 2, 2 }, new[] { 1, 1, 2 }, new[] { 3, 3, 3, 3 });
            var moves = board.LegalMoves();
            Assert.IsTrue(HasMove(moves, 0, 1, 1));
            var after = board.Apply(new WaterSortMove(0, 1, 1));
            Assert.AreEqual(2, after.TopCount(0));
            Assert.AreEqual(2, after.Get(1, 3));
        }

        [Test]
        public void FullTube_HasNoLegalMove()
        {
            // 满块(4 滴同色)禁倒空管 + 目标无同色不满管可补 → 合法板中满管永无合法移动。
            // 推论:终态(全满管 + 空管)零合法移动,反向洗牌从终态动不起来(见 WaterSortLevelGen 注释)。
            var board = Board3(new[] { 1, 1, 1, 1 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3, 3 });
            foreach (var m in board.LegalMoves())
                Assert.Fail($"终态不应有任何合法移动,实际存在: {m}");
        }

        [Test]
        public void TopRun_EdgeCases()
        {
            var board = Board3(new[] { 1, 2, 2, 3 }, new[] { 2, 2, 2, 2 }, new int[0], new int[0]);
            Assert.AreEqual(1, board.TopRun(0)); // 顶部 3 下面不同色 → 块长 1
            Assert.AreEqual(4, board.TopRun(1)); // 整管同色 → 块长 4
            Assert.AreEqual(0, board.TopRun(3)); // 空管 → 0
        }

        [Test]
        public void EncodeKey_Stable()
        {
            var a = Board3(new[] { 1, 1, 1, 2 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3, 3 });
            var b = Board3(new[] { 1, 1, 1, 2 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3, 3 });
            var c = Board3(new[] { 1, 1, 1, 1 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3, 3 });
            Assert.AreEqual(a.EncodeKey(), b.EncodeKey());
            Assert.AreNotEqual(a.EncodeKey(), c.EncodeKey());
        }
    }

    /// <summary>求解器正确性:小规模 BFS 精确对照 + 生成题可解性。</summary>
    [TestFixture]
    public class WaterSortSolverTests
    {
        /// <summary>
        /// IDA* 最优步数与 BFS 精确值对照(验证启发式可采纳,3 色 10 题)。
        /// 收窄到 3 色的原因(Spike 结论):≥4 色最优解不可实时(4 色约 70% 超 3s),
        /// 自动化对照只保留能稳定实时完成的范围;更高色数路线见 19 文档 WS-02/WS-03(代理指标)。
        /// </summary>
        [Test]
        public void Idastar_MatchesBfs_3Colors()
        {
            const int colors = 3;
            var mismatches = new List<string>();
            for (int s = 0; s < 10; s++)
            {
                var board = WaterSortLevelGen.Generate(colors, colors * 8, seed: 9000 + colors * 100 + s);
                Assert.IsNotNull(board, $"{colors}色 s{s}:生成失败(未过可解性验证)");
                int bfs = WaterSortSolver.SolveBfs(board);
                var r = WaterSortSolver.SolveOptimal(board, 5000);
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
            for (int s = 0; s < 5; s++)
            {
                var board = WaterSortLevelGen.Generate(10, 80, seed: 5000 + s);
                Assert.IsNotNull(board, $"10色 s{s}:生成失败(40 次重洗仍未过可解性验证)");
            }
        }
    }

    /// <summary>Spike 数据记录:各色数 IDA* 最优解性能表(结论人工判读,见 19 文档 §10 #1)。</summary>
    [TestFixture]
    public class WaterSortSpikePerfTests
    {
        /// <summary>
        /// 性能表(数据记录型,不断言):3~4 色各 10 题、5~10 色各 3 题,IDA* 求最优解计时。
        /// Spike 结论(2026-09-03 样本 10):3 色均值 162ms / 最大 617ms 可实时;
        /// 4 色约 70% 超 3s;≥5 色全部超时 → 最优解仅 ≤3 色适用(19 文档 WS-02/WS-03 已据此定稿)。
        /// 降样本:高色数每题必然烧满 3s 超时,减到 3 题即可记录"全超时"的结构结论,控制回归耗时。
        /// </summary>
        [Test, Timeout(300000)]
        public void PerfTable_3To10Colors()
        {
            const int timeLimitMs = 3000; // 单题求解限时
            var sb = new StringBuilder();
            sb.AppendLine("=== WaterSort Spike 性能表(Editor, IDA* 最优解,数据记录型)===");
            sb.AppendLine("色数  管数  样本  平均ms  最大ms  平均步数  超时(3s)");

            for (int colors = 3; colors <= 10; colors++)
            {
                int samples = colors <= 4 ? 10 : 3;
                long totalMs = 0, maxMs = 0;
                long totalSteps = 0;
                int timedOut = 0;
                for (int s = 0; s < samples; s++)
                {
                    var board = WaterSortLevelGen.Generate(colors, colors * 8, seed: 1000 * colors + s);
                    Assert.IsNotNull(board, $"{colors}色 s{s}:生成失败(未过可解性验证)");
                    var r = WaterSortSolver.SolveOptimal(board, timeLimitMs);
                    if (r.TimedOut) { timedOut++; continue; }
                    Assert.IsTrue(r.Solved, $"{colors}色 s{s}:未解出");
                    totalMs += r.ElapsedMs;
                    if (r.ElapsedMs > maxMs) maxMs = r.ElapsedMs;
                    totalSteps += r.Steps;
                }
                int done = samples - timedOut;
                double avgMs = totalMs / (double)Math.Max(1, done);
                double avgSteps = totalSteps / (double)Math.Max(1, done);
                sb.AppendLine($"{colors,3}  {colors + 2,4}  {done,4}  {avgMs,7:F0}  {maxMs,6}  {avgSteps,8:F1}  {timedOut,6}");
            }
            sb.AppendLine();
            sb.AppendLine("结论判读:3 色可实时 → 最优解仅用于 ≤3 色精控;4+ 色高超时 → 高色数走 SolveAny 代理指标(19 文档 WS-03)");
            TestContext.Progress.WriteLine(sb.ToString());
            TestContext.WriteLine(sb.ToString());
        }
    }
}
