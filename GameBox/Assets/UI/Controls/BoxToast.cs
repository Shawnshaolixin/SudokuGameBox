using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Box.UI
{
    /// <summary>
    /// 轻量 Toast(Phase 9 真机反馈:广告/IAP 静默失败时给用户可见提示)。
    /// 纯代码运行时创建,不依赖 prefab/Addressables(避免 UI 资产链路问题);
    /// 独立 ScreenSpaceOverlay Canvas + 高 SortingOrder,悬浮在所有 UI 之上。
    /// 单实例复用:新 Toast 顶掉旧的;淡入 150ms → 停留 → 淡出 250ms 后隐藏。
    /// </summary>
    public static class BoxToast
    {
        const int SortingOrder = 20000;
        const float ShowDuration = 2f;

        static GameObject _root;          // 根 Canvas(懒创建,常驻复用)
        static Image _bg;                 // 半透明黑底
        static TMP_Text _text;            // 提示文本(默认字体,已随包)
        static CancellationTokenSource _cts;

        /// <summary>显示 Toast 提示(自动创建画布;重复调用替换旧文案并重置计时)。</summary>
        public static void Show(string message, float duration = ShowDuration)
        {
            EnsureRoot();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _text.text = message;
            // 底宽跟随文本长度(限幅防溢出屏幕);高度固定
            float w = Mathf.Clamp(_text.preferredWidth + 96f, 240f, 900f);
            _bg.rectTransform.sizeDelta = new Vector2(w, 76f);
            _bg.color = new Color(0.08f, 0.08f, 0.1f, 0.82f);

            _root.SetActive(true);
            FadeAsync(1f, 0.15f, _cts.Token).Forget();
            HideAfterAsync(duration, _cts.Token).Forget();
        }

        // ---------- 内部:懒创建 UI ----------

        static void EnsureRoot()
        {
            if (_root != null) return;

            _root = new GameObject("BoxToastCanvas");
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // 与项目 UI 基准一致
            _root.AddComponent<CanvasGroup>(); // 透明度动画载体

            // 背景(居中偏下 20%)
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(_root.transform, false);
            _bg = bgGo.AddComponent<Image>();
            _bg.rectTransform.anchorMin = new Vector2(0.5f, 0.18f);
            _bg.rectTransform.anchorMax = new Vector2(0.5f, 0.18f);
            _bg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _bg.rectTransform.sizeDelta = new Vector2(400, 76);
            _bg.color = new Color(0.08f, 0.08f, 0.1f, 0.82f);

            // 文本(默认字体 = Regular Subset SDF,已进包)
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_bg.transform, false);
            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 30;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = Color.white;
            _text.rectTransform.anchorMin = Vector2.zero;
            _text.rectTransform.anchorMax = Vector2.one;
            _text.rectTransform.offsetMin = new Vector2(24, 8);
            _text.rectTransform.offsetMax = new Vector2(-24, -8);

            _root.SetActive(false);
        }

        static async UniTask FadeAsync(float targetAlpha, float duration, CancellationToken token)
        {
            var cg = _root.GetComponent<CanvasGroup>();
            float start = cg.alpha;
            float t = 0f;
            while (t < 1f && !token.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime / duration; // 不受暂停影响
                cg.alpha = Mathf.Lerp(start, targetAlpha, t);
                await UniTask.Yield(token);
            }
            cg.alpha = targetAlpha;
        }

        static async UniTask HideAfterAsync(float delay, CancellationToken token)
        {
            await UniTask.Delay((int)(delay * 1000), DelayType.UnscaledDeltaTime, cancellationToken: token);
            if (token.IsCancellationRequested) return;
            await FadeAsync(0f, 0.25f, token);
            if (token.IsCancellationRequested) return;
            _root.SetActive(false);
        }
    }
}
