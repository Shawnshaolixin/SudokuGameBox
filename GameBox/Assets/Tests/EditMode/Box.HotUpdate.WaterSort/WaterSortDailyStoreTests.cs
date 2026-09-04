using System;
using Box.Services;
using NUnit.Framework;
using UnityEngine;
using WaterSort.Core;

namespace Box.HotUpdate.WaterSort.Tests
{
    /// <summary>
    /// 每日挑战仓储用例(M2.3,WS-09):完成标记落 watersort 分区(单次落盘信号)/Streak 纯推导
    /// (今日完成含今天、未完成从昨天起算、断更归零、跨月折算)/日期接缝 UtcNow 注入(可玩任意日期)
    /// /每日题库兜底取关(精确命中优先、缺失确定性取备用、备用池空返回 null)。
    /// 全走 FakeSaveService 内存分区 + 显式注入日期——不依赖真实时钟与壳层落盘。
    /// </summary>
    public class WaterSortDailyStoreTests
    {
        // 固定的"今天"(避免用例随真实日期漂移:断言只依赖注入值,生产换日不破用例)
        static readonly DateTime Today = new DateTime(2026, 9, 5);

        FakeSaveService _save;
        Func<DateTime> _origUtcNow;

        [SetUp]
        public void SetUp()
        {
            _save = new FakeSaveService();
            ServiceLocator.Register(_save, new FakeSettingsService());
            _origUtcNow = WaterSortDailyStore.UtcNow;
            WaterSortDailyStore.UtcNow = () => Today; // 日期接缝:全组用例锁在今天
        }

        [TearDown]
        public void TearDown()
        {
            WaterSortDailyStore.UtcNow = _origUtcNow; // 还原接缝(静态,防污染他组)
            ServiceLocator.Reset();                    // 隔离:不污染其它测试
        }

        // ---- 完成标记(分区落盘语义) ----

        [Test]
        public void MarkDone_FirstTime_ReturnsTrue_AndPersistsToPartition()
        {
            Assert.IsFalse(WaterSortDailyStore.IsDone(20260905), "默认未完成");
            Assert.IsTrue(WaterSortDailyStore.MarkDone(20260905), "首次完成必须返回 true(首成信号)");
            Assert.IsTrue(WaterSortDailyStore.IsDone(20260905));

            // 落盘断言:JSON 反序列化读回(分区只存种子集合,JsonUtility List 序列化)
            var raw = _save.RawModuleJson(WaterSortProgressStore.ModuleId);
            Assert.IsNotNull(raw, "MarkDone 必须经 SetModule 落 watersort 分区");
            var data = JsonUtility.FromJson<WaterSortModuleData>(raw);
            Assert.AreEqual(1, data.dailyDoneSeeds.Count);
            Assert.AreEqual(20260905, data.dailyDoneSeeds[0]);
        }

        [Test]
        public void MarkDone_AlreadyDone_ReturnsFalse_NoDuplicate()
        {
            WaterSortDailyStore.MarkDone(20260905);
            Assert.IsFalse(WaterSortDailyStore.MarkDone(20260905), "重复完成不得再次给首成信号");
            var data = _save.GetModule<WaterSortModuleData>(WaterSortProgressStore.ModuleId);
            Assert.AreEqual(1, data.dailyDoneSeeds.Count, "重复落盘不得产生重复种子");
        }

        [Test]
        public void MarkDone_OtherDay_KeepsOwnSeed()
        {
            WaterSortDailyStore.MarkDone(20260904); // 昨天完成
            Assert.IsFalse(WaterSortDailyStore.IsDone(20260905), "昨日完成不影响今日判定");
        }

        [Test]
        public void TodaySeed_UsesUtcNowSeam()
        {
            // 日期接缝(M2 验收「可玩任意日期每日关」):注入何日即取何日种子
            Assert.AreEqual(20260905, WaterSortDailyStore.TodaySeed());
            WaterSortDailyStore.UtcNow = () => new DateTime(2026, 10, 1);
            Assert.AreEqual(20261001, WaterSortDailyStore.TodaySeed());
        }

        // ---- Streak:纯推导(数据 + 今日日期,无服务依赖) ----

