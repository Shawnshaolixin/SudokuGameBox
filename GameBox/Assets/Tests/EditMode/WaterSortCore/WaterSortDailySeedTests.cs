using System;
using NUnit.Framework;

namespace WaterSort.Core.Tests
{
    /// <summary>
    /// 每日种子纯计算用例(M2.3,WS-09):yyyyMMdd 整形种子与日期互逆 + 跨月/跨年/闰年逆推。
    /// WaterSortDailySeed 仅依赖 System(三侧共用:编辑器生成工具/运行时/本组),口径漂移即违约。
    /// </summary>
    public class WaterSortDailySeedTests
    {
        // ---- SeedOf:日期 → yyyyMMdd ----

        [Test]
        public void SeedOf_FormatsYyyyMmDd()
        {
            Assert.AreEqual(20260822, WaterSortDailySeed.SeedOf(new DateTime(2026, 8, 22)));
            Assert.AreEqual(20261231, WaterSortDailySeed.SeedOf(new DateTime(2026, 12, 31)));
            Assert.AreEqual(20270101, WaterSortDailySeed.SeedOf(new DateTime(2027, 1, 1)), "跨年不得进位混乱");
        }

        // ---- DateOf:与 SeedOf 互逆(UTC 0 点换题语义,纯日期) ----

        [Test]
        public void DateOf_And_SeedOf_AreInverse()
        {
            var dates = new[]
            {
                new DateTime(2026, 8, 1),
                new DateTime(2026, 8, 31),
                new DateTime(2026, 9, 1),
                new DateTime(2026, 12, 31),
                new DateTime(2027, 1, 1),
                new DateTime(2028, 2, 29), // 闰日
            };
            foreach (var d in dates)
            {
                int seed = WaterSortDailySeed.SeedOf(d);
                Assert.AreEqual(d, WaterSortDailySeed.DateOf(seed), $"种子 {seed} 回退日期不一致");
            }
        }

        [Test]
        public void DateOf_InvalidSeed_Throws()
        {
            // 月 13/日 32 均非合法日历日期:乱码种子不得静默归一(上游兜底路径依赖此抛错);
            // DateTime 月份/日构造直抛 ArgumentOutOfRangeException(见 WaterSortDailySeed.DateOf 契约)
            Assert.Throws<ArgumentOutOfRangeException>(() => WaterSortDailySeed.DateOf(20261301));
            Assert.Throws<ArgumentOutOfRangeException>(() => WaterSortDailySeed.DateOf(20260132));
        }

        // ---- PrevSeedOf:Streak 逆推步进(跨月/跨年/闰年由 DateTime 加法折算) ----

        [Test]
        public void PrevSeedOf_SameMonth()
        {
            Assert.AreEqual(20260904, WaterSortDailySeed.PrevSeedOf(20260905));
        }

        [Test]
        public void PrevSeedOf_CrossesMonthBoundary()
        {
            Assert.AreEqual(20260831, WaterSortDailySeed.PrevSeedOf(20260901), "跨月逆推");
        }

        [Test]
        public void PrevSeedOf_CrossesYearBoundary()
        {
            Assert.AreEqual(20251231, WaterSortDailySeed.PrevSeedOf(20260101), "跨年逆推");
        }

        [Test]
        public void PrevSeedOf_HandlesLeapFebruary()
        {
            // 2028 为闰年:3/1 的前一天是 2/29;2026 非闰年:3/1 的前一天是 2/28
            Assert.AreEqual(20280229, WaterSortDailySeed.PrevSeedOf(20280301));
            Assert.AreEqual(20260228, WaterSortDailySeed.PrevSeedOf(20260301));
        }
    }
}
