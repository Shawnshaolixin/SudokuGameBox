using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Box.UI
{
    /// <summary>
    /// 自研补间(D-15,不引入 DOTween/PrimeTween):
    /// 纯曲线静态函数(单测覆盖) + UniTask 动画(与 FadeAnimator 同模式)。
    /// 动画全部可取消:调用方持有 CancellationTokenSource,新动画启动前 Cancel 旧动画。
    /// </summary>
    public static class BoxTween
    {
        // ============================================================
        // 纯曲线(0→1 输入,单测可断言)
        // ============================================================

        /// <summary>EaseOutBack:t=0→0, t=1→1,中段过冲(回弹感,弹入动画用)。</summary>
        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        /// <summary>EaseOutCubic:先快后慢(按钮按下/还原)。</summary>
        public static float EaseOutCubic(float t)
        {
            t -= 1f;
            return t * t * t + 1f;
        }

        /// <summary>EaseInOutCubic:慢-快-慢(位移/淡入淡出)。</summary>
        public static float EaseInOutCubic(float t)
        {
            if (t < 0.5f) return 4f * t * t * t;
            t = 2f * t - 2f;
            return 0.5f * t * t * t + 1f;
        }

        // ============================================================
        // 动画(Transform/CanvasGroup,async 可取消)
        // ============================================================

        /// <summary>缩放到指定值(EaseOutCubic,不越界)。</summary>
        public static async UniTask ScaleTo(Transform target, float from, float to, float duration, CancellationToken ct = default)
        {
            if (target == null || duration <= 0f) return;
            if (Time.deltaTime <= 0f) { target.localScale = Vector3.one * to; return; } // EditMode/暂停:直接到位
            var elapsed = 0f;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                float dt = Time.deltaTime;
                if (dt <= 0f) break; // 运行中暂停/无帧步进:立即结束,防 elapsed 永不增长挂起调用链(曾致 Router PushAsync 永久卡 _transitioning)
                elapsed += dt;
                target.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, EaseOutCubic(Mathf.Min(elapsed / duration, 1f)));
                await UniTask.Yield(ct);
                if (target == null) return; // 场景切换销毁:静默退出
            }
            target.localScale = Vector3.one * to;
        }

        /// <summary>弹入缩放(EaseOutBack,中段过冲有回弹感;弹窗/视图入场用)。</summary>
        public static async UniTask ScalePulse(Transform target, float from, float to, float duration, CancellationToken ct = default)
        {
            if (target == null || duration <= 0f) return;
            if (Time.deltaTime <= 0f) { target.localScale = Vector3.one * to; return; } // EditMode/暂停:直接到位
            var elapsed = 0f;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                float dt = Time.deltaTime;
                if (dt <= 0f) break; // 运行中暂停/无帧步进:立即结束,防 elapsed 永不增长挂起调用链(曾致 Router PushAsync 永久卡 _transitioning)
                elapsed += dt;
                target.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, EaseOutBack(Mathf.Min(elapsed / duration, 1f)));
                await UniTask.Yield(ct);
                if (target == null) return; // 场景切换销毁:静默退出
            }
            target.localScale = Vector3.one * to;
        }

        /// <summary>CanvasGroup 透明度渐变(自动补组件)。</summary>
        public static async UniTask FadeTo(GameObject target, float from, float to, float duration, CancellationToken ct = default)
        {
            if (target == null || duration <= 0f) return;
            var group = target.GetComponent<CanvasGroup>();
            if (group == null) group = target.AddComponent<CanvasGroup>();
            if (Time.deltaTime <= 0f) { group.alpha = to; return; } // EditMode/暂停:直接到位
            group.alpha = from;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                float dt = Time.deltaTime;
                if (dt <= 0f) break; // 运行中暂停/无帧步进:立即结束,防 elapsed 永不增长挂起调用链(曾致 Router PushAsync 永久卡 _transitioning)
                elapsed += dt;
                group.alpha = Mathf.LerpUnclamped(from, to, EaseInOutCubic(Mathf.Min(elapsed / duration, 1f)));
                await UniTask.Yield(ct);
                if (target == null) return; // 场景切换销毁:静默退出
            }
            group.alpha = to;
        }

        /// <summary>锚点位移(EaseInOutCubic)。</summary>
        public static async UniTask MoveTo(RectTransform target, Vector2 from, Vector2 to, float duration, CancellationToken ct = default)
        {
            if (target == null || duration <= 0f) return;
            if (Time.deltaTime <= 0f) { target.anchoredPosition = to; return; } // EditMode/暂停:直接到位
            var elapsed = 0f;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                float dt = Time.deltaTime;
                if (dt <= 0f) break; // 运行中暂停/无帧步进:立即结束,防 elapsed 永不增长挂起调用链(曾致 Router PushAsync 永久卡 _transitioning)
                elapsed += dt;
                target.anchoredPosition = Vector2.LerpUnclamped(from, to, EaseInOutCubic(Mathf.Min(elapsed / duration, 1f)));
                await UniTask.Yield(ct);
                if (target == null) return; // 场景切换销毁:静默退出
            }
            target.anchoredPosition = to;
        }

        /// <summary>UI 元素颜色渐变(Graphic:Image/Text 等,EaseInOutCubic;2026-08-30 单元凑齐扩散动效用)。</summary>
        public static async UniTask ColorTo(Graphic target, Color from, Color to, float duration, CancellationToken ct = default)
        {
            if (target == null || duration <= 0f) return;
            if (Time.deltaTime <= 0f) { target.color = to; return; } // EditMode/暂停:直接到位
            target.color = from;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                float dt = Time.deltaTime;
                if (dt <= 0f) break; // 运行中暂停/无帧步进:立即结束,防 elapsed 永不增长挂起调用链
                elapsed += dt;
                target.color = Color.LerpUnclamped(from, to, EaseInOutCubic(Mathf.Min(elapsed / duration, 1f)));
                await UniTask.Yield(ct);
                if (target == null) return; // 场景切换销毁:静默退出
            }
            target.color = to;
        }

        /// <summary>抖动(本地位置,振幅随进度衰减,结束归位)。</summary>
        public static async UniTask Shake(Transform target, float duration, float magnitude, CancellationToken ct = default)
        {
            if (target == null || duration <= 0f) return;
            if (Time.deltaTime <= 0f) return; // EditMode/暂停:直接归位(无位移发生)
            var origin = target.localPosition;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                float dt = Time.deltaTime;
                if (dt <= 0f) break; // 运行中暂停/无帧步进:立即结束,防 elapsed 永不增长挂起调用链(曾致 Router PushAsync 永久卡 _transitioning)
                elapsed += dt;
                float decay = 1f - Mathf.Min(elapsed / duration, 1f);
                target.localPosition = origin + new Vector3(
                    Random.Range(-1f, 1f) * magnitude * decay,
                    Random.Range(-1f, 1f) * magnitude * decay, 0f);
                await UniTask.Yield(ct);
                if (target == null) return; // 场景切换销毁:静默退出
            }
            target.localPosition = origin;
        }
    }
}
