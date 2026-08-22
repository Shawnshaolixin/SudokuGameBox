using System;
using System.Collections.Generic;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 数独模块存档分区(11 文档 §8.1 modules.sudoku,D-7)。
    /// 只存「进度/统计」类数据:每日挑战完成标记与最佳秒数(daily 列表,seed 唯一)。
    /// 偏好(音量/语言/主题)与去广告购买标记留在 PlayerPrefs(§8.1 规定;真 IAP Phase 7 接入再评估)。
    /// JsonUtility 不支持 Dictionary,故用 List + 手工查找(条目极少,每日一条)。
    /// </summary>
    [Serializable]
    public sealed class SudokuModuleData
    {
        public List<DailyEntry> daily = new List<DailyEntry>();

        /// <summary>一日挑战一条:seed=yyyyMMdd 日期种子(UTC)。</summary>
        [Serializable]
        public sealed class DailyEntry
        {
            public int seed;
            public bool done;
            public int bestSeconds;
        }
    }
}