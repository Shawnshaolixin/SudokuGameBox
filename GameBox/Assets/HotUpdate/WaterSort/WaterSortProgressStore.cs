using System.Collections.Generic;
using Box.Services;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 水排序进度仓储(模式照 DailyChallengeStore:静态读写 ISaveService 的 "watersort" 分区)。
    /// 解锁规则(WS-04):可玩关号 ≤ 解锁数 + 1;解锁数 = 自 1 起连续首通的最大编号。
    /// 例:{1,3,4} → 解锁数 1(2 未通,2/3/4 虽已通也不前置放行);
    ///   {1,2,5} → 解锁数 2。关卡编号连续递增是题库生成约定(常规关 1..N)。
    /// 首通 = 集合新增;重复过关 = 集合已含,不落盘、不推进(重玩仅给娱乐价值)。
    /// </summary>
    public static class WaterSortProgressStore
    {
        public const string ModuleId = "watersort";

        /// <summary>读分区;服务未注册(异常上下文)返回空数据不抛(照 DailyChallengeStore)。</summary>
        public static WaterSortModuleData Load()
        {
            return ServiceLocator.Save != null
                ? ServiceLocator.Save.GetModule<WaterSortModuleData>(ModuleId)
                : new WaterSortModuleData();
        }

        static void Save(WaterSortModuleData data)
        {
            ServiceLocator.Save?.SetModule(ModuleId, data); // 内部加密落盘;null 时跳过
        }

        /// <summary>自 1 起连续首通数量(即已解锁的"已完成"关数,不含当前前沿关)。</summary>
        public static int UnlockedCount(WaterSortModuleData data)
        {
            if (data?.firstWinLevels == null || data.firstWinLevels.Count == 0) return 0;
            var won = new HashSet<int>(data.firstWinLevels); // 集合查重 O(1);数据量 ≤ 数百,代价可忽略
            int n = 0;
            while (won.Contains(n + 1)) n++;
            return n;
        }

        /// <summary>该关是否已首通过(结算发币依据:仅首通发奖,WS-04)。</summary>
        public static bool IsCleared(WaterSortModuleData data, int levelNo)
        {
            if (data?.firstWinLevels == null) return false;
            for (int i = 0; i < data.firstWinLevels.Count; i++)
                if (data.firstWinLevels[i] == levelNo) return true;
            return false;
        }

        /// <summary>
        /// 记录首通:此前未通 → 入集合并落盘,返回 true(推进信号);
        /// 已通(重玩)→ 返回 false,不重复落盘。调用方(结算面板)据返回值决定"下一关"是否放行。
        /// </summary>
        public static bool RecordFirstWin(int levelNo)
        {
            var data = Load();
            if (IsCleared(data, levelNo)) return false;
            data.firstWinLevels.Add(levelNo);
            Save(data);
            return true;
        }
    }
}
