using System.Threading;
using Box.Services;
using Box.UI;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 首局三步新手引导(产品拍板:极简三步,不做引导框架)。
    /// 高亮顺序:棋盘 → 数字盘 → 提示按钮;整屏遮罩拦截输入,步骤 1/2 点任意处前进,
    /// 步骤 3 以「Got it!」收尾;右上角「Skip」随时退出。完成(Skip/Got it)后由上层写
    /// PlayerPrefs.HasShownTutorial=1,此后不再弹出(中途退出不写,下次进对局再引导)。
    /// 纯代码构建覆盖层(棋盘格同理),不动 prefab/Addressables 资产,热更零资产成本。
    /// 视觉对齐 docs/UIDesignSystem Token:半透明黑遮罩 + 白描边高亮 + Surface/Primary 说明卡。
    /// </summary>
    public sealed class FirstRunGuide
    {
        /// <summary>已引导标记键(产品指定名;PlayerPrefs 属"偏好类"UI 状态,不进加密存档)。</summary>
        public const string PrefsKey = "HasShownTutorial";

        public static bool HasShown => PlayerPrefs.GetInt(PrefsKey, 0) == 1;

        public static void MarkShown()
        {
            PlayerPrefs.SetInt(PrefsKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>是否引导播放中(视图用其拦截返回键/防重复实例)。</summary>
        public bool IsRunning { get; private set; }

        // ---- 步骤定义:目标路径(视图根下)+ 文案键(顺序即步骤顺序) ----
        // unionChildren=true:量目标改为"直接子节点并集"。NumberPanel 是铺满屏下段的
        // 布局容器(数字行 HLG 居中于内),量容器自身会高亮整个下半屏 → 改量 9 个数字按钮。

        static readonly (string path, string textKey, bool unionChildren)[] s_Steps =
        {
            ("BoardPlaceholder", "tutorial.step.grid", false), // 1:数独棋盘(内容即容器,直量)
            ("NumberPanel", "tutorial.step.numpad", true),     // 2:数字键盘(容器拉伸,量按钮并集)
            ("HintButton", "tutorial.step.hint", false),       // 3:提示按钮(叶子按钮,直量)
        };

        // ---- 主题色(UIDesignSystem Token;照 GameplayView 常量风格) ----

        static readonly Color s_Dim = new Color(0f, 0f, 0f, 0.58f);          // 遮罩:半透明黑
        static readonly Color s_Frame = new Color(1f, 1f, 1f, 0.95f);        // 高亮描边:白
        static readonly Color s_Card = new Color(1f, 0.976f, 0.914f, 0.97f); // Surface/Primary 说明卡
        static readonly Color s_Text = new Color(0.227f, 0.165f, 0.102f);    // Text/Primary 主文字
        static readonly Color s_TextSub = new Color(0.502f, 0.424f, 0.302f); // Text/Secondary 次文字
        static readonly Color s_Primary = new Color(0.914f, 0.471f, 0.196f); // Primary 主按钮橙(Got it)
        static readonly Color s_Chip = new Color(0f, 0f, 0f, 0.30f);         // Skip 半透明底
        static readonly Color s_Transparent = new Color(1f, 1f, 1f, 0f);     // 全屏前进层(仅收点击)

        const float FrameThick = 6f;   // 高亮描边粗细
        const float TargetPad = 8f;    // 高亮目标外扩(描边与目标间的呼吸空间)
        const float CardMarginX = 70f; // 说明卡左右边距
        const float CardHeight = 285f; // 说明卡高(正文 + 底部按钮/点按提示区)
        const float BannerOffset = 36f;// 说明卡底边距棋盘顶的间距
        const float SkipW = 150f, SkipH = 70f, SkipMargin = 20f; // Skip 尺寸/右上边距
        const float GotItW = 320f, GotItH = 96f;                 // Got it 尺寸(说明卡底部居中)

        readonly Transform _root;               // 视图根(全部节点挂其下,随场景销毁)
        readonly RectTransform _overlay;        // 全屏覆盖层(自身坐标系=步骤测量基准)
        readonly TextMeshProUGUI _fontTemplate; // 字体模板:复制视图内现有 TMP(字符集/材质一致)

        readonly Image[] _dimPieces = new Image[4];   // 4 块遮罩:上下全宽 + 左右中带,互不重叠(叠色会加深)
        readonly Image[] _framePieces = new Image[4]; // 高亮描边 4 条
        RectTransform _card;                          // 顶部说明卡
        TextMeshProUGUI _message, _tapHint;           // 说明卡正文 / 点按小字
        RectTransform _gotIt, _skip;                  // 收尾按钮 / 跳过
        Button _advanceButton;                        // 全屏前进层(仅步骤 1/2)

        UniTaskCompletionSource _advanceTcs; // 步骤 1/2 点按信号(每次等待新建,单次可复用)
        UniTaskCompletionSource _finishTcs;  // Got it / Skip 收尾信号
        bool _skipped;

        /// <summary>构建全部 UI 并挂到视图根下。fontTemplate 为空时用 TMP 默认字体(棋盘数字同款)。</summary>
        public FirstRunGuide(Transform viewRoot, TextMeshProUGUI fontTemplate = null)
        {
            _root = viewRoot;
            _fontTemplate = fontTemplate;

            var go = new GameObject("FirstRunGuide", typeof(RectTransform));
            go.transform.SetParent(viewRoot, false);
            _overlay = (RectTransform)go.transform;
            _overlay.anchorMin = Vector2.zero; // 铺满视图根(视图根铺满画布)
            _overlay.anchorMax = Vector2.one;
            _overlay.sizeDelta = Vector2.zero;
            _overlay.SetAsLastSibling(); // 最后子节点 = 同级最上层绘制

            BuildBlockerAndHighlights();
            BuildCard();
            BuildButtons();
            // 摆好第一步前整体不可见:组件各节点默认尺寸为 100x100,先见一帧会闪矩形
            _overlay.gameObject.SetActive(false);
        }

        /// <summary>
        /// 播放引导(依序 3 步)。正常结束(含 Skip)后由上层 MarkShown;
        /// 取消(视图销毁)时抛 OperationCanceledException,由上层决定是否标记。
        /// </summary>
        public async UniTask RunAsync(CancellationToken ct)
        {
            if (_overlay == null) return; // 已销毁
            IsRunning = true;
            try
            {
                _overlay.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases(); // 同步执行画布布局:stretch 尺寸当帧可用(等帧会闪默认矩形)
                for (int i = 0; i < s_Steps.Length; i++)
                {
                    ApplyStep(i);
                    bool last = i == s_Steps.Length - 1;
                    if (last)
                        await WaitFinishAsync(ct); // 步骤 3:只等 Got it / Skip
                    else
                    {
                        await WaitAdvanceAsync(ct); // 步骤 1/2:点任意处前进
                        if (_skipped) break;
                    }
                }
            }
            finally { IsRunning = false; } // 取消也复位状态(异常向上抛,供上层判定)
        }

        /// <summary>销毁覆盖层(视图 OnDestroy/引导结束调用;幂等)。</summary>
        public void Dispose()
        {
            if (_overlay != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_overlay.gameObject);
                else UnityEngine.Object.DestroyImmediate(_overlay.gameObject);
            }
            _advanceTcs?.TrySetCanceled();
            _finishTcs?.TrySetCanceled();
        }

        // ---- 覆盖层构建(节点一次性建齐,步骤间只改位置/尺寸/显隐) ----

        void BuildBlockerAndHighlights()
        {
            for (int i = 0; i < 4; i++)
            {
                _dimPieces[i] = CreateImage("Dim" + i, s_Dim, _overlay).GetComponent<Image>();
                _dimPieces[i].raycastTarget = false; // 点击统一走全屏前进层
            }
            for (int i = 0; i < 4; i++)
            {
                _framePieces[i] = CreateImage("Frame" + i, s_Frame, _overlay).GetComponent<Image>();
                _framePieces[i].raycastTarget = false;
            }
        }

        void BuildCard()
        {
            _card = CreateImage("GuideCard", s_Card, _overlay).GetComponent<RectTransform>();
            _card.GetComponent<Image>().raycastTarget = false;

            _message = CreateText("GuideText", _card, s_Text, 40f);
            // 正文区:卡内顶部 26 起、高 120(两行 40px 文案),左右各留 30
            SetStretch(_message.rectTransform, 30f, 26f, 30f, CardHeight - 26f - 120f);

            _tapHint = CreateText("TapHint", _card, s_TextSub, 28f);
            // 点按小字:正文之下、卡底 26 之上
            SetStretch(_tapHint.rectTransform, 30f, 26f + 120f + 12f, 30f, 26f);
            _tapHint.text = L10n.Get("tutorial.tap");
        }

        void BuildButtons()
        {
            // 全屏前进层(透明 Image 兜底射线,静音无缩放反馈):按钮在 Card 之后创建=盖在卡上,
            // Got it/Skip 再后创建=压过前进层。创建顺序即 z 序。
            var advance = CreateImage("Advance", s_Transparent, _overlay);
            advance.anchorMin = Vector2.zero; // 铺满覆盖层
            advance.anchorMax = Vector2.one;
            advance.sizeDelta = Vector2.zero;
            var btn = advance.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            _advanceButton = btn;
            btn.onClick.AddListener(() => _advanceTcs?.TrySetResult());
            _advanceButton.gameObject.SetActive(false); // 步骤 3 隐藏:强制走 Got it/Skip

            _gotIt = CreateChip("GotIt", s_Primary, L10n.Get("tutorial.gotit"), Color.white, 42f, GotItW, GotItH);
            _gotIt.GetComponent<BoxButton>().OnClick(() => _finishTcs?.TrySetResult());
            _gotIt.gameObject.SetActive(false); // 仅步骤 3 显示

            _skip = CreateChip("Skip", s_Chip, L10n.Get("tutorial.skip"), Color.white, 32f, SkipW, SkipH);
            _skip.GetComponent<BoxButton>().OnClick(() =>
            {
                _skipped = true; // 跳过:放行进行中的等待,RunAsync 收尾
                _finishTcs?.TrySetResult();
                _advanceTcs?.TrySetResult();
            });
        }

        /// <summary>应用某一步:重测目标矩形,摆放遮罩/描边/说明卡/按钮并刷新文案。</summary>
        void ApplyStep(int index)
        {
            var (path, textKey, unionChildren) = s_Steps[index];
            var target = _root.Find(path) as RectTransform;
            float w = _overlay.rect.width, h = _overlay.rect.height;
            float halfW = w * 0.5f, halfH = h * 0.5f; // overlay 本地系原点在中心

            if (target != null)
            {
                // 高亮矩形:标记并集的步骤测"直接子节点包围盒"(数字键盘),否则量自身
                var t = unionChildren ? ChildrenLocalRect(target, TargetPad) : TargetLocalRect(target, TargetPad);
                // 距屏上/左边的正向偏移(Place 采用同语义,内部换算到本地系)
                float topOff = halfH - t.yMax;            // 目标上边距屏顶
                float bottomOff = halfH - t.yMin;         // 目标下边距屏顶(即整高 - 距屏底)
                float leftOff = t.xMin + halfW;           // 目标左边距屏左
                float rightOff = halfW - t.xMax;          // 目标右边距屏右

                // 遮罩 4 块:上下全宽 + 左右只占上下带之间,四块互不重叠(叠色会加深)
                Place(_dimPieces[0].rectTransform, 0f, 0f, w, topOff);                    // 上带
                Place(_dimPieces[1].rectTransform, 0f, bottomOff, w, h - bottomOff);      // 下带
                Place(_dimPieces[2].rectTransform, 0f, topOff, leftOff, bottomOff - topOff);       // 左中带
                Place(_dimPieces[3].rectTransform, w - rightOff, topOff, rightOff, bottomOff - topOff); // 右中带

                // 描边 4 条:紧贴外扩矩形外侧 2px,白线围出高亮框
                float fx = leftOff - 2f, fy = topOff - 2f, fw = t.width + 4f, fh = t.height + 4f;
                Place(_framePieces[0].rectTransform, fx, fy, fw, FrameThick);                  // 上
                Place(_framePieces[1].rectTransform, fx, fy + fh - FrameThick, fw, FrameThick); // 下
                Place(_framePieces[2].rectTransform, fx, fy, FrameThick, fh);                  // 左
                Place(_framePieces[3].rectTransform, fx + fw - FrameThick, fy, FrameThick, fh); // 右
            }
            else
            {
                // 目标缺失(资产被改):本步只显示文案,遮罩保持上一帧位置
                Debug.LogWarning($"[引导] 步骤 {index + 1} 目标 {path} 缺失,仅显示文案");
            }

            // 说明卡:卡底 = 棋盘顶上方 BannerOffset(棋盘位置步间稳定,三步共用一处)
            float boardTopOff = halfH - TargetLocalRect(_root.Find("BoardPlaceholder") as RectTransform, 0f).yMax;
            float cardBottomOff = Mathf.Max(boardTopOff - BannerOffset, CardHeight + 60f); // 短屏防卡顶越界
            Place(_card, CardMarginX, cardBottomOff - CardHeight, w - CardMarginX * 2f, CardHeight);

            // 按钮:Got it 于卡底上方 18 居中(压卡上),Skip 右上角常驻
            Place(_gotIt, (w - GotItW) * 0.5f, cardBottomOff - 18f - GotItH, GotItW, GotItH);
            Place(_skip, w - SkipW - SkipMargin, SkipMargin, SkipW, SkipH);

            // 步骤文案/按钮状态:末步给 Got it,前两步给点按小字 + 全屏前进层
            _message.text = L10n.Get(textKey);
            bool last = index == s_Steps.Length - 1;
            _tapHint.gameObject.SetActive(!last);
            _advanceButton.gameObject.SetActive(!last);
            _gotIt.gameObject.SetActive(last);
        }

        async UniTask WaitAdvanceAsync(CancellationToken ct)
        {
            _advanceTcs = new UniTaskCompletionSource(); // 单次信号,每次新建(复用会残留上次结果)
            await _advanceTcs.Task.AttachExternalCancellation(ct);
        }

        async UniTask WaitFinishAsync(CancellationToken ct)
        {
            _finishTcs = new UniTaskCompletionSource();
            await _finishTcs.Task.AttachExternalCancellation(ct);
        }

        // ---- 几何工具(overlay 本地坐标系,左上原点:右/下为正) ----

        /// <summary>目标世界角 → overlay 本地矩形(含外扩 pad)。画布为 overlay 模式,相机传 null。</summary>
        Rect TargetLocalRect(RectTransform target, float pad)
        {
            if (target == null) return new Rect(0f, 0f, 1f, 1f);
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _overlay, RectTransformUtility.WorldToScreenPoint(null, corners[i]), null, out var local);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            return new Rect(min.x - pad, min.y - pad, max.x - min.x + pad * 2f, max.y - min.y + pad * 2f);
        }

        /// <summary>
        /// 直接子节点世界矩形并集 → overlay 本地矩形(含外扩 pad)。
        /// 布局容器(如 NumberPanel:拉伸容器内 HLG 数字行)自身包围盒 ≫ 可视内容,高亮应圈内容。
        /// </summary>
        Rect ChildrenLocalRect(RectTransform parent, float pad)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i) as RectTransform;
                if (child == null) continue;
                var r = TargetLocalRect(child, 0f);
                min = Vector2.Min(min, r.min);
                max = Vector2.Max(max, r.max);
            }
            // 无子节点(防御):退化为自身矩形,避免空区间炸尺寸
            if (min.x > max.x || min.y > max.y) return TargetLocalRect(parent, pad);
            return new Rect(min.x - pad, min.y - pad, max.x - min.x + pad * 2f, max.y - min.y + pad * 2f);
        }

        /// <summary>
        /// 按"距屏左上角偏移"放置:参数为从屏上/左边的距离(x 右、y 下为正),
        /// 内部换算为锚点 (0,1)+pivot(0,1) 的 anchoredPosition(UGUI 本地系 Y 向上)。
        /// </summary>
        static void Place(RectTransform rt, float xFromLeft, float yFromTop, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(xFromLeft, -yFromTop);
            rt.sizeDelta = new Vector2(width, height);
        }

        /// <summary>四边留白拉伸(anchor 0,0~1,1,offset 以各自锚点为准,正值内缩)。</summary>
        static void SetStretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(left, bottom);   // 左下锚点:向中心内缩
            rt.offsetMax = new Vector2(-right, -top);   // 右上锚点:向中心内缩
        }

        /// <summary>纯色 Image 节点并挂到 parent 下(不挂父级会成场景根孤儿,无 Canvas 祖先不渲染)。</summary>
        static RectTransform CreateImage(string name, Color color, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        /// <summary>文本节点:复制模板 TMP 的字体/材质(字符集一致),默认居中。</summary>
        TextMeshProUGUI CreateText(string name, RectTransform parent, Color color, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (_fontTemplate != null)
            {
                tmp.font = _fontTemplate.font;
                tmp.fontSharedMaterial = _fontTemplate.fontSharedMaterial;
            }
            tmp.color = color;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false; // 文本不拦点击(前进层统一处理)
            return tmp;
        }

        /// <summary>矩形按钮 chip:Image+Button+BoxButton(按压反馈/音效走框架),文字铺满。</summary>
        RectTransform CreateChip(string name, Color bg, string text, Color textColor, float fontSize,
            float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxButton));
            go.transform.SetParent(_overlay, false);
            go.GetComponent<Image>().color = bg;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None; // 自绘底,不做 Selectable 默认变色
            var label = CreateText("Label", (RectTransform)go.transform, textColor, fontSize);
            label.text = text;
            SetStretch(label.rectTransform, 0f, 0f, 0f, 0f); // 文字铺满 chip
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, height); // 尺寸先在默认锚点下就位,ApplyStep 再摆位
            return rect;
        }
    }
}
