using System.Collections.Generic;
using Box.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// UI 特效池(Phase 9 真机修复:渲染方案从 ParticleSystem 改为 UGUI 粒子)。
    /// 背景:原实现用 ParticleSystem 走相机渲染,但播放坐标是 overlay UI 世界坐标(像素级),
    /// 主相机为正交相机(size=5,视野仅 ±5 单位)根本看不到该坐标范围;
    /// 且 ScreenSpaceOverlay 是独立渲染通道,永远绘制在相机渲染(含粒子)之上
    /// ——双重原因导致真机填数/提示/胜利特效全部不可见。
    /// 新方案:自建常驻 ScreenSpaceOverlay Canvas(sortingOrder=1000,高于全部 UI 层 100~700),
    /// 池化 RawImage 单粒子,与 UI 同通道渲染 → 特效必定显示在所有 UI 之上;
    /// 坐标用 RectTransformUtility 在主 canvas 世界坐标 ↔ 屏幕像素 ↔ FxRoot 世界坐标间换算,
    /// 不依赖任何 canvas 缩放参数(跨场景主 canvas 配置变化也稳)。
    /// 贴图从 Addressables 加载(Art/Effects/Particles/*),失败缓存标记防重复加载刷日志。
    /// 播放点:填数(star_01 蓝)、提示(spark_01 金)、胜利(star_04+spark_01 双爆发)。
    /// </summary>
    public static class FxPool
    {
        const int SortOrder = 1000; // 高于 UILayer 最高层(700):特效绘制在所有 UI 之上

        // 播放点贴图地址(Module_Sudoku 组注册,Sudoku/Fx/* 去扩展名;2026-08-30 资源归属分离后移入模块组)
        // 注:2026-08-29 资产命名契约重命名(加 _particle 后缀,CI-2 资产校验白名单),地址同步
        public const string StarTex = "Sudoku/Fx/star_01_particle";     // 填数反馈(小星星)
        public const string SparkTex = "Sudoku/Fx/spark_01_particle";   // 提示/胜利(火花)
        public const string StarBurstTex = "Sudoku/Fx/star_04_particle"; // 胜利(大星星)

        static readonly Dictionary<string, Queue<FxSlot>> _pools = new();          // 贴图地址 → 空闲粒子槽
        static readonly Dictionary<string, Texture2D> _textures = new();            // 贴图地址 → 纹理缓存
        static readonly Dictionary<string, List<System.Action<Texture2D>>> _pending = new(); // 加载中的等待者
        static readonly HashSet<string> _failed = new();                            // 加载失败缓存:不重复触发加载

        static Transform _root; // FxRoot(常驻 overlay canvas)的 transform
        static bool _initialized;

        /// <summary>初始化特效根(幂等):DontDestroyOnLoad 常驻 overlay canvas,跨场景(主菜单→对局)特效可用。</summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            var go = new GameObject("FxRoot");
            Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortOrder; // 关键:独立 overlay canvas,特效永远在 UI 之上
            _root = go.transform;
        }

        /// <summary>
        /// 单点爆发(填数/提示反馈)。
        /// 贴图未加载完成时登记等待,就绪后立即在调用位置播放(回调保证执行一次:成功纹理或 null)。
        /// 位置传主 UI canvas 世界坐标(格子 transform.position),内部自动换算到 FxRoot 坐标系。
        /// </summary>
        public static void PlayBurst(string textureAddress, Vector3 worldPos, int count = 12, float scale = 1f, Color? tint = null)
        {
            if (!_initialized) Init();
            EnsureTexture(textureAddress, tex =>
            {
                if (tex == null) return; // 加载失败(已缓存标记,不再重试)
                var color = tint ?? new Color(0.75f, 0.88f, 1f);
                var fxPos = ToFxRoot(worldPos); // 换算一次,所有粒子共用起点
                for (int i = 0; i < count; i++)
                {
                    var slot = Rent(textureAddress);
                    slot.Play(fxPos, scale, color, tex);
                }
            });
        }

        /// <summary>胜利庆祝:星星大爆发 + 火花四射(双层叠加,中心为传入的棋盘中心)。</summary>
        public static void Celebrate(Vector3 center)
        {
            PlayBurst(StarBurstTex, center, 36, 1.4f, new Color(1f, 0.85f, 0.35f)); // 金色大星
            PlayBurst(SparkTex, center, 64, 1.6f, new Color(1f, 1f, 1f));            // 白色火花
        }

        // ---- 坐标换算 ----

        /// <summary>
        /// 主 canvas 世界坐标(调用方格子位置) → FxRoot canvas 世界坐标:
        /// 先转屏幕像素(overlay 模式传 null 相机),再转入 FxRoot 坐标系。
        /// 自动适配主 canvas 的 CanvasScaler 缩放,不依赖任何固定参数。
        /// </summary>
        static Vector3 ToFxRoot(Vector3 worldPos)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(null, worldPos);
            RectTransformUtility.ScreenPointToWorldPointInRectangle(
                (RectTransform)_root, screen, null, out var fxWorld);
            return fxWorld;
        }

        // ---- 贴图加载 ----

        /// <summary>取/建纹理缓存:命中直接回调;加载中登记等待;失败缓存标记后回调 null。</summary>
        static void EnsureTexture(string address, System.Action<Texture2D> onReady)
        {
            if (_textures.TryGetValue(address, out var tex))
            {
                onReady(tex);
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

            var list = new List<System.Action<Texture2D>> { onReady };
            _pending[address] = list;
            ServiceLocator.Assets?.LoadAsset<Texture2D>(address, tex2 =>
            {
                _pending.Remove(address);
                if (tex2 == null)
                {
                    _failed.Add(address); // 失败缓存:后续调用直接回调 null,防重复加载刷 LogWarning
                    foreach (var cb in list) cb(null);
                    return;
                }
                _textures[address] = tex2; // Kenney 白色图形贴图,色相由 tint 上色
                foreach (var cb in list) cb(tex2);
            });
        }

        // ---- 池管理 ----

        /// <summary>取空闲粒子槽;池空则新建(惰性扩容:特效寿命短,峰值后空闲槽位自然回落)。</summary>
        static FxSlot Rent(string address)
        {
            if (_pools.TryGetValue(address, out var q) && q.Count > 0) return q.Dequeue();

            var go = new GameObject("Fx-" + address.Substring(address.LastIndexOf('/') + 1),
                typeof(RectTransform), typeof(RawImage)); // RectTransform 经构造参数替换默认 Transform
            go.transform.SetParent(_root, false);
            var slot = go.AddComponent<FxSlot>();
            slot.PoolKey = address; // 回池键:直接引用(Rent 与 Release 之间一对一)

            go.SetActive(false); // 入池隐藏
            return slot;
        }

        /// <summary>FxSlot 动画播放完毕回调:停用并按 PoolKey 回池(O(1))。</summary>
        internal static void Release(FxSlot slot)
        {
            slot.StopAllCoroutines(); // 防御:动画协程应已结束,此处防异常复用
            slot.gameObject.SetActive(false);
            if (_pools.TryGetValue(slot.PoolKey, out var q)) q.Enqueue(slot);
            else Object.Destroy(slot.gameObject); // 防御:池被清空(理论不可达)
        }
    }
}
