using System;
using System.Collections.Generic;
using Box.Services;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// 路由与栈管理(11 文档 §3.5 第 5/7 条)。
    /// PushAsync/PopAsync/ReplaceAsync;HUD/Toast 等非栈层只显示不入栈。
    /// 带过渡锁防连点;弹窗/窗口层关闭时隐藏下层并触发 ViewPopped 事件(供 PopupArbiter 串行化)。
    /// </summary>
    public sealed class UIRouter
    {
        public event Action<UIView> ViewPopped;

        /// <summary>UI 埋点(§3.5 第 7 条,04 文档 §6.1):OnShow 自动上报 ui_show + view 参数;null 则跳过。
        /// 事件名固定合规(view.Id 是资源路径,含 / 与大小写,不能拼事件名 —— 2026-09-05 埋点清理立规)。</summary>
        public IAnalyticsService Analytics { get; set; }

        /// <summary>当前栈内(Window/Popup)视图数,仅供测试与调试。</summary>
        public int StackCount => _stack.Count;

        readonly UILayerManager _layers = new();
        readonly IViewLoader _loader;
        readonly Dictionary<string, UIView> _cache = new();
        readonly Stack<UIView> _stack = new();
        bool _transitioning;

        public UIRouter(IViewLoader loader) => _loader = loader;

        static bool IsStacked(UILayer layer) => layer >= UILayer.Window;

        /// <summary>推入视图:key 即 prefab 路径;已在缓存则复用并走 OnShow。</summary>
        public async UniTask<TView> PushAsync<TView>(string key, object args = null) where TView : UIView
        {
            if (_transitioning) return null;
            _transitioning = true;
            try
            {
                UIView view;
                if (_cache.TryGetValue(key, out view))
                {
                    _cache.Remove(key);
                }
                else
                {
                    var go = await _loader.LoadAsync(key);
                    if (go == null) return null;
                    view = go.GetComponent<TView>();
                    if (view == null)
                    {
                        UnityEngine.Object.Destroy(go);
                        return null;
                    }
                    view.Id = key;
                    var canvas = _layers.GetCanvas(view.Layer);
                    if (canvas != null) go.transform.SetParent(canvas.transform, false);
                    await view.CreateAsync();
                }

                if (IsStacked(view.Layer))
                {
                    if (_stack.Count > 0) await _stack.Peek().HideAsync();
                    _stack.Push(view);
                }
                await view.ShowAsync(args); // 非栈层(HUD/Toast)只显示不入栈
                Analytics?.LogEvent("ui_show", "view", view.Id); // UI 埋点自动化:固定事件名 + view 路径参数(04 §6.1)
                return (TView)view;
            }
            finally
            {
                _transitioning = false;
            }
        }

        /// <summary>弹出栈顶视图;非栈层视图调用无效。</summary>
        public async UniTask PopAsync()
        {
            if (_stack.Count == 0) return;
            var view = _stack.Pop();
            await view.HideAsync();
            if (_stack.Count > 0) await _stack.Peek().ShowAsync(null);
            ViewPopped?.Invoke(view);
            CacheOrDestroy(view);
        }

        /// <summary>弹到指定 key(不含自身)。</summary>
        public async UniTask PopToAsync(string key)
        {
            while (_stack.Count > 0 && _stack.Peek().Id != key)
                await PopAsync();
        }

        /// <summary>替换栈顶(同层替换,常用于 确认→结算 流程);返回替换后的视图。</summary>
        public async UniTask<TView> ReplaceAsync<TView>(string key, object args = null) where TView : UIView
        {
            if (_stack.Count == 0)
                return await PushAsync<TView>(key, args);
            var top = _stack.Pop();
            await top.HideAsync();
            ViewPopped?.Invoke(top);
            CacheOrDestroy(top);
            return await PushAsync<TView>(key, args);
        }

        /// <summary>Android 返回键入口(由 UIService 的 BackKeyRunner 逐帧调用)。</summary>
        public async UniTask HandleBackAsync()
        {
            if (_stack.Count > 1 || (_stack.Count == 1 && _stack.Peek().Layer == UILayer.Popup))
                await PopAsync();
        }

        void CacheOrDestroy(UIView view)
        {
            switch (view.Cache)
            {
                case UIView.CacheMode.Keep:
                    _cache[view.Id] = view;
                    break;
                case UIView.CacheMode.CacheN:
                    if (_cache.Count < view.CacheCount) _cache[view.Id] = view;
                    else view.DestroyAsync().Forget();
                    break;
                default:
                    view.DestroyAsync().Forget();
                    break;
            }
        }
    }
}