        static WaterSortModuleData Done(params int[] seeds)
        {
            var data = new WaterSortModuleData();
            data.dailyDoneSeeds.AddRange(seeds);
            return data;
        }

        [Test]
        public void Streak_NoDoneSeeds_Zero()
        {
            Assert.AreEqual(0, WaterSortDailyStore.Streak(new WaterSortModuleData(), Today));
            Assert.AreEqual(0, WaterSortDailyStore.Streak(null, Today), "空数据不得抛");
        }

        [Test]
        public void Streak_TodayDone_CountsFromToday()
        {
            var data = Done(20260905, 20260904, 20260903);
            Assert.AreEqual(3, WaterSortDailyStore.Streak(data, Today));
        }

        [Test]
        public void Streak_TodayNotDone_StartsFromYesterday()
        {
            // 今天还没玩:昨天及以前的连续仍成立("今天断更"尚未发生)
            var data = Done(20260904, 20260903, 20260902);
            Assert.AreEqual(3, WaterSortDailyStore.Streak(data, Today));
        }

        [Test]
        public void Streak_TodayNotDone_GapBreaksToZero_UnlessYesterdayDone()
        {
            Assert.AreEqual(0, WaterSortDailyStore.Streak(Done(20260901), Today), "前天完成、中间断更:链断归零");
            var data = Done(20260904, 20260902); // 昨日完成但 9/3 断:只算 1
            Assert.AreEqual(1, WaterSortDailyStore.Streak(data, Today));
        }

        [Test]
        public void Streak_CrossesMonthBoundary()
        {
            // 8/30→8/31→9/1→9/2 连续完成(跨月由 PrevSeedOf 折算,2026 年 9 月前无 2/29 陷阱)
            var today = new DateTime(2026, 9, 2);
            var data = Done(20260902, 20260901, 20260831, 20260830);
            Assert.AreEqual(4, WaterSortDailyStore.Streak(data, today));
        }

        // ---- 每日题库取关(WaterSortDailyLevelStore.GetForSeed,纯函数无状态) ----

        static WaterSortLevelData Level(int id)
        {
            // 终态盘面恒为 2 空管(玩家规则可玩性不变量):ctor(colors, emptyTubes=2)
            var board = new WaterSortBoard(2, 2); // 仅作解码/编码往返载体,本组不求解
            return WaterSortLevelCodec.Encode(board, id, WaterSortDifficulty.Easy, 0);
        }

        [Test]
        public void GetForSeed_ExactId_HitsLevel_NoFallback()
        {
            var pack = new WaterSortDailyPack();
            pack.levels.Add(Level(20260905));
            pack.spares.Add(Level(0));

            var got = WaterSortDailyLevelStore.GetForSeed(pack, 20260905, out bool usedFallback);
            Assert.AreEqual(20260905, got.id);
            Assert.IsFalse(usedFallback, "精确命中不得走兜底");
        }

        [Test]
        public void GetForSeed_MissingId_UsesDeterministicSpare()
        {
            var pack = new WaterSortDailyPack();
            pack.levels.Add(Level(20260101)); // 库内无 20260905
            pack.spares.Add(Level(100));
            pack.spares.Add(Level(200));
            pack.spares.Add(Level(300));

            var a = WaterSortDailyLevelStore.GetForSeed(pack, 20260905, out bool used);
            var b = WaterSortDailyLevelStore.GetForSeed(pack, 20260905, out used);
            Assert.IsTrue(used, "缺失必须标记兜底(埋点/调试用)");
            Assert.AreSame(a, b, "同日期两次取关必须同条目(确定性)");
            Assert.AreEqual(20260905 % 3, a.id % 100, "兜底索引 = seed % 备用池数");
        }

        [Test]
        public void GetForSeed_EmptySpares_ReturnsNull()
        {
            var pack = new WaterSortDailyPack();
            pack.levels.Add(Level(20260101));
            Assert.IsNull(WaterSortDailyLevelStore.GetForSeed(pack, 20260905, out bool used),
                "备用池空且无精确命中:资产异常,返回 null 由调用方降级");
        }
    }
}
