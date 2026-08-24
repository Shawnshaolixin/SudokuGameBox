using UnityEngine;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 粒子池槽位(Phase 8 体验打磨:特效系统)。
    /// 挂在池化 ParticleSystem 的 GameObject 上:
    /// main.stopAction=Callback 时,粒子全部播放完毕后引擎调用 OnParticleSystemStopped → 自动回池复用。
    /// 性能:颜色渐变(alpha 淡出)为静态模板,创建时配置一次;每次播放只设 startColor(Color struct,零堆分配)
    /// 且 burst 数量不变时跳过重设(避免频繁填数连点时分配 Burst 数组)。
    /// </summary>
    public sealed class FxSlot : MonoBehaviour
    {
        public ParticleSystem System { get; private set; }
        public ParticleSystemRenderer Renderer { get; private set; }

        /// <summary>所属池的贴图地址(Release 时 O(1) 回池,避免名字匹配之类的脆弱做法)。</summary>
        internal string PoolKey;

        // 静态 alpha 淡出渐变模板(白通道):最终粒子色 = startColor × 本渐变(乘算),色相由 startColor 决定
        static readonly Gradient AlphaFade = BuildAlphaFade();

        int _lastCount = -1; // 上次播放的 burst 数量缓存:同参跳过重设

        void Awake()
        {
            System = GetComponent<ParticleSystem>();
            Renderer = GetComponent<ParticleSystemRenderer>();
            // 颜色渐变一次性配置(colorOverLifetime 为乘算:白渐变只负责 alpha 淡出)
            var col = System.colorOverLifetime;
            col.enabled = true;
            col.color = AlphaFade;
        }

        void OnParticleSystemStopped()
        {
            FxPool.Release(this); // 播放完毕自动回池(不依赖外部计时/协程)
        }

        /// <summary>
        /// 配置并播放一次爆发:位置、burst 数量、大小/速度缩放、tint 上色与材质。
        /// 位置传 UI 世界坐标(overlay 下与屏幕像素 1:1 对齐,格子 transform.position 即屏幕位置)。
        /// </summary>
        public void Play(Vector3 worldPos, int count, float scale, Color tint, Material material)
        {
            transform.position = worldPos;
            gameObject.SetActive(true);

            var main = System.main;
            main.startSize = 0.25f * scale;
            main.startSpeed = 3.2f * scale;
            main.startLifetime = 0.55f;
            main.startColor = tint; // 上色(零分配:MinMaxGradient 隐式转换自 Color)

            if (_lastCount != count) // burst 数量变化才重设(避免连点高频下每帧分配数组)
            {
                _lastCount = count;
                var em = System.emission;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            }

            Renderer.sharedMaterial = material;
            System.Play();
        }

        static Gradient BuildAlphaFade()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 0.65f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0f, 1f) });
            return g;
        }
    }
}
