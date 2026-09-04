using System;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 水排序运营配置默认表(19 文档 WS-06/07/08 运营表,M1.4「默认 + 覆盖」两层中的默认层):
    /// 提示单价与每关上限、额外空瓶单价与每关上限、首通奖励曲线(20~100 随关号递增)。
    /// 玩法代码一律经本类取值(零硬编码);字段故意非 readonly —— 覆盖层(M3 接 Game_WaterSort
    /// 组内配置文件)启动时整体改写即可生效,不给"默认值 + 覆盖值"双份源漂移的机会。
    /// 金额单位 = box.coins(盒内唯一货币,ISaveService.Coins;扣减/入账在视图层调用点,见 WaterSortView)。
    /// </summary>
    public static class WaterSortConfig
    {
        // 提示(WS-06):金币直购;激励视频分支 M3 复用同一按钮与上限计数,不另立字段
        public static int HintPriceCoins = 20;          // 单价(币/次)
        public static int HintLimitPerLevel = 3;        // 每关可购次数(StartLevel 复位,重开可再购、已付不退)
        public static int HintSolveTimeLimitMs = 400;   // 求解预算:SolveAny 首解(Spike 性能背书;低端机复测留 M3)

        // 额外空瓶(WS-06/13):+1 支空管(每关可购次数同上,天然上限 ≤2 次 → 盘面至多 +2 管)
        public static int ExtraTubePriceCoins = 40;     // 单价(币/次)
        public static int ExtraTubeLimitPerLevel = 2;   // 每关可购次数

        // 首通奖励(WS-08):仅首通发放(重玩/已解锁关卡通关不发),随关号线性递增至封顶
        public static int FirstWinRewardBase = 20;      // 第 1 关奖励
        public static int FirstWinRewardStep = 4;       // 每关递增步长
        public static int FirstWinRewardCap = 100;      // 奖励封顶(第 21 关起恒定 100)

        /// <summary>首通奖励 = Base + (关号-1) × Step,封顶 Cap;levelNo ≤ 0 按第 1 关计(防御)。</summary>
        public static int FirstWinReward(int levelNo)
        {
            int v = FirstWinRewardBase + (Math.Max(1, levelNo) - 1) * FirstWinRewardStep;
            return Math.Min(v, FirstWinRewardCap);
        }
    }
}
