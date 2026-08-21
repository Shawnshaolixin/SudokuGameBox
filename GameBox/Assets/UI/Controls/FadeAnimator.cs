using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.UI
{
    /// <summary>动画薄封装(§3.5 3-1):CanvasGroup 淡入淡出,UniTask 化。</summary>
    public sealed class FadeAnimator : MonoBehaviour
    {
        CanvasGroup _group;

        void Awake() => _group = GetComponent<CanvasGroup>();

        public async UniTask FadeInAsync(float duration = 0.2f) => await FadeTo(1f, duration);
        public async UniTask FadeOutAsync(float duration = 0.2f) => await FadeTo(0f, duration);

        async UniTask FadeTo(float target, float duration)
        {
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            var from = _group.alpha;
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                _group.alpha = Mathf.Lerp(from, target, Mathf.Min(t / duration, 1f));
                await UniTask.Yield();
            }
            _group.alpha = target;
        }
    }
}
