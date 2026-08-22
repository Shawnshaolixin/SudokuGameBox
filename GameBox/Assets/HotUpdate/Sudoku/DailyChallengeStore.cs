using System;
using Box.Services;
using UnityEngine;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 每日挑战进度存储(Phase 5 正式版):走 ISaveService 的 modules.sudoku 分区,加密落盘(§8.1 D-7)。
    /// v0→v1 迁移(惰性按 seed):旧 PlayerPrefs 键 sudoku.daily.done.&lt;seed&gt;/sudoku.daily.best.&lt;seed&gt;
    /// 在首次读/写该 seed 时迁入分区;旧键保留一个版本以便回滚(§8.2)。
    /// 旧键无法全量枚举(seed 非连续日日递增),故按访问到的 seed 逐个迁移,效果等价。
    /// </summary>
    public static class DailyChallengeStore
    {
        const string DonePrefix = "sudoku.daily.done.";
        const string BestPrefix = "sudoku.daily.best.";
        const string ModuleId = "sudoku";

        /// <summary>日期种子:yyyyMMdd 整数,同日同题(UTC 日期)。</summary>
        public static int SeedFor(DateTime date) => date.Year * 10000 + date.Month * 100 + date.Day;

        public static bool IsCompleted(int seed)
        {
            var data = Load(seed);
            var e = Find(data, seed);
            return e != null && e.done;
        }

        public static void MarkCompleted(int seed)
        {
            var data = Load(seed);
            var e = Find(data, seed, true);
            e.done = true;
            Save(data);
        }

        /// <summary>最佳秒数(0=尚未完成过)。</summary>
        public static int GetBestSeconds(int seed)
        {
            var data = Load(seed);
            var e = Find(data, seed);
            return e != null ? e.bestSeconds : 0;
        }

        /// <summary>仅更新更优成绩(更短秒数)。</summary>
        public static void SetBestSeconds(int seed, int seconds)
        {
            var data = Load(seed);
            var e = Find(data, seed, true);
            if (e.bestSeconds == 0 || seconds < e.bestSeconds)
            {
                e.bestSeconds = seconds;
                Save(data);
            }
        }

        // ---- 内部 ----

        /// <summary>读分区 + 惰性迁移该 seed(v0 旧键仍在 PlayerPrefs 时先搬入分区)。</summary>
        static SudokuModuleData Load(int seed)
        {
            var data = ServiceLocator.Save != null
                ? ServiceLocator.Save.GetModule<SudokuModuleData>(ModuleId)
                : new SudokuModuleData(); // 服务未注册(异常上下文):用空数据,不抛
            MigrateSeed(data, seed);
            return data;
        }

        static void Save(SudokuModuleData data)
        {
            ServiceLocator.Save?.SetModule(ModuleId, data); // 内部加密落盘;null 时跳过(异常上下文)
        }

        static SudokuModuleData.DailyEntry Find(SudokuModuleData data, int seed, bool create = false)
        {
            foreach (var e in data.daily)
                if (e.seed == seed) return e;
            if (!create) return null;
            var n = new SudokuModuleData.DailyEntry { seed = seed };
            data.daily.Add(n);
            return n;
        }

        /// <summary>
        /// v0→v1 惰性迁移单个 seed:分区已有该 seed 记录则不覆盖(已迁移/新数据为权威);
        /// 否则把 PlayerPrefs 旧键(done/best)搬入分区并落盘,旧键保留(§8.2 回滚)。
        /// 幂等:重复调用无副作用。
        /// </summary>
        static void MigrateSeed(SudokuModuleData data, int seed)
        {
            if (Find(data, seed) != null) return; // 分区已有 → 不覆盖

            int done = PlayerPrefs.GetInt(DonePrefix + seed, -1);
            int best = PlayerPrefs.GetInt(BestPrefix + seed, -1);
            if (done == -1 && best == -1) return; // 无旧键(新玩家)

            var e = new SudokuModuleData.DailyEntry
            {
                seed = seed,
                done = done == 1,
                bestSeconds = best > 0 ? best : 0,
            };
            data.daily.Add(e);
            Save(data); // 迁入即落盘;旧键保留,回滚用
        }
    }
}
