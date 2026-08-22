using System;
using UnityEngine;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 每日挑战进度存储(Phase 4 完整版:临时 PlayerPrefs)。
    /// ⚠️ 明确临时态:Phase 5 存档系统(v0→v1 迁移器)落地后,完成标记/最佳秒数迁入正式存档。
    /// </summary>
    public static class DailyChallengeStore
    {
        const string DonePrefix = "sudoku.daily.done.";
        const string BestPrefix = "sudoku.daily.best.";

        /// <summary>日期种子:yyyyMMdd 整数,同日同题(UTC 日期)。</summary>
        public static int SeedFor(DateTime date) => date.Year * 10000 + date.Month * 100 + date.Day;

        public static bool IsCompleted(int seed) => PlayerPrefs.GetInt(DonePrefix + seed, 0) == 1;

        public static void MarkCompleted(int seed) => PlayerPrefs.SetInt(DonePrefix + seed, 1);

        /// <summary>最佳秒数(0=尚未完成过)。</summary>
        public static int GetBestSeconds(int seed) => PlayerPrefs.GetInt(BestPrefix + seed, 0);

        /// <summary>仅更新更优成绩(更短秒数)。</summary>
        public static void SetBestSeconds(int seed, int seconds)
        {
            int best = GetBestSeconds(seed);
            if (best == 0 || seconds < best)
                PlayerPrefs.SetInt(BestPrefix + seed, seconds);
        }
    }
}
