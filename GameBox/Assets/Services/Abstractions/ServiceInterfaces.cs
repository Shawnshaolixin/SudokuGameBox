using System;

namespace Box.Services
{
    /// <summary>
    /// 广告服务接口:激励视频 + 去广告状态。
    /// 定义接口的目的是把「业务逻辑」和「具体 SDK」解耦:
    /// 上层只调用 IAdsService,不关心底层是 AdMob 还是桩实现。
    /// 11 文档:Services 接口程序集(Box.Services.Abstractions)是热更侧唯一可引用的服务程序集。
    /// </summary>
    public interface IAdsService
    {
        bool IsInitialized { get; }
        bool IsRewardedReady { get; }
        bool IsAdsRemoved { get; }

        void Initialize();

        /// <summary>设置去广告状态(内购完成后调用)。</summary>
        void SetRemoveAds(bool removed);

        /// <summary>展示激励视频;回调参数 true 表示玩家看完并应发放奖励。</summary>
        void ShowRewardedAd(Action<bool> onReward);

        /// <summary>展示插屏广告(时机由玩法层在局间/自然停顿点调用)。</summary>
        /// <remarks>
        /// 频控规则(04 文档 §广告频控,真实现内部执行):
        /// 1. 已购去广告 → 零广告;
        /// 2. 新用户前 3 局不弹插屏;
        /// 3. 插屏局间至少间隔 4~6 分钟(取随机值,避免可预测节奏)。
        /// </remarks>
        void ShowInterstitial();
    }

    /// <summary>
    /// 内购服务接口:去广告(非消耗型商品)。
    /// </summary>
    public interface IIapService
    {
        bool IsInitialized { get; }
        bool IsRemoveAdsPurchased { get; }

        /// <summary>新购成功或启动恢复购买完成时触发。</summary>
        event Action PurchaseCompleted;

        void Initialize();
        void BuyRemoveAds();
        void RestorePurchases();
    }

    /// <summary>
    /// 分析服务接口:埋点事件 + 非致命错误上报。
    /// </summary>
    public interface IAnalyticsService
    {
        void LogEvent(string eventName);
        void LogEvent(string eventName, string parameterName, object parameterValue);
        void LogNonFatal(string message);
    }

    /// <summary>
    /// D-7 存档 box.commerce 分区数据(Phase 7 7-1):去广告购买状态持久化。
    /// 15 号文档要求去广告状态写入存档分区(不再用 Stub 的 PlayerPrefs 键),
    /// 由 Ads/Iap 真实现通过 SaveService.GetModule/SetModule("box.commerce") 读写。
    /// </summary>
    [Serializable]
    public sealed class CommerceData
    {
        /// <summary>是否已购去广告(非消耗型商品,购买后永久有效)。</summary>
        public bool RemoveAdsPurchased;

        /// <summary>最后更新时间的 Unix 秒(排查/调试用)。</summary>
        public long UpdatedAtUnixSec;
    }
}
