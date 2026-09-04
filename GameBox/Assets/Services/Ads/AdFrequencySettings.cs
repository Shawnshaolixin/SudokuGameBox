namespace Box.Services
{
    /// <summary>
    /// 插屏频控参数默认表(M3.2 配置化第一步:硬编码参数从 AdFrequencyController 移入本表)。
    /// 设计:本地默认层 + 覆写层——M3 后远程运营配置(公共配置组/随组 JSON)整体覆写本表
    /// 静态字段即全局生效,频控只读本表、不感知配置来源(拍板 3「本地默认表 + 覆盖层」)。
    /// </summary>
    public static class AdFrequencySettings
    {
        /// <summary>新用户前 N 局不弹插屏(0 = 关闭首局保护)。</summary>
        public static int NoInterstitialFirstLevels = 3;

        /// <summary>局间隔下限(秒)。</summary>
        public static int MinIntervalSec = 4 * 60;

        /// <summary>局间隔上限(秒);展示后取 [Min, Max] 随机,避免节奏可预测。</summary>
        public static int MaxIntervalSec = 6 * 60;
    }
}
