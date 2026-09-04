using System;
using Box.Services;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// 广告桩实现:未接入 AdMob 时使用。
    /// 模拟激励视频「直接看完并发放奖励」,方便你在不装任何 SDK 的情况下先跑通流程。
    /// 保留"接口 + 桩 + 真实现 + #if 开关"设计(10 文档 Phase 2-2):真实现(AdMob/Firebase/IAP)Phase 7 接入。
    /// 插屏频控(M3.2)与真实现共用同一 AdFrequencyController(box.ads.* 键,构造时自动迁移旧
    /// sudoku.ads.* 键):计数(NotifyLevelCompleted)与展示判定行为与 AdMobAdsService 一致——
    /// 桩不简化频控规则,只简化「真实展示」本身。
    /// 去广告键仍为桩本地 PlayerPrefs(开发用;真实现去广告走 D-7 box.commerce 存档分区)。
    /// </summary>
    public sealed class AdsServiceStub : IAdsService
    {
        private const string KeyAdsRemoved = "sudoku.ads.removed";

        private readonly AdFrequencyController _frequency = new AdFrequencyController();

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

        /// <summary>过关计数(与真实现共用频控控制器,行为一致;去广告只挡展示不挡计数)。</summary>
        public void NotifyLevelCompleted() => _frequency.NotifyLevelCompleted();

        /// <summary>桩插屏:不真正弹窗,但与真实现走同一频控判定(前 N 局保护/局间隔),命中才模拟展示。</summary>
        public void ShowInterstitial()
        {
            if (IsAdsRemoved) return; // 去广告零广告
            if (!_frequency.CanShowInterstitial())
            {
                Debug.Log("[AdsStub] 插屏频控未通过(前 3 局保护或未到间隔),跳过");
                return;
            }
            Debug.Log("[AdsStub] 模拟插屏展示(接入 AdMob 后由真实现接完整展示链路)");
            _frequency.OnInterstitialShown();
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
        public void Initialize()
        {
            Debug.Log("[AnalyticsStub] 已初始化(未接入 Firebase,埋点走桩实现)");
        }

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
