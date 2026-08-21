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

        RectTransform _rect;

        protected virtual void Awake()
        {
            _rect = GetComponent<RectTransform>();
            ApplySafeArea();
        }

        // ---- 生命周期(由 UIRouter 调用,子类覆写) ----

        protected virtual UniTask OnCreate() => UniTask.CompletedTask;
        protected virtual UniTask OnShow(object args) => UniTask.CompletedTask;
        protected virtual UniTask OnHide() => UniTask.CompletedTask;
        protected virtual UniTask OnDestroy() => UniTask.CompletedTask;
        protected virtual UniTask OnRefresh() => UniTask.CompletedTask;

        internal async UniTask CreateAsync() => await OnCreate();
        internal async UniTask ShowAsync(object args) { IsShown = true; gameObject.SetActive(true); await OnShow(args); }
        internal async UniTask HideAsync() { IsShown = false; await OnHide(); gameObject.SetActive(false); }
        internal async UniTask DestroyAsync() { await OnDestroy(); Destroy(gameObject); }
        internal async UniTask RefreshAsync() => await OnRefresh();

        /// <summary>统一安全区适配:刘海/挖孔下自动缩边。</summary>
        void ApplySafeArea()
        {
            if (_rect == null) return;
            var safe = Screen.safeArea;
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            var inset = new Vector2(
                (safe.xMin - screen.xMin) / screen.width,
                (safe.yMin - screen.yMin) / screen.height);
            _rect.anchorMin = new Vector2(inset.x, inset.y);
            _rect.anchorMax = new Vector2(1f - (screen.xMax - safe.xMax) / screen.width, 1f - (screen.yMax - safe.yMax) / screen.height);
        }
    }
}
