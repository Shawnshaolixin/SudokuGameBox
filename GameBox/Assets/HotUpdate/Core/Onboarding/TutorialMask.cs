using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Box.HotUpdate.Core.Onboarding
{
    /// <summary>
    /// 引导遮罩(通用件,10 文档 §16.7 9.5-1 形态;TutorialFlow 每段引导建一个实例):
    /// 独立 ScreenSpaceOverlay Canvas(层内 UILayer ≤600、BoxToast 20000,本层取 15000 ——
    /// 引导要压住 Popup 玩法视图,又让轻提示保持可读)。纯代码运行时创建(热更组件不进 prefab,
    /// 20 文档 §4 纪律同源,不依赖任何 Addressables 资产)。
    ///
    /// 视觉/交互:全屏半透明黑,目标矩形「镂空」(运行时挖洞纹理,孔内保持可点 ——
    /// Image.alphaHitTestMinimumThreshold 让孔内像素点击穿透到下层玩法按钮);
    /// 孔周白色细框呼吸(引导注意力);目标下/上方文案气泡;右上角常驻「跳过」。
    /// 孔洞坐标 = 玩法控件 WorldCorners 的屏幕矩形(本类内部换算到本画布局部系,
    /// 与目标处于哪个 Canvas/缩放体系无关)。
    /// </summary>
    public sealed class TutorialMask : MonoBehaviour
    {
        const int SortingOrder = 15000;                       // 见类头:盖过 UILayer.Popup(300),低于 BoxToast(20000)
        const float RefWidth = 1080f, RefHeight = 1920f;      // 与 BoxUI/BoxToast 基准一致(设计局部分)
        const float HolePad = 10f;                            // 孔洞外扩(细框贴孔外,防误触相邻控件)
        const float StripThick = 5f;                          // 高亮细框厚度
        const float BubbleWidth = 940f;                       // 气泡宽(文本区内缩 60)
        const float TextWidth = BubbleWidth - 60f;

        RectTransform _rootRt;        // 画布根(局部分 ±540/±960)
        Transform _fx;                // 每步重建的动态子树(孔+框+气泡;跳过钮常驻在其外)
        Image[] _strips = new Image[4]; // 呼吸细框(上/下/左/右)
        bool _breathing;              // 细框动画开关(孔为全屏时无框)

        static Sprite _whiteSprite;   // 1×1 白点(细框/占位共用)
        static Sprite _roundSprite;   // 圆角矩形(气泡/跳过钮,9-slice)
        static Sprite _lastHoleSprite; // 上一步孔洞纹理(延迟销毁引用,防 GC 前闪删)

        /// <summary>创建遮罩实例(画布+跳过钮就位;ShowStep 前不显示)。</summary>
        public static TutorialMask Create(string name, string skipText, System.Action onSkip)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (Application.isPlaying) DontDestroyOnLoad(go); // 独立画布常驻(与 BoxToast 同策略)

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f; // 与 BoxUI 参数一致(同屏同缩放)

            var mask = go.AddComponent<TutorialMask>();
            mask.Init(skipText, onSkip);
            return mask;
        }

        void Init(string skipText, System.Action onSkip)
        {
            _rootRt = (RectTransform)transform;
            _fx = new GameObject("Fx").transform;
            _fx.SetParent(transform, false);

            // 右上角常驻「跳过」(锚定右上,随安全区由视图层整体让位;跳过 = 永不打扰,见 TutorialFlow.Skip)
            var skip = NewRoundRect("Skip", new Vector2(172f, 66f), new Color(0.07f, 0.08f, 0.11f, 0.85f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-44f, -44f));
            skip.transform.SetParent(transform, false);
            var outline = NewImage(skip, "Outline", new Color(1f, 1f, 1f, 0.30f), new Vector2(178f, 72f), Vector2.zero);
            outline.rectTransform.SetSiblingIndex(0); // 垫底:细白描边(比按钮底大 6 局部)
            var btn = skip.gameObject.AddComponent<Button>(); // 标准 Button 承接点击
            btn.targetGraphic = skip.GetComponent<Image>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onSkip?.Invoke());
            var label = NewText(skip, "Label", skipText);
            label.fontSize = 28;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform, 20f, 8f);
        }

        /// <summary>
        /// 展示/刷新一步:重算孔洞(玩法控件世界角 → 本画布局部)并重建动态子树。
        /// rect 为空矩形 = 无目标可挖(如教程目标暂不可解析):全屏不遮黑,仅气泡+跳过,
        /// 保证玩家可正常操作、引导只提示不挡路。
        /// </summary>
        public void ShowStep(Rect holeScreenRect, string copy)
        {
            var hole = LocalizeRect(holeScreenRect);
            bool fullHole = hole.width <= 0f || hole.height <= 0f;
            if (fullHole) hole = new Rect(-RefWidth / 2f, -RefHeight / 2f, RefWidth, RefHeight);

            ClearFx();
            // 呼吸细框只在有真实孔洞时出现(目标可见性标记;全屏孔 = 无遮黑自然无框)
            for (int i = 0; i < _strips.Length; i++) _strips[i] = null;
            if (!fullHole)
            {
                var padded = Expand(hole, HolePad);
                BuildDim(padded);
                BuildStrips(padded);
            }
            BuildBubble(hole, copy);
            gameObject.SetActive(true);
            _breathing = !fullHole;
        }

        /// <summary>收尾销毁(结束后不可再用)。</summary>
        public void Close()
        {
            if (this == null || gameObject == null) return;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        // ---- 呼吸动画(仅 PlayMode 有效;单测/预览 headless 不经此路径) ----
        void Update()
        {
            if (!_breathing) return;
            float a = 0.55f + 0.40f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.5f)); // ~1.4s 周期
            for (int i = 0; i < _strips.Length; i++)
            {
                if (_strips[i] != null) _strips[i].color = new Color(1f, 1f, 1f, a);
            }
        }

        // ---- 动态子树重建 ----

        void ClearFx()
        {
            for (int i = _fx.childCount - 1; i >= 0; i--)
            {
                var c = _fx.GetChild(i);
                c.gameObject.SetActive(false); // 先藏再毁:避免延迟销毁的一帧新旧叠加
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
        }

        /// <summary>全屏半透明黑 + 孔洞挖空:运行时纹理(540×960,2×2 局部=1 纹素,孔缘 2 纹素羽化)。
        /// 孔内 α=0 → alphaHitTest(阈值 0.5)判不命中 → 点击穿透到下层玩法按钮;遮罩区 α≈0.55 拦点击。</summary>
        void BuildDim(Rect holeLocal)
        {
            const int tw = 540, th = 960;
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[tw * th];
            // 孔矩形 → 纹理坐标(局部 1080×1920 ↔ 纹理 540×960 等比减半;局部以画布中心为原点)
            float x0 = (holeLocal.xMin + RefWidth / 2f) / 2f, x1 = (holeLocal.xMax + RefWidth / 2f) / 2f;
            float y0 = (holeLocal.yMin + RefHeight / 2f) / 2f, y1 = (holeLocal.yMax + RefHeight / 2f) / 2f;
            const byte DimA = 141; // ≈0.55 遮罩不透明度
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    // 矩形 SDF 近似:到边缘的外扩距离(孔内/边缘 = 0)
                    float qx = Mathf.Max(x0 - x, x - x1, 0f);
                    float qy = Mathf.Max(y0 - y, y - y1, 0f);
                    float dist = Mathf.Max(qx, qy);
                    float a = Mathf.Clamp01(dist / 2f) * DimA; // 2 纹素(≈4 局部)羽化
                    px[y * tw + x] = new Color32(0, 0, 0, (byte)a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            if (_lastHoleSprite != null) // 上一帧纹理延迟销毁(防同帧旧孔残留)
            {
                if (Application.isPlaying) Destroy(_lastHoleSprite);
                else DestroyImmediate(_lastHoleSprite);
            }
            _lastHoleSprite = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), 100f);

            var img = NewImage(_fx, "Dim", Color.white, new Vector2(RefWidth, RefHeight), Vector2.zero); // 全屏
            img.sprite = _lastHoleSprite;
            img.alphaHitTestMinimumThreshold = 0.5f; // 关键:孔内像素点击穿透到下层
        }

        /// <summary>孔周白框(四长条,呼吸驱动;与孔缘留 2 局部空隙,视觉贴边不遮目标)。</summary>
        void BuildStrips(Rect hole)
        {
            float x = hole.center.x, y = hole.center.y, w = hole.width, h = hole.height;
            _strips[0] = Strip(new Rect(x - w / 2f - 2f, hole.yMax + 2f, w + 4f, StripThick)); // 上
            _strips[1] = Strip(new Rect(x - w / 2f - 2f, hole.yMin - 2f - StripThick, w + 4f, StripThick)); // 下
            _strips[2] = Strip(new Rect(hole.xMin - 2f - StripThick, y - h / 2f - 2f, StripThick, h + 4f)); // 左
            _strips[3] = Strip(new Rect(hole.xMax + 2f, y - h / 2f - 2f, StripThick, h + 4f)); // 右
        }

        Image Strip(Rect r)
        {
            var go = NewObject("Strip", r.size, r.center, Color.white, true);
            go.transform.SetParent(_fx, false);
            return go.GetComponent<Image>();
        }

        /// <summary>文案气泡:目标孔下缘外侧优先,空间不足翻到上缘,再不足居中(引导永不越界不可读)。</summary>
        void BuildBubble(Rect hole, string copy)
        {
            var text = NewText(_fx, "Copy", copy);
            text.fontSize = 30;
            text.alignment = TextAlignmentOptions.Center;
            var pref = text.GetPreferredValues(copy, TextWidth, 640f); // 同步测算换行高度
            float h = Mathf.Clamp(pref.y, 72f, 420f) + 36f;

            float cy;
            float below = hole.yMin - 18f - h / 2f;
            float above = hole.yMax + 18f + h / 2f;
            if (below > -RefHeight / 2f + h / 2f + 30f) cy = below;      // 下侧有空间
            else if (above < RefHeight / 2f - h / 2f - 30f) cy = above;  // 否则翻到上侧
            else cy = 0f;                                                // 极端(孔占满屏):居中

            var bg = NewRoundRect("Bubble", new Vector2(BubbleWidth, h), new Color(0.07f, 0.08f, 0.11f, 0.88f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, cy));
            bg.transform.SetParent(_fx, false);
            text.rectTransform.SetParent(bg.transform, false); // 文本挪进气泡(避独立层级命中检测)
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(30f, 18f);
            text.rectTransform.offsetMax = new Vector2(-30f, -18f);
        }

        // ---- 坐标与构图工具 ----

        /// <summary>屏幕(世界)矩形 → 本画布局部矩形:四角经 RectTransformUtility 中转,
        /// 与目标所处 Canvas/CanvasScaler 体系无关(见类头)。空矩形原样返回由调用方判定。</summary>
        Rect LocalizeRect(Rect screenRect)
        {
            if (screenRect.width <= 0f || screenRect.height <= 0f) return screenRect;
            var lt = new Vector2(screenRect.xMin, screenRect.yMax);
            var rt = new Vector2(screenRect.xMax, screenRect.yMax);
            var lb = new Vector2(screenRect.xMin, screenRect.yMin);
            var rb = new Vector2(screenRect.xMax, screenRect.yMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, lt, null, out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, rt, null, out var b);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, lb, null, out var c);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, rb, null, out var d);
            var min = new Vector2(Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)),
                Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)));
            var max = new Vector2(Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)),
                Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        static Rect Expand(Rect r, float p) => new Rect(r.xMin - p, r.yMin - p, r.width + 2 * p, r.height + 2 * p);

        static GameObject NewObject(string name, Vector2 size, Vector2 center, Color color, bool whiteSprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); // 全部锚中心 → anchoredPosition=局部坐标
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = center;
            var img = go.GetComponent<Image>();
            img.color = color;
            if (whiteSprite) img.sprite = WhiteSprite();
            return go;
        }

        static Image NewImage(Transform parent, string name, Color color, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); // 锚中心 → anchoredPosition = 父局部坐标
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = color;
            return go.GetComponent<Image>();
        }

        /// <summary>圆角矩形(9-slice;气泡/跳过钮/描边共用同一张运行时小图)。</summary>
        static RectTransform NewRoundRect(string name, Vector2 size, Color color, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.sprite = RoundSprite();
            img.type = Image.Type.Sliced; // border 生效:角落圆角、中部拉伸
            img.color = color;
            return rt;
        }

        static void Stretch(RectTransform rt, float marginX, float marginY)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(marginX, marginY);
            rt.offsetMax = new Vector2(-marginX, -marginY);
        }

        static TMP_Text NewText(Transform parent, string name, string content)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(TextWidth, 60f);
            rt.anchoredPosition = Vector2.zero;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.raycastTarget = false; // 文案不拦点击(孔外点击一律落在遮罩黑区)
            text.enableWordWrapping = true; // TMP 新 API(textWrappingMode)在本包版本不可达,沿用旧属性(仅弃用警告)
            text.color = new Color(0.96f, 0.97f, 1f, 1f);
            return text;
        }

        // ---- 共享小图(懒建一次;1×1 白 / 32×32 圆角) ----

        static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var px = new Color32[4];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels32(px);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
            return _whiteSprite;
        }

        static Sprite RoundSprite()
        {
            if (_roundSprite != null) return _roundSprite;
            const int size = 32, radius = 8; // 半径 8px 的圆角 → 9-slice border = 8
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Min(Mathf.Min(x, size - 1 - x), radius - 0.5f);
                    float cy = Mathf.Min(Mathf.Min(y, size - 1 - y), radius - 0.5f);
                    float d = Mathf.Sqrt(Mathf.Max(0f, (x - cx) * (x - cx) + (y - cy) * (y - cy)));
                    bool inside = x < radius && y < radius ? d <= radius // 圆角象限:圆弧内
                        : x >= size - radius && y >= size - radius ? d <= radius
                        : x < radius && y >= size - radius ? d <= radius
                        : x >= size - radius && y < radius ? d <= radius
                        : true; // 其余区域(直边/中部)全透不过判定
                    px[y * size + x] = inside ? Color.white : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _roundSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius)); // border=圆角半径
            return _roundSprite;
        }
    }
}
