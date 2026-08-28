using System.Collections.Generic;
using Box.ModuleFramework;
using Box.Services;
using Box.UI;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// More Games 弹窗(大厅动态化,13 文档 §5 落地):
    /// 读 ModuleLoader.Entries(enabled == true,按 sortOrder 排序)动态渲染全部玩法入口,
    /// 每项点击 → 写 box.lastModuleId + EnterAsync(entry.id)——新增玩法只需在清单加 1 条,
    /// 大厅与弹窗零改动,组件化闭环(§5 目标)。
    /// 入口列表每次 OnCreate/语言刷新按清单重建,不做静态硬编码(与 MainMenuView 的 sudoku 按钮互补:
    /// 弹窗覆盖"更多玩法",主菜单保留两个直达按钮,后期由配置决定展示)。
    /// prefab: UI/Prefabs/Popups/MoreGamesPopup(MoreGamesPopupSetup 生成):Title + Content 容器
    /// + ItemTemplate(隐藏模板,运行时克隆)+ CloseButton,按钮文案子节点约定 "Label"。
    /// 只依赖 ModuleFramework/Services/UI(AOT 壳),不引用任何玩法类型。
    /// </summary>
    public sealed class MoreGamesView : UIView
    {
        /// <summary>语言切换订阅标记(防重复订阅;OnDestroy 退订防泄漏,照 MainMenuView)。</summary>
        bool _langSubscribed;

        protected override void Awake()
        {
            Layer = UILayer.Popup; // 返回键可关
            base.Awake();
        }

        protected override UniTask OnCreate()
        {
            Rebuild();

            var close = FindInCard("CloseButton")?.GetComponent<BoxButton>();
            if (close != null)
                close.OnClick(() => Close().Forget());

            // 语言变更刷新标题与列表(弹窗内不常驻,但语言切换后重开需最新文案)
            if (!_langSubscribed)
            {
                L10n.LanguageChanged += OnLanguageChanged;
                _langSubscribed = true;
            }
            ApplyLanguage();
            return UniTask.CompletedTask;
        }

        protected override async UniTask OnShow(object args)
        {
            // 弹入(D-15):缩放卡片,不缩全屏遮罩(防脉冲期间遮罩露出屏幕边缘接缝)
            var card = transform.Find("Card");
            await BoxTween.ScalePulse(card != null ? card : transform, 0.8f, 1f, 0.22f);
        }

        // MonoBehaviour 销毁:退订语言事件,防静态事件泄漏到已销毁对象
        private new void OnDestroy()
        {
            if (_langSubscribed)
            {
                L10n.LanguageChanged -= OnLanguageChanged;
                _langSubscribed = false;
            }
        }

        void OnLanguageChanged()
        {
            ApplyLanguage();
            Rebuild(); // 列表项文案随语言刷新(displayName 缺键回退原文案)
        }

        /// <summary>按当前语言刷新标题/关闭按钮(列表由 Rebuild 重建)。</summary>
        void ApplyLanguage()
        {
            var title = FindInCard("Title")?.GetComponent<TextMeshProUGUI>();
            if (title != null) title.text = L10n.Get("moreGames.title");
            var close = FindInCard("CloseButton/Label")?.GetComponent<TextMeshProUGUI>();
            if (close != null) close.text = L10n.Get("moreGames.close");
        }

        void Rebuild()
        {
            RenderItems(CollectEntries(ModuleLoader.Instance?.Entries));
        }

        /// <summary>过滤启用 + 非空 id + sortOrder 升序(纯静态,EditMode 单测直接测)。</summary>
        public static List<ModuleEntry> CollectEntries(IReadOnlyList<ModuleEntry> entries)
        {
            var list = new List<ModuleEntry>();
            if (entries == null) return list;
            foreach (var e in entries)
                if (e != null && e.enabled && !string.IsNullOrEmpty(e.id))
                    list.Add(e);
            list.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return list;
        }

        /// <summary>克隆 ItemTemplate 渲染列表(先清旧项,缓存复用/语言刷新安全)。</summary>
        void RenderItems(List<ModuleEntry> entries)
        {
            var content = FindInCard("Content");
            var template = content?.Find("ItemTemplate");
            if (content == null || template == null) return;

            // 清除上次渲染(模板保留:排除 ItemTemplate)
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                if (child != template && child.name.StartsWith("Item"))
                    Destroy(child.gameObject);
            }

            const float step = 108f; // 项高 96 + 间距 12(与 ItemTemplate 一致)
            const float top = 222f;  // 第一项贴容器顶部(容器高 540:半高 270 - 项半高 48)
            for (int i = 0; i < entries.Count && i < 5; i++) // 容器高 540 最多 5 项,超限走滚动/分页(v1.1 预留)
            {
                var item = Instantiate(template.gameObject, content);
                item.name = "Item" + i;
                item.SetActive(true);
                var itemRt = (RectTransform)item.transform;
                itemRt.anchoredPosition = new Vector2(0, top - i * step);

                var label = item.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (label != null) label.text = L10n.Get(entries[i].displayName); // 缺键回退原文案

                var btn = item.GetComponent<BoxButton>();
                var entry = entries[i];
                if (btn != null)
                    btn.OnClick(() => EnterModule(entry.id, null));
            }
        }

        async UniTask Close()
        {
            var svc = UIService.Instance;
            if (svc != null) await svc.Router.PopAsync(); // 返回键/完成按钮同路径
        }

        /// <summary>进入玩法:壳先写 lastModuleId(与 MainMenuView 同逻辑,交叉导量恢复用 §8.1)。</summary>
        void EnterModule(string moduleId, string args)
        {
            if (ServiceLocator.Save != null)
            {
                ServiceLocator.Save.LastModuleId = moduleId; // 交叉导量恢复用
                ServiceLocator.Save.Save();
            }
            ModuleLoader.Instance?.EnterAsync(moduleId, args).Forget();
        }
    }
}