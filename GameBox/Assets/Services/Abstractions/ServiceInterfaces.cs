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
}
