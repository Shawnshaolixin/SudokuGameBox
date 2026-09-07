using System.Collections.Generic;
using Box.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 胜利彩带(UGUI 粒子版,2026-09-07)。
    /// 架构前提:整壳 UI 为 ScreenSpaceOverlay(见 UILayer.cs:47),相机渲染的 ParticleSystem
    /// 恒被 UI 盖住(数独 Phase-9 已踩,FxPool.cs:9-18 有注释)——故 _ConfettiTest 里用粒子系统
    /// 验证过的彩带效果(用户验收版)按同方案移植:RawImage 单粒子、overlay 同通道渲染,必在 UI 之上。
    /// 本类只做「入口 + 贴图缓存/加载」;动画与生命周期在 WinConfettiRig(子物体,见下)。
    /// 每局胜利最多播放一次 → 每帧几十个 RectTransform 直改,无需对象池;播完自毁。
    /// 贴图 = WaterSort/Fx/*(SkinImporter 自动入组,不套 sprite 预设):纯白 alpha 形状,
    /// 色相由 RawImage.color 乘 tint 随机,一张白图可出整族配色(与粒子版 StartColor 染色等价)。
    /// </summary>
    public static class WaterSortWinConfetti
    {
        // 三张贴图地址(形如 "WaterSort/Fx/{名}",SkinImporter 地址约定同 UI 皮肤)
        public const string RibbonAddress = "WaterSort/Fx/confetti_ribbon";
        public const string ChipAddress = "WaterSort/Fx/confetti_chip";
        public const string DotAddress = "WaterSort/Fx/confetti_dot";

        // 贴图缓存 / 加载中等待者 / 失败缓存(结构同 FxPool:命中直返、并发合流、失败防重刷日志)
        static readonly Dictionary<string, Texture2D> _textures = new();
        static readonly Dictionary<string, List<System.Action<Texture2D>>> _pending = new();
        static readonly HashSet<string> _failed = new();

        /// <summary>预热三张贴图(胜利弹层首次出现前调用,避免过关瞬间才异步加载)。</summary>
        public static void Prepare()
        {
            EnsureTexture(RibbonAddress, null);
            EnsureTexture(ChipAddress, null);
            EnsureTexture(DotAddress, null);
        }

        /// <summary>在胜利弹层内播一次彩带:origin 为弹层本地设计坐标(过场弹层覆盖整画布,即画布中心系坐标)。</summary>
        public static void Play(Transform overlayParent, Vector2 origin)
        {
            // 旧组清理:弹层复用场景下上一次播放可能残留(理论上已自毁,防御)
            var old = overlayParent.Find(WinConfettiRig.RootName);
            if (old != null) Object.Destroy(old.gameObject);

            // 根节点:锚定弹层中心系,坐标即 overlay 本地设计坐标
            var rootGo = new GameObject(WinConfettiRig.RootName, typeof(RectTransform));
            var rootRt = (RectTransform)rootGo.transform;
            rootRt.SetParent(overlayParent, false);
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = Vector2.zero;
            rootRt.anchoredPosition = origin;

            var rig = rootGo.AddComponent<WinConfettiRig>();
            rig.Root = rootRt;
            // 三族各自等贴图就绪后落片(已缓存则当帧同步落);失败族静默跳过
            rig.TrySpawnFamily(WinConfettiFamily.Ribbon);
            rig.TrySpawnFamily(WinConfettiFamily.Chip);
            rig.TrySpawnFamily(WinConfettiFamily.Dot);
        }

        /// <summary>取/建贴图缓存:命中直返;加载中登记等待;失败缓存标记。回调不保证在主线程以外,
        /// 参数 onReady 传 null 表示仅预热不等待(失败静默)。</summary>
        static void EnsureTexture(string address, System.Action<Texture2D> onReady)
        {
            if (_textures.TryGetValue(address, out var tex))
            {
                onReady?.Invoke(tex);
                return;
            }
            if (_failed.Contains(address))
            {
                onReady?.Invoke(null); // 已确认失败:直接回调 null,不再触发加载
                return;
            }
            if (_pending.TryGetValue(address, out var waiters))
            {
                if (onReady != null) waiters.Add(onReady); // 加载中:登记等待,就绪后统一回调
                return;
            }
            waiters = new List<System.Action<Texture2D>>();
            if (onReady != null) waiters.Add(onReady);
            _pending[address] = waiters;
            ServiceLocator.Assets?.LoadAsset<Texture2D>(address, loaded =>
            {
                _pending.Remove(address);
                if (loaded == null)
                {
                    _failed.Add(address); // 失败缓存:防后续重复触发加载刷 LogWarning
                    foreach (var cb in waiters) cb?.Invoke(null);
                    return;
                }
                _textures[address] = loaded;
                foreach (var cb in waiters) cb?.Invoke(loaded);
            });
        }

        /// <summary>内部入口:把 Rig 上等待贴图到货的族触发落片(公开给 Rig 同程序集调用)。</summary>
        internal static void EnsureTextureFor(string address, System.Action<Texture2D> onReady)
            => EnsureTexture(address, onReady);
    }

    /// <summary>一族粒子的静态调参(形状贴图 + 色系 + 数量 + 尺寸),数值照抄 _ConfettiTest 粒子调参。</summary>
    internal readonly struct WinConfettiFamily
    {
        public readonly string Address;    // 形状贴图地址(ribbon 长带 / chip 方片 / dot 圆片)
        public readonly Color ColorMin;    // 染色区间下限(粒子版 MinMaxGradient 下界)
        public readonly Color ColorMax;    // 染色区间上限
        public readonly int Count;         // 单次爆发片数
        public readonly Vector2 SizePx;    // 尺寸范围(像素,由粒子 sizeMin~sizeMax × 240 换算)

        WinConfettiFamily(string address, Color min, Color max, int count, Vector2 sizePx)
        {
            Address = address;
            ColorMin = min;
            ColorMax = max;
            Count = count;
            SizePx = sizePx;
        }

        // 粒子版本各族 startSize 单位尺寸 × UnitToPx(240) → 像素
        static readonly float S = WinConfettiRig.UnitToPx;

        public static readonly WinConfettiFamily Ribbon = new(
            WaterSortWinConfetti.RibbonAddress,
            new Color(1f, 0.78f, 0.25f), new Color(0.95f, 0.25f, 0.30f), // 金→红
            22, new Vector2(0.14f * S, 0.22f * S));
        public static readonly WinConfettiFamily Chip = new(
            WaterSortWinConfetti.ChipAddress,
            new Color(0.20f, 0.80f, 1.00f), new Color(0.30f, 0.90f, 0.40f), // 青→绿
            18, new Vector2(0.12f * S, 0.18f * S));
        public static readonly WinConfettiFamily Dot = new(
            WaterSortWinConfetti.DotAddress,
            new Color(1f, 0.40f, 0.70f), new Color(1f, 1f, 1f), // 粉→白
            15, new Vector2(0.07f * S, 0.12f * S));
    }

    /// <summary>
    /// 彩带动画 Rig:逐帧驱动片体(位移+重力+各自终端速度+水平轻阻力+自旋+淡出缩小),全部片体结束即自毁。
    /// 「自然参差」设计(2026-09-07 验收反馈迭代:首版全体同帧起爆、同终端限速 → "队列齐落"假)。
    /// 核心:拒绝任何全局同步量,每片独立随机 —— 发射延迟、初速(差 3 倍以上)、终端下落速度
    /// (150~650px/s 分散 → 有快有慢)、水平阻力(轻阻 = 全程横漂像落叶滑翔,重阻 = 早早定住)、
    /// 自旋速度/方向。任意两片轨迹都不相似,观感错落自然。
    /// 调参全部集中在本类常量区;观感不对只改这里,不动业务。
    /// </summary>
    internal sealed class WinConfettiRig : MonoBehaviour
    {
        /// <summary>片体组织节点名(Play 前按此清理旧组)。</summary>
        public const string RootName = "WinConfetti";

        // ---- 像素标定:原测试相机可视高 8 世界单位铺满设计高 1920 → 1 单位 ≈ 240px ----
        public const float UnitToPx = 1920f / 8f;

        // ---- 自然飘落调参区(二版:落叶感)----
        const float Gravity = 9.81f * UnitToPx;    // 重力加速度(全片统一,真实世界如此)
        const float EmitHalfAngle = 85f;           // 喷射张角:近半球 → 大量片带大横向初速,
                                                   // 炸开即向两侧铺开,不再只有正上方一小把
        const float LaunchMin = 700f;              // 初速范围拉大:慢片在原点附近飘起,
        const float LaunchMax = 2700f;             // 快片直冲屏幕边缘/越界 → 满屏层次
        const float TermYMin = 120f;               // 每片随机「终端下落速度」:下落快慢各异,
        const float TermYMax = 700f;               // 杜绝全体同速齐落的"队列感"
        const float TermSettle = 5f;               // 向终端速度平滑收敛速率 /s(无速度跳变)
        const float DragXMin = 0.3f;               // 水平阻力系数(每片随机):轻阻片全程横漂滑翔
        const float DragXMax = 1.1f;               // (落叶感),重阻片早早悬停直落 —— 轨迹各异
        const float DelayMax = 0.5f;               // 发射时差上限:先喷的已飞出,后喷的刚起爆
        const float LifeMin = 1.9f;                // 运动时长(发射延迟之外),快片短命慢片长飘
        const float LifeMax = 3.6f;
        const float SpinMin = -540f;               // Z 轴自旋(度/s):高速翻面 = 纸屑翻转感
        const float SpinMax = 540f;
        const float FadeTail = 0.75f;              // 运动后段(最后 25%)线性淡出,避免瞬间消失
        const float ShrinkTo = 0.7f;               // 全程线性缩至 70%(飘远淡出的透视感)

        readonly List<Piece> _pieces = new(); // 存活片体(创建后追加;数量上限 = 三族总数)
        internal RectTransform Root;           // 片体挂载的 RectTransform(弹层本地系,Play 注入)

        // 单片状态:矩形 + 图片 + 运动量(后五项生成时逐片随机,见 SpawnFamily —— 参差的关键)
        sealed class Piece
        {
            public RectTransform Rt;
            public RawImage Image;
            public Vector2 Vel;   // px/s
            public float Spin;    // 度/s(Z 轴)
            public float TermY;   // 终端下落速度 px/s(独立 → 快慢错落)
            public float DragX;   // 水平阻力系数 /s(独立 → 横漂长短不一)
            public float Delay;   // 发射延迟 s(独立 → 起爆错峰)
            public float Life;    // 运动时长 s(不含延迟)
            public float Age;
            public float BaseW, BaseH; // 初始尺寸(px),随运动缩小
        }

        /// <summary>尝试落一族片体:贴图已缓存→本帧落;仍在加载→登记等待;已失败→族跳过。</summary>
        public void TrySpawnFamily(WinConfettiFamily family)
        {
            WaterSortWinConfetti.EnsureTextureFor(family.Address, tex =>
            {
                if (tex == null || this == null) return; // 加载失败或弹层已销毁:族静默跳过
                SpawnFamily(family, tex);
            });
        }

        void SpawnFamily(WinConfettiFamily family, Texture2D tex)
        {
            for (int i = 0; i < family.Count; i++)
            {
                // 生成单个片体:锚定 Rig 根中心,世界单位换算 + 随机分布见下方常量
                var go = new GameObject(family.Address.Substring(family.Address.LastIndexOf('/') + 1),
                    typeof(RectTransform), typeof(RawImage)); // 构造参数把默认 Transform 换成 RectTransform
                var rt = (RectTransform)go.transform;
                rt.SetParent(Root, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); // 根内全部居中锚,localPosition 即坐标
                rt.pivot = new Vector2(0.5f, 0.5f);

                var img = go.GetComponent<RawImage>();
                img.raycastTarget = false; // 纯特效不拦截点击
                img.texture = tex;
                var color = Color.Lerp(family.ColorMin, family.ColorMax, Random.value); // 白图染色
                color.a = 1f;
                img.color = color;

                // 同族贴图是正方形画布,片体按正方形展示(与粒子版四方块 Billboard 一致;
                // 长带纹理画在方片内呈粗胶囊,纸片/圆点原样)——一次取值,宽高同用
                float size = Mathf.Max(Random.Range(family.SizePx.x, family.SizePx.y), 1f);
                var piece = new Piece
                {
                    Rt = rt,
                    Image = img,
                    BaseW = size,
                    BaseH = size,
                    Vel = LaunchDir() * Random.Range(LaunchMin, LaunchMax),
                    Spin = Random.Range(SpinMin, SpinMax),
                    TermY = Random.Range(TermYMin, TermYMax),
                    DragX = Random.Range(DragXMin, DragXMax),
                    Delay = Random.Range(0f, DelayMax),
                    Life = Random.Range(LifeMin, LifeMax),
                };
                rt.sizeDelta = new Vector2(size, size);
                // 发射延迟期内不渲染(透明度 0):到点才"起爆",制造错峰参差起点
                var c0 = img.color;
                c0.a = 0f;
                img.color = c0;
                _pieces.Add(piece);
            }
        }

        /// <summary>喷出方向:绕屏幕朝上的均匀方位角 + 0~80° 俯仰(PS 锥形发射器向 2D 屏幕的投影)。</summary>
        static Vector2 LaunchDir()
        {
            // PS 锥体实际是绕 +Y(屏幕朝上)的立体锥,投影到屏幕:vy 由俯仰决定,vx 由方位角贡献
            float polar = Random.Range(0f, EmitHalfAngle) * Mathf.Deg2Rad;
            float azimuth = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar)).normalized;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool anyAlive = false;
            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                var p = _pieces[i];
                p.Age += dt;
                float flying = p.Age - p.Delay; // 距"起爆"已飞时长
                if (flying < 0f)
                {
                    // 未到发射时刻:静置(透明度 0 已在生成时置好),仅参与存活判定
                    anyAlive = true;
                    continue;
                }
                if (flying >= p.Life)
                {
                    // 运动时长结束:销毁片体并从列表移除
                    Object.Destroy(p.Rt.gameObject);
                    _pieces.RemoveAt(i);
                    continue;
                }
                anyAlive = true;
                // 速度积分(各片独立量,见类头「自然参差」设计):
                // 1) 重力统一下拉;2) 回落后向各自终端速度平滑收敛 → 下落有快有慢;
                // 3) 水平按每片阻力系数指数衰减 → 轻阻片带着横漂滑翔,重阻片直落,轨迹各异
                var vel = p.Vel;
                vel.y -= Gravity * dt;
                if (vel.y < -p.TermY) // 仅向下超速时收敛(上升段不受限,保留初速层次)
                    vel.y += (-p.TermY - vel.y) * Mathf.Min(TermSettle * dt, 1f);
                vel.x *= Mathf.Exp(-p.DragX * dt);
                p.Vel = vel;

                // 位移 / 自旋(旋转累计从起爆起算)
                p.Rt.localPosition += (Vector3)(vel * dt);
                p.Rt.localRotation = Quaternion.Euler(0f, 0f, p.Spin * flying);

                // 运动尾段(后 25%)线性淡出;全程线性缩小到 ShrinkTo
                float t = flying / p.Life;
                float alpha = t < FadeTail ? 1f : 1f - (t - FadeTail) / (1f - FadeTail);
                var c = p.Image.color;
                c.a = alpha;
                p.Image.color = c;
                float scale = Mathf.Lerp(1f, ShrinkTo, t);
                p.Rt.sizeDelta = new Vector2(p.BaseW * scale, p.BaseH * scale);
            }
            if (!anyAlive) Object.Destroy(gameObject); // 全部结束:整组自毁(下局 Play 前清理逻辑兜底)
        }
    }
}
