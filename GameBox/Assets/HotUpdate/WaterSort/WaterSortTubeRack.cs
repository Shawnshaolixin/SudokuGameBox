using System;
using System.Collections.Generic;
using Box.Services;
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
    /// 交互:点击试管选中(高亮),再点另一支 = 请求倒水(由视图经会话判定合法性);
    /// 点自己取消选中。组件运行期 AddComponent 到 TubeArea(不走 prefab 序列化,20 文档 §4 纪律同源)。
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

        /// <summary>倒水请求(源, 目标):发起时保持源选中态不变,成功与否由视图裁定——
        /// 合法倒水后视图走「清选中 + 重建」(RefreshTubeArea);非法则仅抖动,选中保留可再试其他目标管。</summary>
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
        bool _refreshScheduled;        // 合帧重建去重标记(见 ScheduleRefresh)

        // shader 属性 ID(缓存避免每次重建字符串查表)
        static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        static readonly int MaskSTId = Shader.PropertyToID("_MaskST");

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
            if (_self == null) _self = (RectTransform)transform;
            ClearChildren();
            EnsureSprites(); // 贴图/遮罩懒加载(首帧可能未就绪,就绪后合帧重建,见 ScheduleRefresh)

            var board = _session.Board;
            int n = board.TubeCount;
            // 尺寸自适应:管宽随管数收窄,高度按宽高比 4.6(经典细长试管),整体在容器内居中
            float w = Mathf.Min(110f, (_self.rect.width - 80f - (n - 1) * 12f) / n);
            float h = Mathf.Min(w * 4.6f, _self.rect.height - 60f);
            float startX = -((n - 1) * (w + 12f)) * 0.5f;

            for (int t = 0; t < n; t++)
            {
                int drops = board.TopCount(t);
                var col = BuildTube(t, w, h); // 空容器(无 Image 不挡射线);液块先入子级、Glass 最后入
                var rt = (RectTransform)col.transform;
                rt.anchoredPosition = new Vector2(startX + t * (w + 12f), 0);

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

        /// <summary>释放派生/基材质(重建复用缓存,仅销毁时清一次,防原生材质泄漏)。</summary>
        void OnDestroy()
        {
            foreach (var kv in _dropMaterials) Destroy(kv.Value);
            _dropMaterials.Clear();
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

        /// <summary>清空选中标记(提交型重建前由视图调用;重建随之进行,无需本方法自行重绘)。</summary>
        public void ClearSelection() => _selected = -1;

        void OnTubeTap(int index)
        {
            if (_selected < 0 || _selected == index)
            {
                _selected = _selected == index ? -1 : index; // 首次=选中并重绘高亮;点同支=取消
                Refresh();
                return;
            }
            // 已选中源管后点另一支:交视图裁决。此处不清选中也不重绘——
            // 合法倒水由视图刷新摘选中;非法倒水保留选中仅抖动,玩家可立刻再试目标。
            PourRequested?.Invoke(_selected, index);
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
