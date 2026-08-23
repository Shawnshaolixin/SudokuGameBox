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
            // Phase 6:视图加载切 Addressables(资源在 Resources → Addressables 分组,见 Phase6AddressablesSetup);
            // 模块清单(ModuleCatalog)体积 <1KB、启动链路过短,保留 Resources 兜底为遗留,Phase 9 再清理。
            var ui = new UIService(new AddressablesViewLoader(), new AnalyticsServiceStub());
            UIService.Register(ui);

            // Phase 5:存档 + 偏好(构造即加载;主/备损坏自动回退重建,不阻塞启动)
            var save = new SaveService();
            var settings = new SettingsService();
            ServiceLocator.Register(save, settings);
            ServiceLocator.RegisterAssets(new AddressablesAssetService()); // Phase 6:资源服务壳层
            L10n.Init(settings.Language); // 启动同步语言偏好 → 首屏即按偏好语言渲染(FR-17)

            // 模块清单:Resources 兜底路径(Phase 6 迁 Addressables)。
            // 缺失时注册空清单,大厅入口静默不渲染(Editor 脚本 Phase45ModuleSetup 保证资产存在并入库)。
            var catalog = Resources.Load<ModuleCatalog>("Config/ModuleCatalog");
            ModuleLoader.Register(new ModuleLoader(ui, catalog != null ? catalog.entries : null));
            Debug.Log($"[AppBootstrap] UIService + ModuleLoader + Services registered (存档:{save.Exists})");
        }
    }
}
