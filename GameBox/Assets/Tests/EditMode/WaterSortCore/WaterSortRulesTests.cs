using System.Collections.Generic;
using NUnit.Framework;
using WaterSort.Core;

namespace WaterSort.Core.Tests
{
    /// <summary>规则正确性单测(由 Spike Box.WaterSortSpike.Tests 迁移正式化,M1.1,用例零删改)。</summary>
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
}
