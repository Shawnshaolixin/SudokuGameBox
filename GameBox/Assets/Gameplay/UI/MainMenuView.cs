using System;
using Box.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Box.Gameplay
{
    /// <summary>
    /// 主菜单(Phase 4 4-1):开始游戏→难度弹窗;每日挑战→日期种子直接开局;设置→Phase 5 占位。
    /// </summary>
    public sealed class MainMenuView : UIView
    {
        protected override async void Awake()
        {
            base.Awake();
            await InitSceneRoot(); // 场景直挂视图:自驱动 Create+Show(不走 Router 栈)
        }

        protected override UniTask OnCreate()
        {
            var svc = UIService.Instance;
            var start = transform.Find("StartButton")?.GetComponent<BoxButton>();
            if (start != null && svc != null)
                start.OnClick(() => svc.Router.PushAsync<DifficultySelectView>("UI/Popups/DifficultySelect").Forget());

            var daily = transform.Find("DailyChallengeButton")?.GetComponent<BoxButton>();
            if (daily != null)
                daily.OnClick(() =>
                {
                    GameContext.SetDaily(DailyChallengeStore.SeedFor(DateTime.UtcNow));
                    SceneManager.LoadSceneAsync("Gameplay");
                });

            var settings = transform.Find("SettingsButton")?.GetComponent<BoxButton>();
            if (settings != null)
                settings.OnClick(() => Debug.Log("[MainMenu] Settings - Phase 5 占位"));
            return UniTask.CompletedTask;
        }
    }
}
