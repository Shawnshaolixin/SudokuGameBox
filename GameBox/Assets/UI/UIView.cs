using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// 界面基类(11 文档 §3.5 第 3/5/8 条)。
    /// 生命周期全部 UniTask 化、可取消;CanvasScaler 与安全区在基类统一处理。
    /// 热更侧 View 只继承本基类、只调接口,禁止直接碰加载器与 SDK 类型。
    /// </summary>
    public abstract class UIView : MonoBehaviour
    {
        /// <summary>缓存策略:常驻 / 缓存 N 个 / 立即销毁。</summary>
        public enum CacheMode { Keep, CacheN, Destroy }

        public string Id { get; internal set; }
        public UILayer Layer { get; protected set; } = UILayer.Window;
        public CacheMode Cache { get; protected set; } = CacheMode.CacheN;
        public int CacheCount { get; protected set; } = 3;

        public bool IsShown { get; private set; }

        bool _rootInited;

        protected virtual void Awake()
        {
            // 安全区由 SafeAreaFitter 托管:横竖屏/键盘变化时动态刷新(§3.5 第 8 条 / 11 文档 §10.1)
            if (GetComponent<SafeAreaFitter>() == null)
                gameObject.AddComponent<SafeAreaFitter>();
        }

        /// <summary>
        /// 容错子节点查找:弹窗改造(2026-08)后内容移入 Card 子节点;
        /// 先查 "Card/路径",未迁移 prefab 回退根直查——迁移过渡期/新老 prefab 均可运行。
        /// </summary>
        protected Transform FindInCard(string path)
            => transform.Find("Card/" + path) ?? transform.Find(path);

        /// <summary>
        /// 场景根视图初始化(Scene 直挂、不进 Router 栈的常驻视图,如 MainMenu/Gameplay):
        /// Create+Show 一次,使 OnCreate 接线与 OnShow 启动逻辑与 Router 推入视图一致。
        /// 子类在 Awake 中调用;Router 管理的视图禁止调用(生命周期由路由驱动)。
        /// </summary>
        protected async UniTask InitSceneRoot()
        {
            if (_rootInited) return;
            _rootInited = true;
            await CreateAsync();
            await ShowAsync(null);
        }

        // ---- 生命周期(由 UIRouter 调用,子类覆写) ----

        protected virtual UniTask OnCreate() => UniTask.CompletedTask;
        protected virtual UniTask OnShow(object args) => UniTask.CompletedTask;
        protected virtual UniTask OnHide() => UniTask.CompletedTask;
        protected virtual UniTask OnDestroy() => UniTask.CompletedTask;
        protected virtual UniTask OnRefresh() => UniTask.CompletedTask;

        // 生命周期内动画(BoxTween)在视图销毁/场景切换时被取消会抛 OCE:
        // 属正常控制流,在此吞掉防止从 async void(Awake) 或 Forget() 逃逸为未处理异常。
        internal async UniTask CreateAsync()
        {
            try { await OnCreate(); }
            catch (OperationCanceledException) { /* 视图销毁/场景切换:静默 */ }
        }

        internal async UniTask ShowAsync(object args)
        {
            try
            {
                IsShown = true;
                gameObject.SetActive(true);
                await OnShow(args);
            }
            catch (OperationCanceledException) { /* 视图销毁/场景切换:静默 */ }
        }

        internal async UniTask HideAsync()
        {
            try
            {
                IsShown = false;
                await OnHide();
                gameObject.SetActive(false);
            }
            catch (OperationCanceledException) { /* 视图销毁/场景切换:静默 */ }
        }

        internal async UniTask DestroyAsync()
        {
            try { await OnDestroy(); }
            catch (OperationCanceledException) { /* 视图销毁/场景切换:静默 */ }
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject); // EditMode 测试/预览安全
        }

        internal async UniTask RefreshAsync() => await OnRefresh();
    }
}
