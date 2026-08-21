using Box.Services;
using Box.UI;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// 应用启动引导(旧工程 AppBootstrap 参考重构,10 文档 Phase 3-3 场景框架的接入点):
    /// 首个场景加载前创建 UIService(路由/弹窗仲裁/层级/返回键),持有唯一实例供玩法层访问。
    /// v1.0 纯 AOT 直接运行,无任何网络等待;v1.1 热更下载链路在 Phase 9 接入。
    /// 广告/内购/分析真实现 Phase 7 接入,当前传 Stub 便于观察 UI 埋点(ui_show)。
    /// </summary>
    public static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            // 唯一实例:Phase 4 起 View 通过 UIService.Instance 访问路由/弹窗仲裁
            UIService.Register(new UIService(
                new ResourceViewLoader(),
                new AnalyticsServiceStub()));
            Debug.Log("[AppBootstrap] UIService created (Router/Popup/BackKey ready)");
        }
    }
}
