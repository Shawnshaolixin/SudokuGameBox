using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// 弹窗互斥队列(11 文档 §3.5 第 6 条)。
    /// 同一时刻仅一个模态弹窗;后续请求排队,当前弹窗被关闭(Pop)后自动展示下一个。
    /// 关闭由路由的 ViewPopped 事件驱动,不轮询。
    /// </summary>
    public sealed class PopupArbiter
    {
        readonly UIRouter _router;
        readonly Queue<Item> _queue = new();
        bool _busy;

        sealed class Item
        {
            public string Key;
            public object Args;
            public UniTaskCompletionSource Done = new();
        }

        public PopupArbiter(UIRouter router) => _router = router;

        /// <summary>排队展示弹窗;返回的 Task 在该弹窗被关闭后完成。</summary>
        public UniTask ShowAsync(string key, object args = null)
        {
            var item = new Item { Key = key, Args = args };
            _queue.Enqueue(item);
            Pump();
            return item.Done.Task;
        }

        void Pump()
        {
            if (_busy || _queue.Count == 0) return;
            var item = _queue.Dequeue();
            _busy = true;
            ShowOne(item).Forget();
        }

        async UniTaskVoid ShowOne(Item item)
        {
            try
            {
                var view = await _router.PushAsync<UIView>(item.Key, item.Args);
                if (view == null)
                {
                    Debug.LogWarning($"[UIKit] 弹窗资源缺失: {item.Key}");
                    return;
                }

                var tcs = new UniTaskCompletionSource();
                void OnPopped(UIView popped)
                {
                    if (popped != view) return;
                    _router.ViewPopped -= OnPopped;
                    tcs.TrySetResult();
                }
                _router.ViewPopped += OnPopped;
                await tcs.Task;
            }
            finally
            {
                _busy = false;
                Pump();
            }
        }
    }
}
