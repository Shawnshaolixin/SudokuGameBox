#if SUDOKU_ADMOB
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GoogleMobileAds;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace Box.Services
{
    /// <summary>
    /// 广告真实现（Phase 7 7-1）：Google AdMob v11（Next-Gen API）+ UMP 同意流程 + 插屏频控。
    /// 职责：初始化（含 UMP 同意表单）、激励视频、插屏（频控：前 3 局不弹 / 局间隔 4~6 分钟随机）、
    /// 去广告状态恢复与持久化（D-7 存档分区 box.commerce，15 号文档 §2）。
    /// 注意：本文件在 #if SUDOKU_ADMOB 下编译——需先导入 google_mobile_ads v11.x .unitypackage，
    /// 再通过 Editor 菜单「Box/商业化/应用 AdMob+IAP 编译符号」写入该符号（Phase7AdMobSetup.cs）。
    /// </summary>
    public sealed class AdMobAdsService : IAdsService
    {
        // 官方测试广告位 ID（15 号文档 §2 约定）；AdMob 账号（A2）与广告单元申请下来后替换。
        // 注意:测试位 ID 在任何设备上都只返回测试广告(零收入,无违规风险),开发阶段无需注册测试设备;
        // 换成真实广告位 ID 后,必须把真机注册为测试设备(见 TestDeviceIds),否则会被判定无效流量。
        private const string RewardedAdUnitId = "ca-app-pub-6367116322180531/5022991846";
        private const string InterstitialAdUnitId = "ca-app-pub-6367116322180531/4813896836";

        // 真机测试设备 ID 列表(换真实广告位后的必做项,Phase 9):
        // 真机首次请求广告后,logcat 会打印
        //   Use RequestConfiguration.Builder().setTestDeviceIds(...) to get test ads on this device
        // 把其中的设备 ID 填入下方数组,该设备将始终收到测试广告。
        // 已填入:用户 OPPO 真机(2026-08-25 从老包 logcat 取得,设备级 ID 跨应用相同);
        // 新设备首次请求广告后按 logcat 提示补充。填入后广告初始化前生效。
        private static readonly List<string> TestDeviceIds = new() { "AAC1C00E2A99B28A43349D7BD59ADE49" };

        // UMP 同意流程开关(Phase 9 真机):GDPR 只约束欧洲(EEA)用户,中国用户不需要同意表单。
        // 真机实测:UMP 访问 consent.google.com 在国内网络会挂起,原生调用阻塞导致广告初始化
        // 迟迟不执行(15 秒超时兜底都来不及触发)。测试期默认关闭,直接初始化广告;
        // 上架面向欧美市场时置回 true 并确保 UMP 表单在 AdMob 后台「隐私与消息」已配置。
        private const bool UmpEnabled = false;

        private const string CommerceModuleId = "box.commerce"; // D-7 存档分区：去广告状态

        private readonly ISaveService _save;
        private readonly AdFrequencyController _frequency = new AdFrequencyController();

        private RewardedAd _rewardedAd;      // 当前就绪的激励视频实例（展示完成后置空并预加载下一个）
        private InterstitialAd _interstitialAd; // 当前就绪的插屏实例

        // 激励视频失败延迟重试(Phase 9 真机:国内网络访问 Google 广告服务器偶发超时,
        // 插屏成功/激励 Internal error 即此类抖动;官方防限流:不在失败回调内立即重试,
        // 而是延迟低频补加载)。
        // 策略:前 2 次 60 秒快速重试,之后转为 5 分钟一次持续重试(不封顶)——测试位/网络
        // 恢复后广告自动就绪,无需用户操作。官方测试位曾出现数小时 Internal error 波动,
        // 短窗口重试会耗尽导致广告永久不可用。
        private const int MaxRewardedRetry = 2;
        private const int RewardedRetryIntervalSec = 60;   // 快速重试间隔
        private const int RewardedRetryLongSec = 300;      // 持续重试间隔(5 分钟)
        private int _rewardedRetryCount;
        private bool _retryPending; // 防重入:已有重试在排队时不再追加(避免多路并发加载)

        public bool IsInitialized { get; private set; }

        /// <summary>激励视频是否已就绪可展示。</summary>
        public bool IsRewardedReady => _rewardedAd != null && _rewardedAd.CanShowAd();

        /// <summary>去广告（用户已购买或恢复）。启动时从 D-7 分区恢复。</summary>
        public bool IsAdsRemoved { get; private set; }

        /// <param name="save">存档服务，用于读写 D-7「box.commerce」分区的去广告状态。</param>
        public AdMobAdsService(ISaveService save)
        {
            _save = save;
            IsAdsRemoved = _save.GetModule<CommerceData>(CommerceModuleId)?.RemoveAdsPurchased ?? false;
        }

        /// <summary>
        /// 初始化：UMP 更新同意状态并（需要时）展示同意表单，随后初始化 AdMob 并预加载两类广告。
        /// 必须在主线程启动时调用一次。
        /// UMP 加 15 秒超时兜底(Phase 9 真机):国内网络访问 consent.google.com 偶发挂起,
        /// 回调链卡住会导致 MobileAds.Initialize 永不执行、广告永不出(老项目无 UMP 因此正常);
        /// 超时直接跳过同意流程初始化广告,测试位广告展示不受影响。
        /// 警告：不要在广告加载失败回调里立刻重试（官方建议，防止限流）；重试只发生在展示关闭后。
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized)
            {
                Debug.Log("[AdMob] 已初始化，跳过重复初始化");
                return;
            }

            // —— 第零步:注册真机测试设备(TestDeviceIds 非空时生效;测试位 ID 阶段可跳过)——
            if (TestDeviceIds.Count > 0)
            {
                var requestConfiguration = new RequestConfiguration
                {
                    TestDeviceIds = TestDeviceIds,
                };
                MobileAds.SetRequestConfiguration(requestConfiguration);
                Debug.Log($"[AdMob] 已注册 {TestDeviceIds.Count} 台真机测试设备");
            }

            if (UmpEnabled)
            {
                // UMP 启用时走同意流程(带超时兜底);关闭时直接初始化(非 EEA 用户不需要)
                UmpFlowWithTimeout().Forget();
            }
            else
            {
                InitializeAds();
            }
        }

        /// <summary>
        /// UMP 同意流程带超时(确定性兜底):Update →(需要时)表单 → 回调链结束或 15 秒超时,一律进入广告初始化。
        /// 用 UniTask.WhenAny 竞速:表单流程回调(flow) vs 15 秒定时器,先完成者决定流程走向。
        /// </summary>
        async UniTaskVoid UmpFlowWithTimeout()
        {
            var flow = new UniTaskCompletionSource();
            var requestParameters = new ConsentRequestParameters { TagForUnderAgeOfConsent = false };
            ConsentInformation.Update(requestParameters, updateError =>
            {
                if (updateError != null)
                {
                    // 更新失败但可能仍有上轮的同意结果，继续初始化广告（官方示例同样处理）
                    Debug.LogWarning($"[AdMob] UMP 更新同意状态失败：{updateError.Message}");
                }

                // 需要同意时才展示表单；表单展示/关闭后进入广告初始化
                ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                {
                    if (formError != null)
                    {
                        // 表单展示失败一般不影响广告（依赖上轮同意状态），仅告警
                        Debug.LogWarning($"[AdMob] UMP 同意表单展示失败：{formError.Message}");
                    }
                    flow.TrySetResult(); // 表单流程走完(无论成败),进入初始化
                });
            });

            int idx = await UniTask.WhenAny(flow.Task,
                UniTask.Delay(TimeSpan.FromSeconds(15), ignoreTimeScale: true));
            if (idx == 1)
            {
                // 回调链 15 秒未走完:国内网络访问同意服务挂起,跳过同意流程直接初始化
                Debug.LogWarning("[AdMob] UMP 同意流程 15 秒超时,跳过直接初始化广告(国内网络兜底)");
            }
            InitializeAds();
        }

        /// <summary>初始化 AdMob 并预加载激励视频与插屏（UMP 表单流程完成后调用）。</summary>
        private void InitializeAds()
        {
            MobileAds.Initialize(status =>
            {
                IsInitialized = true;
                Debug.Log($"[AdMob] 初始化完成，初始化状态：{status}");
                LoadRewardedAd();
                LoadInterstitialAd();
            });
        }

        /// <summary>
        /// 设置去广告状态并持久化到 D-7 存档分区。
        /// 调用方：IAP 购买/恢复成功链路（AppBootstrap 订阅 IapService.PurchaseCompleted 后调用）。
        /// </summary>
        public void SetRemoveAds(bool removed)
        {
            if (IsAdsRemoved == removed)
            {
                return; // 状态无变化，避免无谓写盘
            }

            IsAdsRemoved = removed;
            _save.SetModule(CommerceModuleId, new CommerceData
            {
                RemoveAdsPurchased = removed,
                UpdatedAtUnixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            Debug.Log($"[AdMob] 去广告状态已更新：{removed}");
        }

        /// <summary>
        /// 展示激励视频。用户完整看完后回调 onReward(true) 发放奖励；
        /// 未就绪或展示失败回调 onReward(false)。注意：激励视频回调不会在"用户中途关闭"时收到 true。
        /// </summary>
        public void ShowRewardedAd(Action<bool> onReward)
        {
            if (IsAdsRemoved)
            {
                Debug.Log("[AdMob] 已去广告，跳过激励视频（按产品策略仍可看广告换奖励）");
            }

            if (_rewardedAd == null || !_rewardedAd.CanShowAd())
            {
                Debug.LogWarning("[AdMob] 激励视频未就绪，正在重新加载");
                onReward?.Invoke(false);
                LoadRewardedAd(); // 未就绪时补一次加载（非失败回调重试，符合官方建议）
                return;
            }

            var ad = _rewardedAd;
            _rewardedAd = null; // 提前置空：展示期间不可重复使用同一实例
            ad.Show(reward =>
            {
                // 用户完整观看完成，发放奖励
                Debug.Log($"[AdMob] 激励视频观看完成，奖励类型：{reward.Type} 数量：{reward.Amount}");
                onReward?.Invoke(true);
            });
        }

        /// <summary>
        /// 展示插屏（一局结束时的候选点）。频控判定在展示前完成：
        /// 去广告零插屏 → 前 3 局不弹 → 距上次间隔 4~6 分钟。
        /// 展示成功后将下次允许时间记录为 now + 随机 4~6 分钟。
        /// </summary>
        public void ShowInterstitial()
        {
            if (IsAdsRemoved)
            {
                Debug.Log("[AdMob] 已去广告，跳过插屏");
                return;
            }

            _frequency.OnLevelEnded(); // 每局结束计数（频控用）
            if (!_frequency.CanShowInterstitial())
            {
                Debug.Log("[AdMob] 插屏频控未通过（前 3 局保护或未到间隔）");
                return;
            }

            if (_interstitialAd == null || !_interstitialAd.CanShowAd())
            {
                Debug.LogWarning("[AdMob] 插屏未就绪，跳过本次（下次对局再试）");
                LoadInterstitialAd();
                return;
            }

            var ad = _interstitialAd;
            _interstitialAd = null; // 展示后实例失效，Close 事件里会重新预加载
            ad.Show();
            _frequency.OnInterstitialShown(); // 记录下次允许展示时间
        }

        // ======================== 激励视频加载与生命周期 ========================

        /// <summary>加载激励视频（v11 双参数回调：成功给 ad，失败给 error）。</summary>
        private void LoadRewardedAd()
        {
            var adRequest = new AdRequest();
            RewardedAd.Load(RewardedAdUnitId, adRequest, (ad, error) =>
            {
                if (error != null)
                {
                    // 输出错误码便于诊断(code=0 即 Internal error,多为网络超时/测试位波动)
                    Debug.LogWarning($"[AdMob] 激励视频加载失败：code={error.GetCode()} {error.GetMessage()}");
                    ScheduleRewardedRetry();
                    return;
                }

                _rewardedRetryCount = 0; // 加载成功,重置重试计数
                _rewardedAd = ad;
                BindLifecycleEvents(ad);
                Debug.Log("[AdMob] 激励视频加载成功");
            });
        }

        /// <summary>
        /// 激励视频加载失败的延迟重试(网络抖动/测试位波动缓解)。
        /// 前 MaxRewardedRetry 次 60 秒快速重试,之后 5 分钟一次持续重试直到加载成功——
        /// 测试位或网络恢复后广告自动就绪。防重入:已有重试排队时不追加。
        /// 官方建议「不在失败回调内立即重试」防限流——延迟低频重试不冲突。
        /// </summary>
        private async UniTaskVoid ScheduleRewardedRetry()
        {
            if (_retryPending)
            {
                return; // 已有重试在排队,避免多路并发加载同一单元
            }

            _retryPending = true;
            var delaySec = _rewardedRetryCount < MaxRewardedRetry
                ? RewardedRetryIntervalSec
                : RewardedRetryLongSec;
            _rewardedRetryCount++;
            Debug.Log($"[AdMob] 激励视频第 {_rewardedRetryCount} 次延迟重试({delaySec}s)");
            await UniTask.Delay(TimeSpan.FromSeconds(delaySec), ignoreTimeScale: true);
            _retryPending = false;
            LoadRewardedAd(); // 若仍失败会再次进入本方法,直到成功
        }

        /// <summary>绑定激励视频生命周期事件：收费上报（埋点）、点击、打开、关闭、展示失败。</summary>
        private void BindLifecycleEvents(RewardedAd ad)
        {
            ad.OnAdPaid += value =>
            {
                // 广告收入事件：将来接入 Analytics 时在此上报（IAnalyticsService.LogEvent）
                Debug.Log($"[AdMob] 激励视频付费事件：{value.Value} {value.CurrencyCode}");
            };
            ad.OnAdImpressionRecorded += () => Debug.Log("[AdMob] 激励视频展示已记录");
            ad.OnAdClicked += () => Debug.Log("[AdMob] 激励视频被点击");
            ad.OnAdFullScreenContentOpened += () => Debug.Log("[AdMob] 激励视频已打开");
            ad.OnAdFullScreenContentClosed += () =>
            {
                // 用户看完或关闭 → 销毁实例防泄漏，并预加载下一个
                ad.Destroy();
                Debug.Log("[AdMob] 激励视频已关闭，预加载下一个");
                LoadRewardedAd();
            };
            ad.OnAdFullScreenContentFailed += error =>
            {
                Debug.LogWarning($"[AdMob] 激励视频展示失败：{error.GetMessage()}");
                ad.Destroy();
                LoadRewardedAd(); // 展示失败后可重试加载
            };
        }

        // ======================== 插屏加载与生命周期 ========================

        /// <summary>加载插屏（v11 双参数回调）。</summary>
        private void LoadInterstitialAd()
        {
            var adRequest = new AdRequest();
            InterstitialAd.Load(InterstitialAdUnitId, adRequest, (ad, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[AdMob] 插屏加载失败：code={error.GetCode()} {error.GetMessage()}");
                    return; // 不在失败回调中立刻重试（官方建议）
                }

                _interstitialAd = ad;
                BindLifecycleEvents(ad);
                Debug.Log("[AdMob] 插屏加载成功");
            });
        }

        /// <summary>绑定插屏生命周期事件。</summary>
        private void BindLifecycleEvents(InterstitialAd ad)
        {
            ad.OnAdPaid += value => Debug.Log($"[AdMob] 插屏付费事件：{value.Value} {value.CurrencyCode}");
            ad.OnAdImpressionRecorded += () => Debug.Log("[AdMob] 插屏展示已记录");
            ad.OnAdClicked += () => Debug.Log("[AdMob] 插屏被点击");
            ad.OnAdFullScreenContentOpened += () => Debug.Log("[AdMob] 插屏已打开");
            ad.OnAdFullScreenContentClosed += () =>
            {
                // 展示关闭 → 销毁实例防泄漏，并预加载下一个
                ad.Destroy();
                Debug.Log("[AdMob] 插屏已关闭，预加载下一个");
                LoadInterstitialAd();
            };
            ad.OnAdFullScreenContentFailed += error =>
            {
                Debug.LogWarning($"[AdMob] 插屏展示失败：{error.GetMessage()}");
                ad.Destroy();
                LoadInterstitialAd();
            };
        }
    }
}
#endif