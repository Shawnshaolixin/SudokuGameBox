using System;
using System.Collections.Generic;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 水排序模块存档分区(11 文档 §8.1 modules.watersort,D-7;ISaveService 加密落盘)。
    /// 只存「进度」类数据:firstWinLevels = 各关首通集合(关卡编号 1 起,JsonUtility 不支持
    /// HashSet,故用 List 存集合语义)。
    /// 派生量一律不落盘(解锁数 = 自 1 起连续首通数、某关是否已首通),由 WaterSortProgressStore
    /// 从本集合推出 —— 首通是解锁与发奖的唯一推进信号,单一数据源防双写不一致
    /// (WS-04:重玩老关不推进、不给奖)。
    /// </summary>
    [Serializable]
    public sealed class WaterSortModuleData
    {
        /// <summary>首通关编号(乱序可;查重/推进逻辑见 ProgressStore)。</summary>
        public List<int> firstWinLevels = new List<int>();

        /// <summary>
        /// 每日挑战已完成日期种子(yyyyMMdd,乱序可;M2.3 新增)。
        /// Streak/今日是否完成均由本集合即时推导(WaterSortDailyStore),单一数据源不落派生量;
        /// 补签/每日最佳步数等增值字段不在 M2 范围,后续扩展走同一集合或另立列表,勿存派生状态。
        /// </summary>
        public List<int> dailyDoneSeeds = new List<int>();
    }
}
