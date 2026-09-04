using System;
using UnityEngine;
using UnityEngine.UI;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 试管架(运行时代码绘制,M1.3 占位美术):
    /// 按会话盘面在 TubeArea 内重建试管列 —— 玻璃底色 Image + 底部向上分层液块 Image,
    /// 占位阶段无贴图/无圆角,表现后置 M3 走 AIGC 管线替换(玩法逻辑不受影响)。
    /// 交互:点击试管选中(高亮),再点另一支 = 请求倒水(由视图经会话判定合法性);
    /// 点自己取消选中。组件运行期 AddComponent 到 TubeArea(不走 prefab 序列化,20 文档 §4 纪律同源)。
    /// 重建即清空重画(盘面 ≤12 管 × ≤4 层,一次性 UI 对象开销可忽略)。
    /// </summary>
    public sealed class WaterSortTubeRack : MonoBehaviour
    {
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

            var board = _session.Board;
            int n = board.TubeCount;
            // 尺寸自适应:管宽随管数收窄,高度按宽高比 4.6(经典细长试管),整体在容器内居中
            float w = Mathf.Min(110f, (_self.rect.width - 80f - (n - 1) * 12f) / n);
            float h = Mathf.Min(w * 4.6f, _self.rect.height - 60f);
            float startX = -((n - 1) * (w + 12f)) * 0.5f;

            for (int t = 0; t < n; t++)
            {
                int drops = board.TopCount(t);
                var col = BuildTube(t, w, h);
                var rt = (RectTransform)col.transform;
                rt.anchoredPosition = new Vector2(startX + t * (w + 12f), 0);
                // 玻璃底 + 液块(颜色按 1..drops 自底向上;空管无液块)
                var glass = col.GetComponent<Image>();
                glass.color = t == _selected
                    ? new Color(0.45f, 0.55f, 0.70f, 0.9f)  // 选中高亮(占位:提亮杯体)
                    : new Color(1f, 1f, 1f, 0.10f);
                float innerW = w * 0.68f, innerH = h * 0.86f;
                const float dropGap = 3f;
                float dh = (innerH - (drops - 1) * dropGap) / Mathf.Max(4, drops);
                for (int i = 0; i < drops; i++)
                {
                    int color = board.Get(t, i); // 0=底
                    var drop = NewChild(rt, "Drop", innerW, dh, Palette[color - 1]);
                    // 液块中心:自容器底部向上排(容器高 h,液区底 = -(h/2 - (h-innerH)/2))
                    float bottomY = -(h * 0.5f - (h - innerH) * 0.5f);
                    ((RectTransform)drop.transform).anchoredPosition =
                        new Vector2(0, bottomY + dh * 0.5f + i * (dh + dropGap));
                }
            }
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
            var go = NewChild(_self, "Tube" + index, w, h, Color.white); // 自带杯体 Image(底色由 Refresh 上色)
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>(); // 纯 Unity Button 承接点击;免过渡闪烁
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            var idx = index;
            btn.onClick.AddListener(() => OnTubeTap(idx));
            return go;
        }

        GameObject NewChild(RectTransform parent, string name, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return go;
        }

        void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }
    }
}
