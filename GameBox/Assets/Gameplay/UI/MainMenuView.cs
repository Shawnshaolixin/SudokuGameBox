using Box.ModuleFramework;
using Box.Services;
using Box.UI;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// 主菜单/大厅雏形(Phase 4 4-1 + Phase 4.5 + Phase 5):
    /// 入口按钮 → IModuleLoader.EnterAsync(中间态:玩法模块内部开弹窗/切场景,决策 B);
    /// 设置 → Phase 5 正式设置弹窗(SettingsView,偏好走 ISettingsService)。
    /// 进入玩法前由壳写入 box.lastModuleId(§8.1:box.* 仅 Shell 可写,D-5)。
    /// 不再静态引用玩法类型 —— Box.Gameplay(AOT) 不依赖 HotUpdate.Sudoku,
    /// v1.1 热更下发后大厅零改动接入新玩法(读 ModuleCatalog 渲染入口,网格化留给第二玩法)。
    /// 本地化:订阅 L10n.LanguageChanged,语言切换后标题/按钮文案即时刷新(FR-17)。
    /// </summary>
    public sealed class MainMenuView : UIView
    {
        /// <summary>语言切换订阅标记(防重复订阅;OnDestroy 退订防泄漏)。</summary>
        bool _langSubscribed;

        protected override async void Awake()
        {
            base.Awake();
            await InitSceneRoot(); // 场景直挂视图:自驱动 Create+Show(不走 Router 栈)
        }

        protected override UniTask OnCreate()
        {
            var start = transform.Find("StartButton")?.GetComponent<BoxButton>();
            if (start != null)
                start.OnClick(() => EnterModule("sudoku", null));

            var daily = transform.Find("DailyChallengeButton")?.GetComponent<BoxButton>();
            if (daily != null)
                daily.OnClick(() => EnterModule("sudoku", "daily"));

            var settings = transform.Find("SettingsButton")?.GetComponent<BoxButton>();
            if (settings != null)
                settings.OnClick(() => UIService.Instance?.Router.PushAsync<SettingsView>("UI/Popups/SettingsPopup").Forget());

            // 语言变更全 UI 刷新:订阅一次,OnDestroy 退订
            if (!_langSubscribed)
            {
                L10n.LanguageChanged += OnLanguageChanged;
                _langSubscribed = true;
            }
            ApplyLanguage(); // 打开时同步当前语言文案
            return UniTask.CompletedTask;
        }

        // MonoBehaviour 销毁(场景卸载):退订语言事件,防静态事件泄漏到已销毁对象
        private new void OnDestroy()
        {
            if (_langSubscribed)
            {
                L10n.LanguageChanged -= OnLanguageChanged;
                _langSubscribed = false;
            }
        }

        void OnLanguageChanged() => ApplyLanguage();

        /// <summary>按当前语言刷新本视图所有文案(prefab 初始中文,运行期由 L10n 覆盖)。</summary>
        void ApplyLanguage()
        {
            SetText("Title", L10n.Get("menu.title"));
            SetLabel("StartButton", L10n.Get("menu.start"));
            SetLabel("DailyChallengeButton", L10n.Get("menu.daily"));
            SetLabel("SettingsButton", L10n.Get("menu.settings"));
        }

        void SetLabel(string buttonPath, string text)
        {
            var t = transform.Find(buttonPath + "/Label")?.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }

        void SetText(string path, string text)
        {
            var t = transform.Find(path)?.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }

        /// <summary>进入玩法:壳先写 lastModuleId(仅 Shell 可写 box.*),再交模块加载器。</summary>
        void EnterModule(string moduleId, string args)
        {
            if (ServiceLocator.Save != null)
            {
                ServiceLocator.Save.LastModuleId = moduleId; // 交叉导量恢复用(§8.1)
                ServiceLocator.Save.Save();
            }
            ModuleLoader.Instance?.EnterAsync(moduleId, args).Forget();
        }
    }
}
