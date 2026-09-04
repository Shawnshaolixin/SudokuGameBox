namespace WaterSort.Core
{
    /// <summary>
    /// 关卡序号 → 难度/生成规格默认编排表(19 文档 WS-03 默认参考值,M2.1 校准数据任务定版回写):
    /// 1~10 Easy / 11~40 Medium / 41~55 渐进带上半(承 Medium 尾)/ 56~70 渐进带下半(向 Hard 过渡)
    /// / 71+ Hard,色数与步窗随序号带整体上移,标签半程切 Hard。
    /// 步窗语义:≤3 色=IDA* 精确最优步数 / ≥4 色=SolveAny 首解深度代理(见 WaterSortGenSpec);
    /// 分位分布依据 19 文档附录 M2.1 校准表(每色实测中位 8/16/20/25/28/35/38/41,分布随色数单调右移)。
    /// M2.1 定版改动(数据驱动):41~55 色数上限 8→7、56~70 下限 6→7——8 色在 [20,34] 步窗覆盖率
    /// 仅 ~46%(期望 ~80 次散射才命中 1 关,MaxAttempts 内难产),归位 [25,42] 窗(覆盖率 ~95%);
    /// 6 色在 [25,42] 覆盖率仅 ~55%(半窗被拒),且 6 色已由 41~55 [20,34] 充分承接(覆盖率 ~90%)。
    /// 本表是"本地默认":代码内不散落档位数值,生成工具/校准任务(WS-03 AC)统一经本表取规格
    /// (数值本身仍是生成期默认,运行时不读)。
    /// </summary>
    public static class WaterSortGenDefaults
    {
        /// <summary>按关号取生成规格(区间含界;关号 ≤0 视作 1 兜底)。</summary>
        public static WaterSortGenSpec SpecForIndex(int levelNo)
        {
            if (levelNo <= 10) return Make(WaterSortDifficulty.Easy, 3, 4, 5, 15);
            if (levelNo <= 40) return Make(WaterSortDifficulty.Medium, 5, 7, 15, 30);
            if (levelNo <= 55) return Make(WaterSortDifficulty.Medium, 6, 7, 20, 34); // 渐进带上半:承 Medium 尾
            if (levelNo <= 70) return Make(WaterSortDifficulty.Hard, 7, 9, 25, 42);   // 渐进带下半:向 Hard 过渡
            return Make(WaterSortDifficulty.Hard, 8, 10, 30, 60);                     // 71+ 正式 Hard
        }

        /// <summary>按关号取落档标签(界面/结算/奖励曲线用;与 SpecForIndex 同源)。</summary>
        public static WaterSortDifficulty DifficultyForIndex(int levelNo) => SpecForIndex(levelNo).Difficulty;

        static WaterSortGenSpec Make(WaterSortDifficulty d, int minC, int maxC, int minS, int maxS)
        {
            return new WaterSortGenSpec
            {
                Difficulty = d,
                MinColors = minC,
                MaxColors = maxC,
                MinSteps = minS,
                MaxSteps = maxS,
            };
        }
    }
}
