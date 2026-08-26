using Box.ModuleFramework;
using Box.Services;
using Box.UI;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// 应用启动引导(旧工程 AppBootstrap 参考重构,10 文档 Phase 3-3 场景框架的接入点):
    /// 首个场景加载前创建 UIService(路由/弹窗仲裁/层级/返回键)、ModuleLoader(模块清单,Phase 4.5)
    /// 与存档/偏好服务(Phase 5:D-7 AES 存档 + PlayerPrefs 偏好),注册 Services 静态定位器供玩法层访问。
    /// v1.0 纯 AOT 直接运行,无任何网络等待;v1.1 热更下载链路在 Phase 9 接入。
    /// 广告/内购/分析真实现 Phase 7 接入,当前传 Stub 便于观察 UI 埋点(ui_show)。
    /// </summary>
    public static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            // ===== 分析服务(Phase 11 前置:Firebase 提前接入,2026-08 拍板) =====
            // 真实现需 SUDOKU_FIREBASE 符号(编辑器菜单「Box/商业化/应用 Firebase 编译符号」,
            // FirebaseSetup.cs)与 Assets/google-services.json;未定义符号时自动回退桩(事件打印 Console),
            // 不影响日常开发与 CI。UIService 构造即需要分析服务,故在视图装配前创建。
#if SUDOKU_FIREBASE
            var analytics = new FirebaseAnalyticsService();
#else
            var analytics = new AnalyticsServiceStub();
#endif
            ServiceLocator.RegisterAnalytics(analytics); // 玩法层/壳层经 ServiceLocator.Analytics 上报

            // Phase 6:视图加载切 Addressables(资源在 Resources → Addressables 分组,见 Phase6AddressablesSetup);
            // 模块清单(ModuleCatalog)体积 <1KB、启动链路过短,保留 Resources 兜底为遗留,Phase 9 再清理。
            var ui = new UIService(new AddressablesViewLoader(), analytics);
            UIService.Register(ui);

            // Phase 5:存档 + 偏好(构造即加载;主/备损坏自动回退重建,不阻塞启动)
            var save = new SaveService();
            var settings = new SettingsService();
            ServiceLocator.Register(save, settings);
            ServiceLocator.RegisterAssets(new AddressablesAssetService()); // Phase 6:资源服务壳层
            L10n.Init(settings.Language); // 启动同步语言偏好 → 首屏即按偏好语言渲染(FR-17)

            // ===== Phase 7 7-1:广告 / 内购接入(真实现 + #if 编译符号;无符号时自动回退 Stub) =====
            // 说明:SUDOKU_IAP 已写入 ProjectSettings.asset(manifest 已声明 com.unity.purchasing 包);
            // SUDOKU_ADMOB 需先导入 google_mobile_ads v11 .unitypackage,再执行 Editor 菜单
            // 「Box/商业化/应用 AdMob+IAP 编译符号」(Phase7AdMobSetup.cs)后才会写入。
            // 这样设计:未导入 AdMob 插件前代码可正常编译,不影响日常开发与 CI。
#if SUDOKU_IAP
            var iap = new UnityIapService(save); // 真实现:Google Play 商店连接 + 非消耗品 remove_ads
#else
            var iap = new IapServiceStub();      // 桩:模拟购买,便于无 SDK 环境跑通流程
#endif
            ServiceLocator.RegisterIap(iap);

#if SUDOKU_ADMOB
            var ads = new AdMobAdsService(save); // 真实现:UMP 同意 + 激励视频 + 插屏(频控)
#else
            var ads = new AdsServiceStub();      // 桩:直接"看完"并发放奖励,插屏按前 3 局简化频控
#endif
            ServiceLocator.RegisterAds(ads);

            // 去广告链路(08 文档 §5.1):IAP 购买/恢复成功 → 广告服务置为去广告,此后不再展示任何广告
            iap.PurchaseCompleted += () => ads.SetRemoveAds(true);

            // ===== Phase 8 体验打磨:音频系统(BGM 常驻 + SFX 池;开关联动设置页) =====
            // 热更侧经 ServiceLocator.Audio(IAudioService)调用,实现细节(Addressables 加载/源池)在壳层。
            var audio = new AudioManager(ServiceLocator.Assets, settings);
            ServiceLocator.RegisterAudio(audio);
            audio.Initialize(); // 创建常驻对象 + 按偏好播 BGM(主菜单常驻,对局不切)

            // 异步初始化:真实现中 AdMob 含 UMP 同意流程与广告预加载,IAP 异步连接商店,
            // Firebase 异步修复依赖(埋点在就绪前静默丢弃)。
            // 初始化结果不影响启动流程(广告先弹后投、商店未就绪时购买按钮给出提示)。
            ads.Initialize();
            iap.Initialize();
            analytics.Initialize();

            // 模块清单:Resources 兜底路径(Phase 6 迁 Addressables)。
            // 缺失时注册空清单,大厅入口静默不渲染(Editor 脚本 Phase45ModuleSetup 保证资产存在并入库)。
            var catalog = Resources.Load<ModuleCatalog>("Config/ModuleCatalog");
            ModuleLoader.Register(new ModuleLoader(ui, catalog != null ? catalog.entries : null));
            Debug.Log($"[AppBootstrap] UIService + ModuleLoader + Services registered (存档:{save.Exists})");
        }
    }
}
