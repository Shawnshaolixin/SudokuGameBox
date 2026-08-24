using System.Collections.Generic;
using Box.Services;
using UnityEngine;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 粒子特效池(Phase 8 体验打磨:特效系统)。
    /// 从 Addressables 加载粒子贴图(Art/Effects/Particles/*)并池化 ParticleSystem 复用
    /// (避免频繁 Instantiate/Destroy);播放完由 FxSlot 自动回池。
    /// 渲染层方案:粒子 renderer 的 sortingOrder=9000,高于全部 overlay UI canvas(100~700),
    /// 保证粒子绘制在 UI 之上(overlay 渲染仲裁统一按 sortingOrder 排序,同一体系);
    /// 播放坐标传 UI 世界坐标(overlay canvas 单位=屏幕像素,格子 transform.position 即屏幕位置)。
    /// shader 用内置管线 Legacy Particles/Alpha Blended(项目无 URP,GraphicsSettings 为内置管线)。
    /// 播放点:填数(star_01 蓝)、提示(spark_01 金)、胜利(star_04+spark_01 双爆发)。
    /// 错误处理:贴图加载失败缓存标记,回调 null 且不再重复触发加载(防 LogWarning 刷屏)。
    /// </summary>
    public static class FxPool
    {
        const int SortOrder = 9000; // 高于 UILayer 最高层(700):粒子绘制在所有 overlay UI 之上

        // 播放点贴图地址(RegisterArtAssets 注册,Art/Effects/Particles/* 去扩展名)
        public const string StarTex = "Art/Effects/Particles/star_01";     // 填数反馈(小星星)
        public const string SparkTex = "Art/Effects/Particles/spark_01";   // 提示/胜利(火花)
        public const string StarBurstTex = "Art/Effects/Particles/star_04"; // 胜利(大星星)

        static readonly Dictionary<string, Queue<FxSlot>> _pools = new();          // 贴图地址 → 空闲槽位
        static readonly Dictionary<string, Material> _materials = new();            // 贴图地址 → 材质缓存
        static readonly Dictionary<string, List<System.Action<Material>>> _pending = new(); // 加载中的等待者(就绪后全部回调)
        static readonly HashSet<string> _failed = new();                            // 加载失败缓存:不重复触发加载

        static Transform _root;
        static bool _initialized;

        /// <summary>初始化特效根(幂等):DontDestroyOnLoad 常驻,跨场景(主菜单→对局)特效可用。</summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            var go = new GameObject("FxRoot");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
        }

        /// <summary>
        /// 单点爆发(填数/提示反馈)。
        /// 贴图未加载完成时登记等待,就绪后立即在调用位置播放(回调保证执行一次:成功 Material 或 null)。
        /// </summary>
        public static void PlayBurst(string textureAddress, Vector3 worldPos, int count = 12, float scale = 1f, Color? tint = null)
        {
            if (!_initialized) Init();
            EnsureMaterial(textureAddress, material =>
            {
                if (material == null) return; // 加载失败(已缓存标记,不再重试)
                var slot = Rent(textureAddress);
                slot.Play(worldPos, count, scale,
                    tint ?? new Color(0.75f, 0.88f, 1f), material);
            });
        }

        /// <summary>胜利庆祝:星星大爆发 + 火花四射(双层叠加,中心为传入的屏幕/棋盘中心)。</summary>
        public static void Celebrate(Vector3 center)
        {
            PlayBurst(StarBurstTex, center, 36, 1.4f, new Color(1f, 0.85f, 0.35f)); // 金色大星
            PlayBurst(SparkTex, center, 64, 1.6f, new Color(1f, 1f, 1f));            // 白色火花
        }

        // ---- 池管理 ----

        /// <summary>取/建材质:缓存命中直接回调;加载中登记等待;失败缓存标记后回调 null。</summary>
        static void EnsureMaterial(string address, System.Action<Material> onReady)
        {
            if (_materials.TryGetValue(address, out var mat))
            {
                onReady(mat);
                return;
            }
            if (_failed.Contains(address))
            {
                onReady(null); // 已确认失败:不再触发加载
                return;
            }
            if (_pending.TryGetValue(address, out var waiters))
            {
                waiters.Add(onReady); // 加载中:登记等待,就绪后统一回调
                return;
            }

            var list = new List<System.Action<Material>> { onReady };
            _pending[address] = list;
            ServiceLocator.Assets?.LoadAsset<Texture2D>(address, tex =>
            {
                _pending.Remove(address);
                if (tex == null)
                {
                    _failed.Add(address); // 失败缓存:后续调用直接回调 null,防重复加载刷 LogWarning
                    foreach (var cb in list) cb(null);
                    return;
                }
                var m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
                if (m == null || m.shader == null)
                {
                    Object.Destroy(m);
                    Debug.LogWarning("[Fx] 粒子 shader 缺失:Legacy Shaders/Particles/Alpha Blended");
                    _failed.Add(address);
                    foreach (var cb in list) cb(null);
                    return;
                }
                m.mainTexture = tex; // Kenney 白色图形贴图,色相由 startColor 乘算上色
                _materials[address] = m;
                foreach (var cb in list) cb(m);
            });
        }

        /// <summary>取空闲槽位;池空则新建(惰性扩容:峰值并发后不再创建,粒子寿命短,空闲槽位自然回落)。</summary>
        static FxSlot Rent(string address)
        {
            if (_pools.TryGetValue(address, out var q) && q.Count > 0) return q.Dequeue();

            var go = new GameObject("Fx-" + address.Substring(address.LastIndexOf('/') + 1), typeof(ParticleSystem));
            go.transform.SetParent(_root, false);
            var slot = go.AddComponent<FxSlot>();
            slot.PoolKey = address; // 回池键:直接引用(Rent 与 Release 之间一对一)

            var ps = slot.System;
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 世界坐标:播放中不受父级移动影响
            main.stopAction = ParticleSystemStopAction.Callback;        // 播放完 → OnParticleSystemStopped → 回池
            main.maxParticles = 256;

            var em = ps.emission;
            em.rateOverTime = 0f; // 只用手动 burst(爆发型)

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f; // 小半径球面发射:粒子向四周散开

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.3f))); // 先涨后缩

            slot.Renderer.sortingOrder = SortOrder; // 关键:高于全部 UI canvas → 粒子绘制在 UI 之上

            go.SetActive(false); // 入池隐藏
            return slot;
        }

        /// <summary>FxSlot 播放完毕回调:停用并按 PoolKey 回池(O(1))。</summary>
        internal static void Release(FxSlot slot)
        {
            slot.gameObject.SetActive(false);
            if (_pools.TryGetValue(slot.PoolKey, out var q)) q.Enqueue(slot);
            else Object.Destroy(slot.gameObject); // 防御:池被清空(理论不可达)
        }
    }
}
