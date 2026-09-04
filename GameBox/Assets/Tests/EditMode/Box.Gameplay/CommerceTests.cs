using System;
using Box.Services;
using NUnit.Framework;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// 商业化闭环冒烟(Phase 7 7-1,15 号文档 §2.4「去广告→零广告路径」):
    /// 用 Fake 广告/内购服务验证「购买去广告 → PurchaseCompleted → SetRemoveAds(true) → 零广告」链路。
    /// 接线与 AppBootstrap 一致(iap.PurchaseCompleted += () => ads.SetRemoveAds(true)),
    /// 桩/真实现共用同一接口,本测试对两种实现均有效。
    /// </summary>
    public class CommerceTests
    {
        FakeAdsService _ads;
        FakeIapService _iap;

        [SetUp]
        public void SetUp()
        {
            _ads = new FakeAdsService();
            _iap = new FakeIapService();
            ServiceLocator.Reset();
            ServiceLocator.RegisterAds(_ads);
            ServiceLocator.RegisterIap(_iap);
            // 与 AppBootstrap.cs 相同的去广告接线(商业闭环核心)
            _iap.PurchaseCompleted += () => _ads.SetRemoveAds(true);
        }

        [TearDown]
        public void TearDown() => ServiceLocator.Reset();

        [Test]
        public void BeforePurchase_Interstitial_Shown()
        {
            Assert.IsFalse(_ads.IsAdsRemoved);
            _ads.ShowInterstitial();
            Assert.AreEqual(1, _ads.InterstitialShown, "购买前插屏正常展示");
        }

        [Test]
        public void Purchase_RemoveAds_StopsAllAds()
        {
            _iap.BuyRemoveAds();

            Assert.IsTrue(_iap.IsRemoveAdsPurchased);
            Assert.IsTrue(_ads.IsAdsRemoved, "购买成功应联动广告服务置为去广告");

            _ads.ShowInterstitial();  // 去广告后:插屏零展示(与 AdMobAdsService 一致)
            Assert.AreEqual(0, _ads.InterstitialShown, "去广告后插屏不应展示");
            // 注:激励视频去广告后仍可看(AdMobAdsService 产品策略「激励永不强制」,主动观看仍回奖),此处不拦截
        }

        [Test]
        public void PurchaseCompleted_Event_Triggers_RemoveAds()
        {
            Assert.IsFalse(_ads.IsAdsRemoved);
            _iap.SimulatePurchaseComplete(); // 模拟购买/恢复成功回调
            Assert.IsTrue(_ads.IsAdsRemoved, "PurchaseCompleted 事件应触发去广告联动");
        }

        [Test]
        public void Ads_ServiceAccessible_ThroughLocator()
        {
            Assert.AreSame(_ads, ServiceLocator.Ads, "注册后玩法层经 ServiceLocator.Ads 取广告服务");
            Assert.AreSame(_iap, ServiceLocator.Iap, "注册后玩法层经 ServiceLocator.Iap 取内购服务");
        }

        // ---- 测试用 Fake 服务(与 Stub 语义一致:购买即成功,触发事件) ----

        sealed class FakeAdsService : IAdsService
        {
            public bool IsInitialized => true;
            public bool IsRewardedReady => true;
            public bool IsAdsRemoved { get; private set; }
            public int InterstitialShown { get; private set; }

            public void Initialize() { }
            public void SetRemoveAds(bool removed) => IsAdsRemoved = removed;
            public void ShowRewardedAd(Action<bool> onReward) => onReward?.Invoke(true);
            public void NotifyLevelCompleted() { } // M3.2 频控计数接口:本用例只验展示开关,计数无关
            public void ShowInterstitial()
            {
                if (IsAdsRemoved) return; // 去广告零广告(与 AdMobAdsService 一致)
                InterstitialShown++;
            }
        }

        sealed class FakeIapService : IIapService
        {
            public bool IsInitialized => true;
            public bool IsRemoveAdsPurchased { get; private set; }
            public event Action PurchaseCompleted;

            public void Initialize() { }
            public void BuyRemoveAds()
            {
                if (IsRemoveAdsPurchased) return; // 非消耗品幂等
                IsRemoveAdsPurchased = true;
                PurchaseCompleted?.Invoke();
            }
            public void RestorePurchases() { }
            public void SimulatePurchaseComplete() => PurchaseCompleted?.Invoke();
        }
    }
}
