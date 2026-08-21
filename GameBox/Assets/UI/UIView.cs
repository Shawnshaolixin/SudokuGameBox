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

        protected virtual void Awake()
        {
            // 安全区由 SafeAreaFitter 托管:横竖屏/键盘变化时动态刷新(§3.5 第 8 条 / 11 文档 §10.1)
            if (GetComponent<SafeAreaFitter>() == null)
                gameObject.AddComponent<SafeAreaFitter>();
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
        internal async UniTask DestroyAsync()
        {
            await OnDestroy();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject); // EditMode 测试/预览安全
        }
        internal async UniTask RefreshAsync() => await OnRefresh();
    }
}
