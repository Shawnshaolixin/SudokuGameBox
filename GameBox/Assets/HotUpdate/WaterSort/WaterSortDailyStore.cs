using System;
using System.Collections.Generic;
using Box.Services;
using WaterSort.Core;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 水排序每日挑战进度仓储(WS-09):走 ISaveService 的 "watersort" 分区(与 WaterSortProgressStore
    /// 同分区,模块数据类同一份——分区 id 常量、读空不抛口径全同,见类头)。
    /// 只存「已完成日期种子」集合(dailyDoneSeeds);今日是否完成、连续天数 Streak 均为纯推导,
    /// 单一数据源,无派生落盘(与首通解锁同一设计哲学)。
    /// 日期接缝:UtcNow 可注入(默认 DateTime.UtcNow;M2 验收"可玩任意日期每日关"经此换日期,
    /// 生产代码/测试共用),种子 = yyyyMMdd(WaterSortDailySeed,UTC 0 点换题、全球同日同题)。
    /// Streak 语义(标准每日挑战口径):今天已完成 → 含今天往前数连续完成天数;
    /// 今天未完成 → 从昨天往前数(昨天及以前连续仍成立,今天的"断更"还没发生);
    /// 中途有缺失日 → 归零。补签不在 M2 范围。
    /// </summary>
    public static class WaterSortDailyStore
    {
        /// <summary>当前日期提供器(UTC;测试注入任意日期验证跨日/跨月 Streak 与取关)。</summary>
        public static Func<DateTime> UtcNow = () => DateTime.UtcNow;

        public static int TodaySeed() => WaterSortDailySeed.SeedOf(UtcNow());

        /// <summary>读分区;服务未注册(异常上下文)返回空数据不抛(照 ProgressStore)。</summary>
        public static WaterSortModuleData Load()
        {
            return ServiceLocator.Save != null
                ? ServiceLocator.Save.GetModule<WaterSortModuleData>(WaterSortProgressStore.ModuleId)
                : new WaterSortModuleData();
        }

        static void Save(WaterSortModuleData data)
        {
            ServiceLocator.Save?.SetModule(WaterSortProgressStore.ModuleId, data); // 内部加密落盘
        }

        static bool Contains(WaterSortModuleData data, int seed)
        {
            if (data?.dailyDoneSeeds == null) return false;
            for (int i = 0; i < data.dailyDoneSeeds.Count; i++)
                if (data.dailyDoneSeeds[i] == seed) return true;
            return false;
        }

        /// <summary>该日期是否已完成(幂等查询;不做任何写入)。</summary>
        public static bool IsDone(int seed)
        {
            return Contains(Load(), seed);
        }

        /// <summary>
        /// 标记完成:此前未完成 → 入集合并落盘,返回 true(首次完成信号);
        /// 已标记(当日重玩)→ 返回 false 不重复落盘。结算按返回值决定是否播首次完成反馈(若有)。
        /// </summary>
        public static bool MarkDone(int seed)
        {
            var data = Load();
            if (Contains(data, seed)) return false;
            data.dailyDoneSeeds.Add(seed);
            Save(data);
            return true;
        }

        /// <summary>
        /// 连续完成天数(截至 today,含今天的完成才算今天链;见类头 Streak 语义)。
        /// 纯推导便于测试:不依赖 Save 服务,直接吃分区数据。
        /// </summary>
        public static int Streak(WaterSortModuleData data, DateTime today)
        {
            var done = data?.dailyDoneSeeds;
            if (done == null || done.Count == 0) return 0;
            var set = new HashSet<int>(done);
            int cursor = WaterSortDailySeed.SeedOf(today);
            if (!set.Contains(cursor))
                cursor = WaterSortDailySeed.PrevSeedOf(cursor); // 今日未完成:从昨天起算
            int n = 0;
            while (set.Contains(cursor))
            {
                n++;
                cursor = WaterSortDailySeed.PrevSeedOf(cursor);
            }
            return n;
        }
    }
}
