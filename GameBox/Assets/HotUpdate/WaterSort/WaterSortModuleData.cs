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

        /// <summary>
        /// 常规对局半局快照(单槽;二期入口直达配套):最近一盘「未完成的常规局」,进模块自动续玩。
        /// 通关/重开/全新开局即清(WaterSortProgressStore.ClearRun);每日挑战无半局语义不走本槽。
        /// </summary>
        public WaterSortRunData run;
    }

    /// <summary>
    /// 半局快照数据(与 WaterSortBoard 同构):盘面按管序拼接存储(每管自底向上,与棋盘索引一致),
    /// 恢复合法性由会话侧逐项校验(快照来自存档,按不可信输入对待;损坏 → 静默回全新开局)。
    /// 撤销历史不入快照(恢复后历史从当前盘重算);道具计数必须恢复,防续局重置造成经济漏洞。
    /// </summary>
    [Serializable]
    public sealed class WaterSortRunData
    {
        public int levelId;      // 所属关卡号(恢复时必须与开局关卡匹配)
        public int colors;       // 颜色数(须与关卡解码一致,会话侧校验)
        public int tubeCount;    // 总管数(≥ 色数 + 空管;加管道具 +1,撤销加管会回落)
        public List<int> tubeHeights = new List<int>(); // 各管非空格数(0~4,容量 WaterSortBoard.Capacity)
        public List<int> cells = new List<int>();       // 依管序拼接的自底向上色值(1..colors)
        public int moveCount;    // 已走步数(步数显示/结算文案)
        public int hintsUsed;    // 已购提示次数(消费型计数,恢复防重置)
        public int extraTubesUsed; // 已购额外空瓶次数(同上)
    }
}
