using System;
using System.Collections.Generic;

namespace WaterSort.Core
{
    /// <summary>
    /// 每日挑战题库包(JSON 文件顶层包装,WS-09):levels = 按日期种子(yyyyMMdd)索引的正式条目,
    /// spares = 兜底备用池——某日期条目缺失/损坏时取库内备用(杜绝"全球同日死局")。
    /// 生成工具(编辑器/CLI)、运行时加载(WaterSortDailyLevelStore)与测试共用本结构;
    /// 条目复用 WaterSortLevelData:id = 日期种子,其余字段(colors/difficulty/measuredSteps/tubes)
    /// 与常规关同语义、同编解码(WaterSortLevelCodec),生成管线同一套(WS-02/WS-09)。
    /// </summary>
    [Serializable]
    public sealed class WaterSortDailyPack
    {
        public List<WaterSortLevelData> levels = new List<WaterSortLevelData>();   // 日期索引:id = yyyyMMdd
        public List<WaterSortLevelData> spares = new List<WaterSortLevelData>();   // 备用池:id 从 0 起占位
    }

    /// <summary>
    /// 日期种子工具(每日挑战共用,WS-09「按日期种子 UTC 0 点换题、全球同日同题」):
    /// seed = yyyyMMdd 整数(照数独 DailyChallengeStore.SeedFor 口径),仅依赖 System 纯计算,
    /// 编辑器生成工具/运行时/测试三侧共用,单一实现防两侧口径漂移。
    /// </summary>
    public static class WaterSortDailySeed
    {
        /// <summary>日期 → 种子(yyyyMMdd;UTC 语义由调用方保证,本方法只做整形换算)。</summary>
        public static int SeedOf(DateTime date) => date.Year * 10000 + date.Month * 100 + date.Day;

        /// <summary>种子 → 日期(与 SeedOf 互逆;非合法日历日期直抛 ArgumentOutOfRangeException——调用方兜底)。</summary>
        public static DateTime DateOf(int seed)
        {
            return new DateTime(seed / 10000, seed / 100 % 100, seed % 100);
        }

        /// <summary>前一自然日种子(Streak 逆推用;跨月/跨年由 DateTime 加法正确折算)。</summary>
        public static int PrevSeedOf(int seed) => SeedOf(DateOf(seed).AddDays(-1));
    }
}
