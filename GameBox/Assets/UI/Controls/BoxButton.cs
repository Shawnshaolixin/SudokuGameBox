using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Box.UI
{
    /// <summary>按钮(10 文档 §8 3-2):点击回调注册 + 防重入 + 按下缩放反馈(D-15 自研补间)。</summary>
    /// <remarks>单类文件(Unity 约定):多类同文件会导致非首类 prefab 序列化为 missing script。</remarks>
    public sealed class BoxButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        const float PressScale = 0.95f;   // 按下目标缩放
        const float PressDur = 0.08f;     // 按下动画时长
        const float ReleaseDur = 0.10f;   // 还原动画时长

        readonly List<Action> _clicks = new();
        Button _button;
        bool _firing;
        CancellationTokenSource _tweenCts;

        /// <summary>按下缩放反馈开关:棋盘格等密集网格设为 false,防缩放观感(露出背景)。</summary>
        public bool PressFeedbackEnabled = true;

        void Awake()
        {
            _button = GetComponent<Button>();
            if (_button != null) _button.onClick.AddListener(Fire);
        }

        public void OnClick(Action callback)
        {
            if (callback != null) _clicks.Add(callback);
        }

        public void SetInteractable(bool value)
        {
            if (_button != null) _button.interactable = value;
        }

        // ---- 按下反馈(仅动 localScale,不动 sizeDelta;新动画前取消旧的防叠加) ----

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!PressFeedbackEnabled) return;
            _tweenCts?.Cancel();
            _tweenCts = new CancellationTokenSource();
            BoxTween.ScaleTo(transform, 1f, PressScale, PressDur, _tweenCts.Token).Forget();
        }

        public void OnPointerUp(PointerEventData eventData) => RestoreScale();

        public void OnPointerExit(PointerEventData eventData) => RestoreScale();

        void RestoreScale()
        {
            if (!PressFeedbackEnabled) return;
            _tweenCts?.Cancel();
            _tweenCts = new CancellationTokenSource();
            BoxTween.ScaleTo(transform, PressScale, 1f, ReleaseDur, _tweenCts.Token).Forget();
        }

        void Fire()
        {
            if (_firing) return; // 防重入:回调内再次点击不递归
            _firing = true;
            foreach (var cb in _clicks)
            {
                try { cb(); }
                catch (Exception e) { Debug.LogWarning($"[UIKit] 点击回调异常: {e.Message}"); }
            }
            _firing = false;
        }
    }
}
