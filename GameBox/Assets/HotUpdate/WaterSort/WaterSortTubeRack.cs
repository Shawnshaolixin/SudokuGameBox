using System;
using System.Collections.Generic;
using System.Threading;
using Box.Services;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 试管架(运行时代码绘制):按会话盘面在 TubeArea 内重建试管列。每支试管 = 容器节点 + 液块列 +
    /// Glass 顶层(最后子级 = 最晚绘制,杯身贴图叠在液体上方:管壁/杯弧/高光压住水缘,形成「水在管内」透视)。
    /// 液体裁剪(软裁剪):液块挂自定义材质(Box/UI/WaterSortLiquid = ws_liquid_soft),逐块写入 _MaskST
    /// 把液块 quad 的 UV 映射到内腔剪影遮罩 ws_tube_mask(96×400 与杯身同画布),片元按遮罩 alpha
    /// smoothstep 软过渡裁剪 —— 剪影边缘自带 1~2px 羽化,底弧等强曲线处无 stencil 硬裁的像素阶梯,
    /// 且剪影底弧贴玻璃壁线内侧(液缘藏进半透明壁线之下,无管外露边)。
    /// 兜底:遮罩或 shader 未就绪 → 直角液块(宽收在强壁线 x8-11/84-87 底下),就绪后合帧重建补画。
    /// 杯身/遮罩/ shader 经 IAssetService 异步加载(地址约定 21 文档 §6.1);就绪后自动重建补画。
    /// 交互:点谁谁选中(整管拎起 SelectedLiftPx 停留 + 杯身微染);再点另一支时按引擎同源规则
    /// (LegalMoves 枚举,见 IsPourable)预判倒水合法性 —— 合法发 PourRequested 交视图倒水
    /// (成功后视图刷新摘选中,管落回);非法直接把选中切换到新点的管(旧管落回、新管拎起),
    /// 无需先点回旧管;点自己取消选中。组件运行期 AddComponent 到 TubeArea(不走 prefab 序列化,20 文档 §4 纪律同源)。
    /// 重建即清空重画(盘面 ≤12 管 × ≤4 层,一次性 UI 对象开销可忽略)。
    /// </summary>
    public sealed class WaterSortTubeRack : MonoBehaviour
    {
        /// <summary>杯身贴图地址(21 文档 §6.1 映射;编辑器 WaterSortSkinImporter 自动注册进 Game_WaterSort 组)。</summary>
        public const string TubeSpriteAddress = "WaterSort/UI/ws_tube";

        /// <summary>内腔剪影遮罩地址(液块软裁剪用,须与 ws_tube 同画布同构)。</summary>
        public const string MaskSpriteAddress = "WaterSort/UI/ws_tube_mask";

        /// <summary>液体软裁剪 shader 地址(Box/UI/WaterSortLiquid;采样遮罩 alpha 软过渡,替代 UGUI Mask 硬裁)。</summary>
        public const string LiquidShaderAddress = "WaterSort/UI/ws_liquid_soft";

        // 贴图就位后的选中高亮:图自带玻璃透明度/高光,不再用占位期的透明度提亮,改轻微蓝染区分
        // (占位方案见 Refresh 兜底分支;P3 落地 ws_glow_ring 柔光环后替换本染色)
        static readonly Color SelectedTint = new Color(0.80f, 0.92f, 1f, 1f);

        // 液柱几何常量 —— 实测 96×400 二图(试管软玻璃体 x6..89、强壁线 x8-11/84-87;
        // 封底渐变 rows ~340..397;遮罩剪影贴壁线内侧 1px、底弧随壁线收口)。换贴图按同法重测
        // (像素探针:Build/Tools/probe_tube_*.ps1、生成脚本 Build/Tools/gen_ws_tube_mask.py)再校准本组常量。
        const float DropWidthFrac = 0.98f;       // 液块宽 = 0.98×管宽:略超内腔由遮罩 alpha 软裁齐,侧缘零方形可见
        const float DropWidthFallbackFrac = 0.78f; // 直角兜底宽(软裁剪未就绪):x10.6..85.4 收在强壁线 x8-11/84-87 底下
        const float WaterBottomPadFrac = 0.015f; // 液柱底边距图底(≈6px/400):与遮罩底弧收口行对齐,底缘藏进封底描边
        const float WaterTopGapFrac = 0.15f;     // 液柱顶距图顶最小余量(≈60px/400):满管液面也不顶到杯口区
        const float DropOverlapPx = 1f;          // 相邻液块 1px 重叠:消除浮点取整发丝缝(层间无可见间隙)

        // 选中表现:拎起 = 试管整体上移 SelectedLiftPx 并停留(比高亮染色更直观),取消/倒完水落回原位
        const float SelectedLiftPx = 26f;        // 选中上抬高度(px)
        const float LiftDuration = 0.16f;        // 拎起动画时长(落回用 DropBounce 稍长,见 AnimateLift)

        // 布局:≤4 管单行、5~8 管两行、>8 管三行;每行水平居中、整块垂直居中,行间距 RowGap
        const float RowGap = 34f;
        const float RowGapMaxFrac = 0.6f; // 行内管间距上限(×管宽):管少的行居中、适度拉开不贴边;
                                          // 管多(最宽行)时均分余量 ≤ 上限,自然间距直接生效、自动铺满

        // 倒水动画节奏(总 ~0.9s;调快整体手感更「弹」,调慢更「重」)
        const float PourLiftDuration = 0.14f;  // 滑移:源管前沿角送到锚点(目标口上方)
        const float PourTipPerUnitDuration = 0.30f; // 绕锚点倾倒:每份水的时长(份数线性放大 →
                                               // 旋转角速度随之放慢,流速恒定;回弧段固定不随份数)
        const float PourBackDuration = 0.18f;  // 收管:从倾倒终态直线插值回基准位(不沿原弧)
        // 整体节奏倍率:当前 2.5f(时长 2.5 倍 = 40% 速度,介于原速 1 与观察速 5 之间的手感值);
        // 验收定稿后如回归原速改回 1f(总时长随倍率线性膨胀,忘改交付 = 倒水偏慢)
        const float PourTimeScale = 2.5f;
        const float PourMaxTiltDeg = 87f;      // 倾角绝对上限:常规水量下倾角由「液面贴唇口」条件自动
                                               // 决定(LipTheta),本上限仅近乎倒空时才接近
        const float PourTipPhaseFrac = 0.35f;  // 倾倒段内「快速倾到出水角」所占进度,余下边续转边倒
        const float PourGlideTiltDeg = 30f;    // 滑移段预倾角上限:边移边旋到位;≤出水角途中不漏,少水管不一路转平
        const float PourAnchorAboveFrac = 0.10f; // 旋转锚点在目标「口顶端」上方的高度(×管高)
        // 倾斜期液块加宽系数:管体倾斜 θ 后内腔水平宽度 = 管宽/cosθ,水平液块须
        // 覆盖到两侧壁,否则露出液块自身直边(几何硬边锯齿,「台阶感」的成因);超宽部分由遮罩
        // 裁掉 + shader UV 夹紧,常宽无副作用。近 90° 倒水取大余量

        // 占位色板(1..12 对应液滴值;色相取自常见水排序配色,表现替换时仅改本表)
        static readonly Color[] Palette =
        {
            new Color32(0xF4, 0x43, 0x36, 0xFF), // 1 红
            new Color32(0x4C, 0xAF, 0x50, 0xFF), // 2 绿
            new Color32(0x21, 0x96, 0xF3, 0xFF), // 3 蓝
            new Color32(0xFF, 0xC1, 0x07, 0xFF), // 4 黄
            new Color32(0xFF, 0x98, 0x00, 0xFF), // 5 橙
            new Color32(0x9C, 0x27, 0xB0, 0xFF), // 6 紫
            new Color32(0x00, 0xBC, 0xD4, 0xFF), // 7 青
            new Color32(0xE9, 0x1E, 0x63, 0xFF), // 8 粉
            new Color32(0x8B, 0xC3, 0x4A, 0xFF), // 9 浅绿
            new Color32(0x79, 0x55, 0x48, 0xFF), // 10 棕
            new Color32(0x60, 0x7D, 0x8B, 0xFF), // 11 蓝灰
            new Color32(0xFF, 0xE0, 0x82, 0xFF), // 12 淡黄
        };

        /// <summary>倒水请求(源, 目标):仅合法移动会发出(试管架已按 LegalMoves 预判);
        /// 视图 TryPour 成功后经 BoardChanged 刷新整架并摘选中(选中管随重建落回原位)。</summary>
        public event Action<int, int> PourRequested;

        WaterSortSession _session;
        int _selected = -1; // 当前选中试管;点同支取消
        RectTransform _self;
        Sprite _tubeSprite;            // 已加载杯身贴图(懒加载一次,视图缓存期内常驻)
        bool _tubeSpriteRequested;     // 杯身贴图请求已发出(失败不回退重试,占位色兜底)
        Sprite _tubeMaskSprite;        // 已加载内腔遮罩(液块软裁剪;与 shader 双就绪才启用裁剪)
        bool _tubeMaskRequested;       // 遮罩请求已发出(失败不回退,维持兜底路径)
        Shader _liquidShader;          // 液体软裁剪 shader(与遮罩双就绪 → 构建基材质)
        bool _liquidShaderRequested;   // shader 请求已发出(失败不回退,维持兜底路径)
        Material _liquidMaterial;      // 基材质:持有 _MaskTex;液块按 (层数,层序) 派生实例写 _MaskST
        readonly Dictionary<int, Material> _dropMaterials = new Dictionary<int, Material>(); // 实例缓存(见 GetDropMaterial)
        float _dropMatCacheH;          // 液块材质缓存构建时的管高(布局几何变化即整体失效,见 Refresh)
        readonly List<Material> _pourTempMats = new List<Material>(); // 倒水期临时材质(液带克隆/生长块):
                                                                      // Destroy 节点不回收材质,须显式销毁防原生泄漏
        Vector2[] _basePositions;      // 各管基准锚点(分行布局计算结果;选中抬起 = 基准 + SelectedLiftPx)
        readonly Dictionary<int, CancellationTokenSource> _liftAnim =
            new Dictionary<int, CancellationTokenSource>(); // 各管抬/落动画取消源(快速连点防动画打架)
        float _layoutW = 80f;          // 最近一次布局的管宽(倒水动画几何计算用,初值防首帧未布局)
        float _layoutH = 320f;         // 最近一次布局的管高(同上)
        bool _animating;               // 倒水动画进行中:输入锁 + 盘面刷新挂起(收尾统一重建,见 PlayPourAsync)
        bool _refreshScheduled;        // 合帧重建去重标记(见 ScheduleRefresh)

        /// <summary>倒水动画进行中(视图据此锁操作钮;RefreshTubeArea 亦据此挂起整架重建)。</summary>
        public bool IsAnimating => _animating;

        /// <summary>倒水动画收尾(整架已重建到真实盘面)后广播:视图做 HUD/引导同步。</summary>
        public event Action PourCompleted;

        // shader 属性 ID(缓存避免每次重建字符串查表)
        static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        static readonly int MaskSTId = Shader.PropertyToID("_MaskST");
        static readonly int MaskRotId = Shader.PropertyToID("_MaskRot");
        static readonly int MaskAspectId = Shader.PropertyToID("_MaskAspect");
        static readonly int EdgeLoId = Shader.PropertyToID("_EdgeLo");
        static readonly int EdgeHiId = Shader.PropertyToID("_EdgeHi");

        /// <summary>绑定会话(视图在会话就绪后调用一次;盘面以 Refresh 为准,无需重复设置)。</summary>
        public void SetSession(WaterSortSession session) => _session = session;

        /// <summary>指定序号的试管根节点(Refresh 后有效,序 = 当前盘面管序)——引导第 2 步高亮定位用。</summary>
        public RectTransform Tube(int index)
        {
            if (_self == null || index < 0 || index >= transform.childCount) return null;
            return (RectTransform)transform.GetChild(index);
        }

        /// <summary>按盘面重建全部试管(倒水/撤销/重开/换关后由视图调用)。</summary>
        public void Refresh()
        {
            if (_session == null || _session.Board == null) return;
            if (_animating) return; // 倒水动画中盘面已落子:挂起重建,动画收尾统一刷(见 PlayPourAsync)
            if (_self == null) _self = (RectTransform)transform;
            ClearChildren();
            EnsureSprites(); // 贴图/遮罩懒加载(首帧可能未就绪,就绪后合帧重建,见 ScheduleRefresh)

            var board = _session.Board;
            int n = board.TubeCount;
            // 分行布局:≤4 管单行 / 5~8 管两行 / >8 管三行。余量给底行(下重上轻视觉更稳),
            // 每行水平居中、整块在容器内垂直居中;管序 = 自上而下逐行从左到右(与 Board 索引一致)
            int rows = n <= 4 ? 1 : (n <= 8 ? 2 : 3);
            int perRow = n / rows, extra = n % rows;
            var rowCounts = new int[rows];
            for (int r = 0; r < rows; r++)
                rowCounts[r] = perRow + (extra > 0 && r >= rows - extra ? 1 : 0);
            int maxPerRow = rowCounts[rows - 1]; // 底行最宽 → 水平尺寸按它收
            // 尺寸自适应:管宽按最宽行的管数收窄;高度按宽高比 4.6(经典细长试管)并受
            // 「行数 × 管高 + 行距」总高约束;高度受限时按比例反收管宽,保持试管形状
            float w = Mathf.Min(110f, (_self.rect.width - 80f - (maxPerRow - 1) * 12f) / maxPerRow);
            float h = Mathf.Min(w * 4.6f, (_self.rect.height - 60f - (rows - 1) * RowGap) / rows);
            w = Mathf.Min(w, h / 4.6f);
            _layoutW = w; // 动画几何计算用(倒水悬停点/水流柱)
            _layoutH = h;
            if (_liquidMaterial != null)
            {
                _liquidMaterial.SetFloat(MaskAspectId, w / h); // 旋转补偿的等比空间换算比(shader 注释)
                if (!Mathf.Approximately(_dropMatCacheH, h))
                {
                    // 布局几何变化(分辨率/窗口尺寸/分行):缓存实例的 _MaskST/_MaskAspect 均为旧几何,
                    // 复用会整体错位,废弃重建(派生实例创建时快照基材质,基材质改比例不会回传)
                    foreach (var kv in _dropMaterials) Destroy(kv.Value);
                    _dropMaterials.Clear();
                    _dropMatCacheH = h;
                }
            }

            _basePositions = new Vector2[n];
            CancelLiftAnims(); // 重建即全换新节点:挂着的抬/落动画全部作废
            float blockH = rows * h + (rows - 1) * RowGap;
            int t = 0;
            for (int r = 0; r < rows; r++)
            {
                float rowY = blockH * 0.5f - h * 0.5f - r * (h + RowGap); // 本行中心 y(首行最上)
                // 本行水平分布:行内边缘间距 = clamp(均分余量, 12, RowGapMaxFrac·管宽),整行居中 ——
                // 管少的行适度拉开、不贴边;管多(最宽行)时余量小、自然间距 ≤ 上限,自动协调铺满
                float rowAvailW = _self.rect.width - 80f;
                int rowCount = rowCounts[r];
                float step, startX;
                if (rowCount > 1)
                {
                    float gap = Mathf.Clamp((rowAvailW - rowCount * w) / (rowCount - 1),
                        12f, w * RowGapMaxFrac);
                    step = w + gap;
                    startX = -(rowCount * w + (rowCount - 1) * gap) * 0.5f + w * 0.5f;
                }
                else
                {
                    step = 0f;
                    startX = 0f; // 单管居中(当前行分布不会出现,防御路径)
                }
                for (int c = 0; c < rowCounts[r]; c++, t++)
                {
                    int drops = board.TopCount(t);
                    var col = BuildTube(t, w, h); // 空容器(无 Image 不挡射线);液块先入子级、Glass 最后入
                    var rt = (RectTransform)col.transform;
                    var basePos = new Vector2(startX + c * step, rowY);
                    rt.anchoredPosition = basePos;
                    _basePositions[t] = basePos;

                    // 液块:底边落座在杯底收口(几何常量类头),自底向上排;相邻块 1px 重叠消取整缝。
                    // 软裁剪就绪时液块宽略超内腔,两侧/底弧由遮罩 alpha 软裁齐;兜底模式收在强壁线底下
                    bool softClipped = drops > 0 && _liquidMaterial != null;
                    float bottomY = -h * (0.5f - WaterBottomPadFrac);
                    float usableH = h * (1f - WaterBottomPadFrac - WaterTopGapFrac);
                    float dropH = (usableH + (drops - 1) * DropOverlapPx) / Mathf.Max(4, drops);
                    float waterW = w * (softClipped ? DropWidthFrac : DropWidthFallbackFrac);
                    for (int i = 0; i < drops; i++)
                    {
                        int color = board.Get(t, i); // 0=底
                        var drop = NewChild(rt, "Drop", waterW, dropH, Palette[color - 1], false);
                        float yBottom = bottomY + i * (dropH - DropOverlapPx); // 本块底边(i=0 贴杯底)
                        ((RectTransform)drop.transform).anchoredPosition =
                            new Vector2(0, yBottom + dropH * 0.5f);
                        if (softClipped)
                            drop.GetComponent<Image>().material =
                                GetDropMaterial(h, drops, i, dropH); // 逐块写遮罩 UV 映射
                    }

                    // Glass 顶层(最后子级 = 最晚绘制 → 玻璃压在液体上,管壁/杯口弧/高光叠在水层前方,呈现水在管内)
                    var glass = AppendGlass(rt, t, w, h);
                    if (_tubeSprite != null)
                    {
                        // 贴图模式:Simple 整图等比缩放到 w×h;颜色仅做选中微染(玻璃透明度/高光由贴图自带)
                        glass.sprite = _tubeSprite;
                        glass.type = Image.Type.Simple;
                        glass.color = t == _selected ? SelectedTint : Color.white;
                    }
                    else
                    {
                        // 兜底占位(贴图未就绪/未注册):半透明杯体,选中提亮;贴图就绪后由回调补画
                        glass.color = t == _selected
                            ? new Color(0.45f, 0.55f, 0.70f, 0.9f)  // 选中高亮(占位:提亮杯体)
                            : new Color(1f, 1f, 1f, 0.10f);
                    }
                }
            }
            // 跨重建保留的选中(正常提交型重建前视图已 ClearSelection;贴图回调重建等路径防御性复位抬起位)
            if (_selected >= 0 && _selected < n) SnapSelected();
        }

        /// <summary>懒加载杯身/遮罩贴图与液体 shader(IAssetService 回调式;失败留空只试一次 → 占位色/直角
        /// 液块兜底,见类头)。任一就绪 → 合帧重建整架;遮罩+shader 双就绪时先建基材质再重建。</summary>
        void EnsureSprites()
        {
            if (!_tubeSpriteRequested)
            {
                _tubeSpriteRequested = true;
                ServiceLocator.Assets?.LoadAsset<Sprite>(TubeSpriteAddress, sp =>
                {
                    if (sp == null) return; // 未注册/未构建:保持占位色,不刷警告不重试
                    _tubeSprite = sp;
                    ScheduleRefresh();
                });
            }
            if (!_tubeMaskRequested)
            {
                _tubeMaskRequested = true;
                ServiceLocator.Assets?.LoadAsset<Sprite>(MaskSpriteAddress, sp =>
                {
                    if (sp == null) return;
                    _tubeMaskSprite = sp;
                    TryBuildLiquidMaterial();
                    ScheduleRefresh();
                });
            }
            if (!_liquidShaderRequested)
            {
                _liquidShaderRequested = true;
                ServiceLocator.Assets?.LoadAsset<Shader>(LiquidShaderAddress, sh =>
                {
                    if (sh == null) return;
                    _liquidShader = sh;
                    TryBuildLiquidMaterial();
                    ScheduleRefresh();
                });
            }
        }

        /// <summary>遮罩 + shader 双就绪时构建液体基材质(挂 _MaskTex);液块实例按需从它派生。
        /// 单独就绪其一不构建 —— 缺遮罩无裁剪形状、缺 shader 无软裁逻辑,均走直角兜底。</summary>
        void TryBuildLiquidMaterial()
        {
            if (_liquidMaterial != null || _tubeMaskSprite == null || _liquidShader == null) return;
            _liquidMaterial = new Material(_liquidShader);
            _liquidMaterial.SetTexture(MaskTexId, _tubeMaskSprite.texture);
        }

        /// <summary>液块材质:按 (层数, 层序) 缓存派生实例(同构液块共用,合批友好),写入 _MaskST =
        /// 液块 quad(texcoord 0..1 = 液块自身矩形)到遮罩 UV 空间的平移(xy)+缩放(zw)。管矩形归一化为
        /// [0,1](左下原点)后:液块宽恒为 DropWidthFrac,底边 v = WaterBottomPadFrac + i×层高步进。
        /// 几何式须与 Refresh 绘制循环保持一字不差,否则裁剪形状与液块错位。</summary>
        Material GetDropMaterial(float h, int drops, int i, float dropH)
        {
            int key = drops * 8 + i; // 层数 ≤4、层序 <8,单字节键互斥
            if (!_dropMaterials.TryGetValue(key, out var mat))
            {
                mat = new Material(_liquidMaterial);
                float yBottom = -h * (0.5f - WaterBottomPadFrac) + i * (dropH - DropOverlapPx);
                float u0 = 0.5f * (1f - DropWidthFrac);
                float v0 = yBottom / h + 0.5f;
                mat.SetVector(MaskSTId, new Vector4(u0, v0, DropWidthFrac, dropH / h));
                _dropMaterials[key] = mat;
            }
            return mat;
        }

        /// <summary>释放派生/基材质与抬落动画取消源(重建复用缓存,仅销毁时清一次,防原生泄漏)。</summary>
        void OnDestroy()
        {
            CancelLiftAnims();
            foreach (var kv in _dropMaterials) Destroy(kv.Value);
            _dropMaterials.Clear();
            foreach (var m in _pourTempMats) Destroy(m); // 动画中途销毁的兜底:临时材质一并回收
            _pourTempMats.Clear();
            if (_liquidMaterial != null) Destroy(_liquidMaterial);
            _liquidMaterial = null;
        }

        /// <summary>贴图/遮罩到货后的重建:合帧只跑一次(两资源同帧回调时防重复重建/清空竞态)。</summary>
        void ScheduleRefresh()
        {
            if (_refreshScheduled) return;
            _refreshScheduled = true;
            StartCoroutine(RefreshNextFrame());
        }

        System.Collections.IEnumerator RefreshNextFrame()
        {
            yield return null;
            _refreshScheduled = false;
            Refresh();
        }

        /// <summary>倒水被拒(非法):抖动源管给出反馈(占位无音效,动画见 D-15 BoxTween)。</summary>
        public async void ShakeTube(int tubeIndex)
        {
            if (_self == null || tubeIndex < 0 || tubeIndex >= _self.childCount) return;
            await Box.UI.BoxTween.Shake(_self.GetChild(tubeIndex), 0.28f, 14f);
        }

        /// <summary>清空选中标记(提交型重建前由视图调用):取消挂着的抬/落动画,就地还原杯身染色;
        /// 重建后各管归位基准点。倒水前调用可防收尾重建后源管残留「拎起 + 蓝染」
        /// (动画期 RefreshTubeArea 被挂起,选中只能在这里摘)。</summary>
        public void ClearSelection()
        {
            int prev = _selected;
            _selected = -1;
            CancelLiftAnims();
            if (prev >= 0) ApplyTint(prev); // 就地还原染色(贴图模式;收尾整架重建本身也会重上色)
        }

        void OnTubeTap(int index)
        {
            // 倒水动画中锁点击:动画期再落子会并发第二个倒水任务,收尾重建会销毁对方动画握持的
            // 节点 → 异常且 _animating 永久为 true(输入锁/盘面刷新全挂死)
            if (_animating) return;
            if (_selected < 0)
            {
                SetSelected(index); // 无选中:点谁选中谁(拎起停留)
                return;
            }
            if (_selected == index)
            {
                SetSelected(-1); // 点自己:取消选中(落回原位)
                return;
            }
            // 已有选中:先按引擎同源规则判倒水合法性 —— 合法才发请求(视图 TryPour 成功后经
            // BoardChanged 刷新并摘选中,选中管落回);非法不再要求玩家先点回旧管,
            // 直接把选中切换到新点的管(旧管落回、新管拎起)
            if (IsPourable(_selected, index))
            {
                PourRequested?.Invoke(_selected, index);
                return;
            }
            SetSelected(index);
        }

        /// <summary>倒水合法性预判(与会话 TryPour 同源:枚举引擎 LegalMoves 匹配 src→dst)。
        /// 管数 ≤12+额外,枚举开销可忽略;会话/盘面缺失按不可倒处理(走选中切换)。</summary>
        bool IsPourable(int src, int dst)
        {
            var b = _session?.Board;
            if (b == null) return false;
            foreach (var m in b.LegalMoves())
                if (m.Src == src && m.Dst == dst) return true;
            return false;
        }

        /// <summary>设置选中(index &lt; 0 = 取消):新选中管拎起停留、原选中管落回,同步杯身高亮。
        /// 只动节点位移动画不整架重建(重建会打断动画且开销无谓);高亮同样就地去色。</summary>
        void SetSelected(int index)
        {
            int prev = _selected;
            _selected = index;
            if (prev >= 0 && prev != index) AnimateLift(prev, false); // 旧选中落回
            if (index >= 0) AnimateLift(index, true);                 // 新选中拎起
            ApplyTint(prev);
            ApplyTint(index);
        }

        /// <summary>拎起/落回动画:目标位 = 基准位 ± SelectedLiftPx。落回走 DropBounce(落地回弹,
        /// 「放回去」的手感);同管旧动画先取消,防快速连点时位移动画叠加打架。</summary>
        void AnimateLift(int tube, bool up)
        {
            if (tube < 0 || tube >= transform.childCount
                || _basePositions == null || tube >= _basePositions.Length) return;
            if (_liftAnim.TryGetValue(tube, out var old))
            {
                old.Cancel();
                old.Dispose();
            }
            var cts = new CancellationTokenSource();
            _liftAnim[tube] = cts;
            var rt = (RectTransform)transform.GetChild(tube);
            var from = rt.anchoredPosition;
            var to = _basePositions[tube] + (up ? new Vector2(0, SelectedLiftPx) : Vector2.zero);
            if (up) Box.UI.BoxTween.MoveTo(rt, from, to, LiftDuration, cts.Token).Forget();
            else Box.UI.BoxTween.DropBounce(rt, from, to, LiftDuration + 0.12f, cts.Token).Forget();
        }

        /// <summary>选中管瞬移到抬起位(无动画):重建后恢复选中态用(防御路径,正常流程选中不跨重建)。</summary>
        void SnapSelected()
        {
            if (_selected < 0 || _selected >= transform.childCount) return;
            ((RectTransform)transform.GetChild(_selected)).anchoredPosition =
                _basePositions[_selected] + new Vector2(0, SelectedLiftPx);
        }

        /// <summary>杯身高亮就地刷新(选中微蓝染/还原白色;占位期无贴图不动,抬起位移已足够区分)。</summary>
        void ApplyTint(int tube)
        {
            if (_tubeSprite == null || tube < 0 || tube >= transform.childCount) return;
            var glass = transform.GetChild(tube).Find("Glass")?.GetComponent<Image>();
            if (glass != null) glass.color = tube == _selected ? SelectedTint : Color.white;
        }

        /// <summary>取消全部抬/落动画并释放取消源(重建清子级前/摘选中时调用,防动画写已销毁节点)。</summary>
        void CancelLiftAnims()
        {
            foreach (var kv in _liftAnim)
            {
                kv.Value.Cancel();
                kv.Value.Dispose();
            }
            _liftAnim.Clear();
        }

        // ---- 倒水动画(四段式;仅合法移动会进入,视图已完成 TryPour 落子) ---- //

        /// <summary>上动画锁(视图在 TryPour 前调用:BoardChanged 触发的整架刷新被 Refresh 挂起)。</summary>
        public void BeginPour() => _animating = true;

        /// <summary>解锁(竞态兜底:上锁后未能落子时复位,理论不可达)。</summary>
        public void CancelPour() => _animating = false;

        /// <summary>单格液面高度(与 Refresh 布局公式同源,勿改其一)。</summary>
        float DropHeight(float h, int drops)
            => (h * (1f - WaterBottomPadFrac - WaterTopGapFrac) + (drops - 1) * DropOverlapPx) / Mathf.Max(4, drops);

        /// <summary>
        /// 倒水动画(总 ~0.9s;盘面在进入前已落子,本动画只演「过程」,收尾重建到真实盘面):
        /// 1) 源管提到最上层,边移动边旋转到预倾角(≤出水角,封顶 PourGlideTiltDeg,途中不漏),
        ///    把管口「前沿角」(旋转朝向一侧的嘴角)送到锚点(目标口上方);
        /// 2) 绕锚点倾倒:前沿角钉在锚点(管心位 = 锚点 - R(θ)·前沿角,真实倾倒姿态),倾角按
        ///    「液面贴唇口」条件随水量自动加大(LipTheta),
        ///    液块子节点反向旋转保持屏幕水平(水面恒水平),材质 _MaskRot 反向补偿采样遮罩,
        ///    裁剪边界贴合倾斜后的内腔剪影;液面触及前沿角(几何推导)后开始出水 —— 水流柱 +
        ///    源管顶层 run 逐块平滑下降 + 目标管口新块同步长起(等量转移;临时材质克隆);
        /// 3) 不沿原弧,直接插值回基准位,销毁临时节点,整架重建并广播 PourCompleted。
        /// 途中视图销毁:destroyCancellationToken 令 UniTask.Yield 抛取消,临时节点随整架销毁。
        /// </summary>
        public async UniTaskVoid PlayPourAsync(int src, int dst, int count)
        {
            var ct = destroyCancellationToken;
            try
            {
                // 兜底:软裁剪未就绪(无材质)/节点缺失(未布局)不演动画,直接重建交付结果
                if (_liquidMaterial == null || src >= transform.childCount || dst >= transform.childCount)
                {
                    _animating = false;
                    Refresh();
                    PourCompleted?.Invoke();
                    return;
                }

                var srcRt = (RectTransform)transform.GetChild(src);
                var dstRt = (RectTransform)transform.GetChild(dst);
                float h = _layoutH;
                float w = _layoutW;
                float bottomY = -h * (0.5f - WaterBottomPadFrac);

                // 源管液体按「颜色段」重组为水平液带(倒水前状态,自底向上):每段 = 同色连续块,
                // 用该段最底部的块作带矩形(同色后继块隐藏)。水平带模型下色带界面恒为屏幕水平线,
                // 倒水时只有最顶带(液面)收缩 —— 任意倾角下层间关系稳定(蓝永远整体在黄下方)。
                // 逐块水平矩形在近 90° 时会沿管轴错开(色块并排而非叠放),是层间穿帮的根因。
                int srcDrops = 0;
                for (int i = 0; i < srcRt.childCount; i++)
                    if (srcRt.GetChild(i).name == "Drop") srcDrops++;
                float dropH = DropHeight(h, srcDrops);
                float unitSrc = dropH - DropOverlapPx;
                var bandRects = new List<RectTransform>();
                var bandMats = new List<Material>();
                var bandA = new List<float>(); // 各带轴向底(管局部 y)
                var bandB = new List<float>(); // 各带轴向顶
                for (int i = 0; i < srcRt.childCount; i++)
                {
                    if (srcRt.GetChild(i).name != "Drop") continue;
                    var block = (RectTransform)srcRt.GetChild(i);
                    var img = block.GetComponent<Image>();
                    float a = bottomY + i * unitSrc;
                    // 与上一带同色 → 并入(顶边抬高,自身隐藏);异色 → 新带
                    if (bandRects.Count > 0 && img.color == bandRects[bandRects.Count - 1].GetComponent<Image>().color)
                    {
                        bandB[bandB.Count - 1] = a + dropH;
                        img.gameObject.SetActive(false);
                        continue;
                    }
                    img.gameObject.SetActive(true);
                    bandRects.Add(block);
                    var clone = new Material(img.material); // 临时克隆,共享缓存不可动画改写
                    img.material = clone;
                    bandMats.Add(clone);
                    _pourTempMats.Add(clone);
                    bandA.Add(a);
                    bandB.Add(a + dropH);
                }
                // 液带保持原始中心锚点与几何(θ=0 时带 = 原块,起手零跳变);倾倒中由
                // UpdateBands 以「屏幕水平带」语义重定位(中心锚点,见 SetBandGeometry)
                // 动画期放宽软边过渡带(占满羽化全程):旋转采样下窄过渡带显锯齿(楼梯感),
                // 全带宽 ≈2~3px 柔边,观感即「液体贴玻璃」的自然过渡;动画结束经 _pourTempMats 统一销毁
                foreach (var m in bandMats)
                {
                    m.SetFloat(EdgeLoId, 0f);
                    m.SetFloat(EdgeHiId, 1f);
                }
                // 合带后立即按各带完整轴向区间重建几何(θ=0):带矩形取的是 run 最底块(仅单格高),
                // 不在滑移前撑满的话,多格同色 run 会在整个滑移段显示成 1 格(3 蓝 → 1 蓝穿帮)。
                // 同色合并本身视觉无损(同色块间界面不可见),撑满后 θ=0 与原块逐像素一致;
                // yLow 的 1px 下延与 UpdateBands 在 θ=0 的取值一致,倾倒段起手零跳变
                for (int r = 0; r < bandRects.Count; r++)
                {
                    float yLow0 = bandA[r] - (r == 0 ? 0f : DropOverlapPx);
                    SetBandGeometry(bandRects[r], bandMats[r], TiltQuadWidth(0f, w, h), w, 0f,
                        (bandB[r] + yLow0) * 0.5f, bandB[r] - yLow0, h);
                }

                // 目标管:生长块(插到 Glass 之下;颜色 = 倒入色 = 落子后目标顶层色)
                int dstDrops = 0;
                for (int i = 0; i < dstRt.childCount; i++)
                    if (dstRt.GetChild(i).name == "Drop") dstDrops++;
                // 生长块按「倒水后层数」的单格高建模:终态几何与收尾重建一致,削弱收尾整列重排的
                // snap(新旧单格高差亚像素级,与既有液块的接缝偏差可忽略)
                float unitDst = DropHeight(h, dstDrops + count) - DropOverlapPx;
                float growBottom = bottomY + dstDrops * unitDst; // 新块底 = 目标当前液面
                float growFinalH = count * unitDst + DropOverlapPx;
                int pourColor = _session.Board.TopColor(dst); // 落子后顶层即倒入色
                var grow = NewChild(dstRt, "PourGrow", w * DropWidthFrac, 0.01f, Palette[pourColor - 1], false);
                var growRt = (RectTransform)grow.transform;
                growRt.pivot = new Vector2(0.5f, 0f);
                grow.transform.SetSiblingIndex(dstRt.childCount - 2); // Glass(末子级)之前
                var growMat = new Material(_liquidMaterial);
                grow.GetComponent<Image>().material = growMat;
                _pourTempMats.Add(growMat);

                // 水流柱(挂在目标管内、Glass 之下 → 玻璃叠在水流上方,有「没入管内」的层次;
                // 顶可伸出管外到源管口 —— 子矩形不受父矩形裁剪):粗细 ~0.11 管宽,出水首尾宽度淡入淡出
                float streamW = Mathf.Max(5f, w * 0.11f);
                var stream = NewChild(dstRt, "PourStream", streamW, 1f, Palette[pourColor - 1], false);
                var streamRt = (RectTransform)stream.transform;
                stream.transform.SetSiblingIndex(dstRt.childCount - 2); // 与生长块同层(Glass 之前)
                stream.gameObject.SetActive(false); // 滑移段隐藏:初始 1px 高色线会在目标管内穿帮,出水建立再显示

                // —— 1) 滑移:把管口「前沿角」送到锚点(目标口上方)。前沿角 = 旋转朝向一侧的嘴角
                //    (往右倒 = 顺时针 = 右嘴角是前;往左倒同理) —— 倒水时它钉在锚点上不动 ——
                srcRt.SetAsLastSibling();
                var glideFrom = srcRt.anchoredPosition; // 当前位(可能带选中抬起量),从原地起滑
                var basePos = _basePositions[src];      // 动画结束落回的布局基准位
                float dir = Mathf.Sign(dstRt.anchoredPosition.x - glideFrom.x);
                if (dir == 0f) dir = 1f;                          // 同列:默认顺时针(往右倒)
                var anchor = (Vector2)dstRt.anchoredPosition + new Vector2(0f, h * 0.5f + h * PourAnchorAboveFrac); // 锚点 = 目标「口顶端」上方,非管中心
                var frontLocal = new Vector2(dir * w * 0.5f, h * 0.5f);   // 前沿角(管局部)
                // 倾角全程由「液面钉在唇口」条件驱动:液面是屏幕水平线(带模型),屏幕高 = 管心高
                // + surface·cosθ,令其恰等于唇口(= 锚点)解得 |θ| = atan(2(h/2 − surface)/w)。
                // 倒水时液面随消减量下降、倾角自动跟大 —— 上层水正好从管口流出、下一层刚好挨着
                // 瓶口;固定终角方案必然顾此失彼(过大把液带全挤向管口,过小则水未到口就"提前减少")
                // 初液面(轴向)= 顶带真实顶。不可按「带底 + count」反推:部分转移(count < 顶层
                // run 长度,如目标只剩 2 格空间)时会低估液面 → 起手液面跳变一格,且顶带被整条
                // 倒空、收尾重建把未倒的余量「凭空弹回」(水不守恒穿帮)
                float surface0 = bandB[bandB.Count - 1];
                float LipTheta(float surfaceAxial) // 液面贴唇口的倾角(带方向;PourMaxTiltDeg 仅近乎倒空时触顶)
                    => -dir * Mathf.Clamp(
                           Mathf.Atan(2f * (h * 0.5f - surfaceAxial) / w) * Mathf.Rad2Deg, 0f, PourMaxTiltDeg)
                       * Mathf.Deg2Rad;
                float thStart = LipTheta(surface0); // 出水角:初液面恰触唇口
                // 滑移段预倾角:边移动边旋转,到位时已带 θ_pre = clamp(出水角, ±30°) ——
                // ≤出水角保证滑移途中水不涌到管口漏出;封顶防少水管一路转到近水平
                float preTilt = Mathf.Clamp(thStart,
                    -PourGlideTiltDeg * Mathf.Deg2Rad, PourGlideTiltDeg * Mathf.Deg2Rad);
                var glideTo = anchor - (Vector2)(Quaternion.Euler(0f, 0f, preTilt * Mathf.Rad2Deg) * frontLocal); // 到位管心位(前沿角钉锚点 @θ_pre)
                // θ(p) 两段式:先从预倾角倾到出水角(占 PourTipPhaseFrac,不出水);出水后按
                // 「液面钉唇口」随消减量自动加大倾角(与 tick 内 drainAxial 同式,严格同步)
                float TipTheta(float p)
                {
                    if (p <= PourTipPhaseFrac)
                        return preTilt + (thStart - preTilt) * Box.UI.BoxTween.EaseInOutCubic(p / PourTipPhaseFrac);
                    float fp = (p - PourTipPhaseFrac) / (1f - PourTipPhaseFrac);
                    float drain = growFinalH * Box.UI.BoxTween.EaseInOutCubic(fp) * unitSrc / unitDst;
                    return LipTheta(surface0 - drain);
                }
                // 滑移:位置滑向锚点,同步旋转到 θ_pre,液带按当前倾角重排(UpdateBands 为 local
                // function,前向调用合法;θ=0 首帧与合带撑满几何一致,零跳变)—— 边移边转
                await Tween01(PourLiftDuration * PourTimeScale, ct, p =>
                {
                    float e = Box.UI.BoxTween.EaseInOutCubic(p);
                    float theta = preTilt * e;
                    UpdateBands(theta, 0f, false);
                    ApplySourceTilt(srcRt, bandRects, bandMats, theta);
                    srcRt.anchoredPosition = Vector2.LerpUnclamped(glideFrom, glideTo, e);
                });

                // —— 2) 绕锚点倾倒:前沿角钉在锚点(管心位 = 锚点 - R(θ)·前沿角),倾角随液面下降按
                //    「液面贴唇口」自动加大(LipTheta)—— 上层正好流出、下层刚好挨着瓶口;
                //    液带反向旋转 + 材质补偿保水面水平。θ 两段式:先快速倾到出水角,随后边续转
                //    边倒。液体运动 = 「管口侧消减」:顶带(被倒色)的管口侧随水量退向闭端,
                //    下层带相对管身完全固定(游戏惯例:分层贴管,不随倒水漂移,
                //    底层永不脱离弧底)。时长与份数成比例(PourTipPerUnitDuration × count):
                //    倒 N 份慢 N 倍,旋转角速度随份数放慢、单份流速恒定 ——
                // 各液带按当前倾角重建几何(屏幕水平带语义):轴向区间 [a,b] 投影到屏幕高度
                // [a·cosθ, b·cosθ](相对管心),最底带底边下探 w·|sinθ| 贴弧 —— 高倾角下沿管轴
                // 延伸无效(轴向位移的竖直分量 ×cosθ≈0),必须直接以屏幕高度定位。
                // 带矩形用「中心锚点」:中心屏幕位 (0, yCenter) 反解管局部坐标
                // (x_c = y_c·tanθ, y_c = yCenter·cosθ),带横跨管包围盒且以管为中心 ——
                // 旧方案沿轴向反算锚点会把锚点推到管外十余倍管宽,整条带渲染在管外(水跑管外)。
                // 顶带轴向顶随已倒量下降(管口侧消减),其余带恒定 → 层间界面稳定
                void UpdateBands(float theta, float drainAxial, bool topEmptied)
                {
                    float quadW = TiltQuadWidth(theta, w, h);
                    float cosT = Mathf.Cos(theta);   // |θ| < 90° 恒正
                    float tanT = Mathf.Tan(theta);
                    float sink = w * Mathf.Abs(Mathf.Sin(theta)); // 底带屏幕下探量(贴弧补偿)
                    for (int r = 0; r < bandRects.Count; r++)
                    {
                        bool isTop = r == bandRects.Count - 1;
                        if (isTop && topEmptied)
                        {
                            bandRects[r].gameObject.SetActive(false); // 已倒空:回程不复现
                            continue;
                        }
                        // 层带轴向区间固定(贴管);只有顶带(被倒色)的管口侧随水量消减退向闭端
                        float a = bandA[r];
                        float b = isTop ? Mathf.Max(a + 0.01f, surface0 - drainAxial) : bandB[r];
                        float yB = a * cosT, yT = b * cosT;           // 底/顶边的屏幕高度(相对管心)
                        float yLow = yB - (r == 0 ? sink : DropOverlapPx); // 最底带下探贴弧;其余带下延 1px
                                                                           // 压住下层带顶缘(防取整发丝缝,同静置布局)
                        float drawH = Mathf.Max(0.01f, yT - yLow);
                        float yCenter = (yT + yLow) * 0.5f;
                        float yC = yCenter * cosT;
                        // 屏幕中心 x 归零的反解:x_c = y_c·tanθ(= yCenter·sinθ)。⚠ 不可写成
                        // yCenter·tanθ:屏幕位 R(θ)·c 的 y 分量含 sin²θ/cosθ 项,倾角越大放大越狠
                        // (60° ≈1.75 倍、87° ≈19 倍),各带会被按轴向中点原样摆回(等效管未旋转),
                        // 层间距拉开 + 底层带整条沉到管外被裁没 —— 分层/底层体积变小/漂移出管外
                        float xC = yC * tanT;
                        bandRects[r].gameObject.SetActive(drawH > 0.02f);
                        SetBandGeometry(bandRects[r], bandMats[r], quadW, w, xC, yC, drawH, h);
                    }
                }

                ServiceLocator.Audio?.PlaySfx(AudioSfx.WaterPour);
                float tipDuration = PourTipPerUnitDuration * count * PourTimeScale;
                bool topEmptied = false; // 顶带(被倒的 run)已倒空:回程不复现
                await Tween01(tipDuration, ct, p =>
                {
                    float theta = TipTheta(p);
                    float fp = Mathf.Clamp01((p - PourTipPhaseFrac) / (1f - PourTipPhaseFrac)); // 出水进度
                    float grown = growFinalH * Box.UI.BoxTween.EaseInOutCubic(fp); // 目标已涨高 = 源已降量
                    float drainAxial = grown * unitSrc / unitDst;     // 顶带管口侧的消减量(轴向)
                    if (surface0 - drainAxial <= bandA[bandA.Count - 1] + 0.02f) topEmptied = true;
                    UpdateBands(theta, drainAxial, topEmptied);
                    ApplySourceTilt(srcRt, bandRects, bandMats, theta);
                    srcRt.anchoredPosition = anchor - (Vector2)(Quaternion.Euler(0f, 0f, theta * Mathf.Rad2Deg) * frontLocal);

                    grow.gameObject.SetActive(grown > 0.01f);
                    SetDropGeometry(growRt, growMat, w * DropWidthFrac, w, growBottom, grown, h);
                    // 水流柱(目标管 Glass 之下):顶 = 前沿唇口(钉在锚点,目标局部系高度恒定),
                    // 底 = 目标管内液面 —— 没入感。不可用管口中心:高倾角时它高出唇口 ~w/2,
                    // 水流顶端会悬空飘在管外
                    float topY = h * (0.5f + PourAnchorAboveFrac), botY = growBottom + grown;
                    // 首尾宽度淡入/淡出:出水建立 ~8% 进度渐入,收尾 15% 渐出(瞬现/瞬断生硬)
                    float widthFactor = fp <= 0f ? 0f
                        : Mathf.Min(1f, fp / 0.08f) * (fp > 0.85f ? (1f - fp) / 0.15f : 1f);
                    stream.gameObject.SetActive(widthFactor > 0.01f); // 出水建立才显示(滑移段隐藏)
                    streamRt.sizeDelta = new Vector2(streamW * Mathf.Clamp01(widthFactor), Mathf.Max(1f, topY - botY));
                    streamRt.anchoredPosition = new Vector2(0f, (topY + botY) * 0.5f);
                });

                // —— 3) 收管:不沿原弧返回 —— 直接从倾倒终态(位置/终角/液带)单段插值回
                // 基准位,位置 + 旋转 + 液带同步,干净利落(原「沿弧转回 + 落地回弹」两段太拖沓)
                float endDrain = growFinalH * unitSrc / unitDst; // 已完成的消减量冻结(水量已转移)
                var tiltEndPos = srcRt.anchoredPosition;         // 倾倒段终点的管心位
                float thetaEnd = TipTheta(1f);                   // 倾倒段终角(液面贴唇口的终态角)
                float backDur = PourBackDuration * PourTimeScale;
                await Tween01(backDur, ct, p =>
                {
                    float e = Box.UI.BoxTween.EaseInOutCubic(p);
                    float theta = thetaEnd * (1f - e);
                    UpdateBands(theta, endDrain, topEmptied);
                    ApplySourceTilt(srcRt, bandRects, bandMats, theta);
                    srcRt.anchoredPosition = Vector2.LerpUnclamped(tiltEndPos, basePos, e);
                });
                srcRt.localRotation = Quaternion.identity; // 兜底清残角(Tween01 终值已为 0)
            }
            catch (OperationCanceledException)
            {
                return; // 视图/架子销毁:临时节点随整架销毁,临时材质由 OnDestroy 回收,无需清理
            }
            catch (Exception e)
            {
                // 意外异常兜底:记日志后照常走收尾,防 _animating 永久为 true(输入锁/盘面刷新全挂死)
                Debug.LogException(e);
            }

            // —— 收尾:解锁 + 重建到真实盘面(临时节点随之销毁)+ 广播 ——
            _animating = false;
            Refresh();
            foreach (var m in _pourTempMats) Destroy(m); // 临时材质已无引用:显式销毁(节点 Destroy 不回收材质)
            _pourTempMats.Clear();
            PourCompleted?.Invoke();
        }

        /// <summary>归一段时驱动:t 从 0 走到 1(按 Time.deltaTime),每帧回调 tick(t);ct 取消即抛出。</summary>
        static async UniTask Tween01(float duration, CancellationToken ct, Action<float> tick)
        {
            float t = 0f;
            while (t < 1f)
            {
                tick(t);
                await UniTask.Yield(ct);
                t = Mathf.Min(1f, t + Time.deltaTime / duration);
            }
            tick(1f);
        }

        /// <summary>液块几何统一写入口:矩形(底部锚定)+ 软裁剪材质 _MaskST 同步更新。
        /// quadW = 矩形实际宽(可为管宽的加宽倍数,倾斜期用),遮罩 UV 按实际几何推导 ——
        /// 高度/宽度变化时矩形与遮罩采样框必须同步,否则剪影会随缩放变形
        /// (不可用 localScale 做液面动画的原因)。目标管生长块用(竖直管,底部锚定)。</summary>
        static void SetDropGeometry(RectTransform rt, Material mat, float quadW, float tubeW, float bottomY, float height, float h)
        {
            rt.sizeDelta = new Vector2(quadW, Mathf.Max(0.01f, height));
            rt.anchoredPosition = new Vector2(0f, bottomY);
            if (mat != null)
            {
                float wFrac = quadW / tubeW; // 管矩形归一化宽(可 >1,越界部分靠遮罩裁掉 + shader UV 夹紧)
                mat.SetVector(MaskSTId, new Vector4(
                    0.5f - wFrac * 0.5f, (bottomY + h * 0.5f) / h, wFrac, height / h));
            }
        }

        /// <summary>液带几何写入口(中心锚定):带矩形是屏幕水平带,中心屏幕位 (xC, yC)(相对管心,
        /// h 单位)反解出的管局部坐标,尺寸 (quadW, drawH)。_MaskST 按中心/尺寸推导 ——
        /// 软裁剪补偿(_MaskRot 枢轴 = 带中心 UV,见 ApplySourceTilt)对此精确,与锚点位置无关。
        /// 中心锚定的意义:带永远以管为中心横跨包围盒,不会因锚点远偏把整条带甩到管外。</summary>
        static void SetBandGeometry(RectTransform rt, Material mat, float quadW, float tubeW,
            float xC, float yC, float drawH, float tubeH)
        {
            rt.anchoredPosition = new Vector2(xC, yC);
            rt.sizeDelta = new Vector2(quadW, Mathf.Max(0.01f, drawH));
            if (mat != null)
            {
                float wFrac = quadW / tubeW, hFrac = drawH / tubeH;
                mat.SetVector(MaskSTId, new Vector4(
                    0.5f + xC / tubeW - wFrac * 0.5f,
                    0.5f + yC / tubeH - hFrac * 0.5f,
                    wFrac, hFrac));
            }
        }

        /// <summary>倾角 θ 下液块所需的屏幕宽 = 旋转后管矩形的包围盒宽(w·|cosθ| + h·|sinθ|)
        /// + 羽化余量 —— 内腔水平跨度随倾角增大(最大倾角下 ≈4 倍管宽),水平液块必须盖满两壁,
        /// 否则露出液块自身直边(几何硬边,「楼梯/错位」的成因);超宽部分由遮罩裁掉 + UV 夹紧,
        /// θ=0 时自然回归常态宽。</summary>
        static float TiltQuadWidth(float thetaRad, float w, float h)
            => w * Mathf.Abs(Mathf.Cos(thetaRad)) + h * Mathf.Abs(Mathf.Sin(thetaRad)) + w * 0.15f;

        /// <summary>源管倾斜姿态(三件套配对,缺一即穿帮):① 管体根节点旋转 θ(瓶身/玻璃随动);
        /// ② 液带子节点反向旋转 θ 保持屏幕水平(水面恒水平);③ 各带临时材质 _MaskRot =
        /// (cosθ, sinθ, 液带中心UV) —— 片元采样遮罩前把 UV 绕该枢轴(经 _MaskAspect 等比
        /// 空间)旋回试管空间,裁剪边界贴合倾斜后的内腔。补偿枢轴必须与液带反向旋转的枢轴
        /// (带中心,pivot 为 (0.5,0.5))同点;带几何由调用方先经 UpdateBands 刷新。</summary>
        static void ApplySourceTilt(RectTransform root, List<RectTransform> rects, List<Material> mats, float thetaRad)
        {
            root.localRotation = Quaternion.Euler(0f, 0f, thetaRad * Mathf.Rad2Deg); // 管体倾斜
            var q = Quaternion.Euler(0f, 0f, -thetaRad * Mathf.Rad2Deg);             // 液带反向旋转 → 水平
            var rot = new Vector4(Mathf.Cos(thetaRad), Mathf.Sin(thetaRad), 0f, 0f);
            for (int i = 0; i < rects.Count; i++)
            {
                rects[i].localRotation = q;
                var m = mats[i];
                if (m == null) continue;
                var st = m.GetVector(MaskSTId);
                m.SetVector(MaskRotId, new Vector4(rot.x, rot.y,
                    st.x + st.z * 0.5f, st.y + st.w * 0.5f)); // 枢轴 = 带中心
            }
        }

        GameObject BuildTube(int index, float w, float h)
        {
            // 纯容器:自身无 Image(不挡射线也不产生图元);液块先入、Glass 后入,渲染序即先水后玻璃
            return NewNode(_self, "Tube" + index, w, h);
        }

        /// <summary>玻璃顶层节点(追加为容器最后子级 = 最上层):承接点击,杯身贴图/占位色由调用方上色。</summary>
        Image AppendGlass(RectTransform tube, int index, float w, float h)
        {
            var go = NewChild(tube, "Glass", w, h, Color.white, true);
            var img = go.GetComponent<Image>();
            var btn = go.AddComponent<Button>(); // 纯 Unity Button 承接点击;免过渡闪烁
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            var idx = index;
            btn.onClick.AddListener(() => OnTubeTap(idx));
            return img;
        }

        /// <summary>纯容器节点(无 Image;供试管根/未来分组用,不参与渲染与射线)。</summary>
        GameObject NewNode(RectTransform parent, string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        GameObject NewChild(RectTransform parent, string name, float w, float h, Color color, bool raycast = true)
        {
            var go = NewNode(parent, name, w, h);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast; // 液块关闭:点击穿透到 Glass 顶层;Glass 开(承接 Button)
            return go;
        }

        void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }
    }
}
