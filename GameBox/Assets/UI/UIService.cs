using Box.Services;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Box.UI
{
    /// <summary>
    /// UIKit 组合入口(11 文档 §3.5):路由 + 弹窗仲裁 + 层级管理器 + 返回键接线。
    /// 场景启动时由引导脚本创建并持有唯一实例;热更侧通过 AOT 接口访问。
    /// </summary>
    public sealed class UIService
    {
        public UIRouter Router { get; }
        public PopupArbiter Popup { get; }
        public UILayerManager Layers { get; } = new();

        BackKeyRunner _backRunner;

        public UIService(IViewLoader loader, IAnalyticsService analytics = null)
        {
            Router = new UIRouter(loader) { Analytics = analytics };
            Popup = new PopupArbiter(Router);
            EnsureEventSystem();
            _backRunner = new GameObject("BoxUI_BackKey").AddComponent<BackKeyRunner>();
            _backRunner.Init(Router);
        }

        /// <summary>
        /// UGUI 点击必需;Scene 场景未自带 EventSystem 时兜底创建。
        /// 项目为 New-only 输入(模板默认,6000.3 起 Android 不支持 Both),
        /// 故用 InputSystemUIInputModule 而非 StandaloneInputModule。
        /// </summary>
        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        /// <summary>逐帧监听 Android 返回键(Escape,新输入系统,无 InputAction 资产零配置)。</summary>
        sealed class BackKeyRunner : MonoBehaviour
        {
            UIRouter _router;

            public void Init(UIRouter router)
            {
                _router = router;
                DontDestroyOnLoad(gameObject);
            }

            void Update()
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    _router.HandleBackAsync().Forget();
            }
        }
    }
}
