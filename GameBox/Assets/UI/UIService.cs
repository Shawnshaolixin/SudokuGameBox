using System;
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
        /// <summary>
        /// 应用级唯一实例,由启动引导(AppBootstrap)经 Register 注册。
        /// EditMode 测试不触发 RuntimeInitializeOnLoadMethod,不受影响。
        /// </summary>
        public static UIService Instance { get; private set; }

        /// <summary>注册应用级唯一实例(启动引导调用);重复注册告警并覆盖。</summary>
        public static void Register(UIService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (Instance != null)
                Debug.LogWarning("[UIKit] UIService 重复注册,旧实例将被覆盖");
            Instance = service;
        }

        public UIRouter Router { get; }
        public PopupArbiter Popup { get; }
        public UILayerManager Layers { get; } = new();

        /// <summary>
        /// 返回键自定义处理(Phase 4:对局视图注册,返回键=Undo)。
        /// 返回 true=已消费(路由不再处理);false=交还 Router(弹栈)。
        /// 仅允许注册一个;注册前自动注销旧的,视图 OnHide 时自行注销。
        /// </summary>
        public Func<UniTask<bool>> CustomBackHandler { get; private set; }

        BackKeyRunner _backRunner;

        public UIService(IViewLoader loader, IAnalyticsService analytics = null)
        {
            Router = new UIRouter(loader) { Analytics = analytics };
            Popup = new PopupArbiter(Router);
            EnsureEventSystem();
            _backRunner = new GameObject("BoxUI_BackKey").AddComponent<BackKeyRunner>();
            _backRunner.Init(this);
        }

        public void RegisterBackHandler(Func<UniTask<bool>> handler) => CustomBackHandler = handler;

        /// <summary>注销返回键自定义处理(视图 OnDestroy 时调用,防残留闭包)。</summary>
        public void ClearBackHandler() => CustomBackHandler = null;

        /// <summary>Android 返回键统一入口:自定义 handler 优先,未消费则交还路由。</summary>
        public async UniTask HandleBackAsync()
        {
            if (CustomBackHandler != null && await CustomBackHandler())
                return; // 已消费(如对局 Undo)
            await Router.HandleBackAsync();
        }

        /// <summary>
        /// UGUI 点击必需;场景未自带 EventSystem 时兜底创建(Phase 9 修复:常驻跨场景)。
        /// 背景:本项目场景资产(MainMenu/Gameplay)此前各自带 EventSystem,
        /// 而本方法在 BeforeSceneLoad 时执行——场景对象尚未加载,检测不到场景内置的
        /// EventSystem,导致启动场景出现 2 个 EventSystem(Unity 官方要求恰好 1 个,
        /// 多例会让 current 竞争,点击事件偶发丢失)。
        /// 修复:创建时 DontDestroyOnLoad 常驻,并删除场景资产中的 EventSystem,
        /// 任意时刻全局唯一。
        /// 项目为 New-only 输入(模板默认,6000.3 起 Android 不支持 Both),
        /// 故用 InputSystemUIInputModule 而非 StandaloneInputModule。
        /// </summary>
        static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go); // 跨场景常驻:热更场景切换不重建
        }

        /// <summary>逐帧监听 Android 返回键(Escape,新输入系统,无 InputAction 资产零配置)。</summary>
        sealed class BackKeyRunner : MonoBehaviour
        {
            UIService _service;

            public void Init(UIService service)
            {
                _service = service;
                if (Application.isPlaying) DontDestroyOnLoad(gameObject); // EditMode 测试禁止调用
            }

            void Update()
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    _service.HandleBackAsync().Forget();
            }
        }
    }
}
