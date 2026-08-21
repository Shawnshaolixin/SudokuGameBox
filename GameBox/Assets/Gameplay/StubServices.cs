using System;
using Box.Services;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// 广告桩实现:未接入 AdMob 时使用。
    /// 模拟激励视频「直接看完并发放奖励」,方便你在不装任何 SDK 的情况下先跑通流程。
    /// 保留"接口 + 桩 + 真实现 + #if 开关"设计(10 文档 Phase 2-2):真实现(AdMob/Firebase/IAP)Phase 7 接入。
    /// </summary>
    public sealed class AdsServiceStub : IAdsService
    {
        private const string KeyAdsRemoved = "sudoku.ads.removed";

        public bool IsInitialized => true;
        public bool IsRewardedReady => true;
        public bool IsAdsRemoved { get; private set; }

        public AdsServiceStub()
        {
            IsAdsRemoved = PlayerPrefs.GetInt(KeyAdsRemoved, 0) == 1;
        }

        public void Initialize()
        {
            Debug.Log("[AdsStub] 已初始化(未接入 AdMob,广告走桩实现)");
        }

        public void SetRemoveAds(bool removed)
        {
            IsAdsRemoved = removed;
            PlayerPrefs.SetInt(KeyAdsRemoved, removed ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void ShowRewardedAd(Action<bool> onReward)
        {
            Debug.Log("[AdsStub] 模拟激励视频:直接视为看完并发放奖励");
            onReward?.Invoke(true);
        }
    }

    /// <summary>
    /// 内购桩实现:未接入 Unity IAP 时使用,模拟购买 / 恢复。
    /// </summary>
    public sealed class IapServiceStub : IIapService
    {
        private const string KeyRemoveAdsPurchased = "sudoku.iap.removeAdsPurchased";

        public bool IsInitialized => true;
        public bool IsRemoveAdsPurchased { get; private set; }
        public event Action PurchaseCompleted;

        public IapServiceStub()
        {
            IsRemoveAdsPurchased = PlayerPrefs.GetInt(KeyRemoveAdsPurchased, 0) == 1;
        }

        public void Initialize()
        {
            Debug.Log("[IapStub] 已初始化(未接入 Unity IAP,内购走桩实现)");
            if (IsRemoveAdsPurchased)
                PurchaseCompleted?.Invoke(); // 模拟「启动时恢复购买」
        }

        public void BuyRemoveAds()
        {
            Debug.Log("[IapStub] 模拟购买「去广告」成功");
            IsRemoveAdsPurchased = true;
            PlayerPrefs.SetInt(KeyRemoveAdsPurchased, 1);
            PlayerPrefs.Save();
            PurchaseCompleted?.Invoke();
        }

        public void RestorePurchases()
        {
            Debug.Log("[IapStub] 模拟恢复购买");
            if (IsRemoveAdsPurchased)
                PurchaseCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 分析桩实现:未接入 Firebase 时使用,把事件打印到 Console。
    /// </summary>
    public sealed class AnalyticsServiceStub : IAnalyticsService
    {
        public void LogEvent(string eventName)
        {
            Debug.Log($"[AnalyticsStub] 事件:{eventName}");
        }

        public void LogEvent(string eventName, string parameterName, object parameterValue)
        {
            Debug.Log($"[AnalyticsStub] 事件:{eventName}  {parameterName}={parameterValue}");
        }

        public void LogNonFatal(string message)
        {
            Debug.LogWarning($"[AnalyticsStub] 非致命:{message}");
        }
    }
}
