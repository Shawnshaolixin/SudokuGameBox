using System;
using Box.ModuleFramework;
using Box.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 数独玩法模块入口(11 文档 §3.1 ModuleManifest.entryType,清单 id="sudoku"):
    /// 中间态(决策 B):OnEnter = 参数化入口 —— args="daily" 直接开每日挑战,
    /// 否则进难度弹窗;场景切换由模块内部负责(单场景收敛推迟到 Phase 6)。
    /// OnExit = 回大厅。
    /// v1.0 随包 AOT 编译,无静态引用,入口类型经 link.xml 保留防 IL2CPP 裁剪。
    /// 埋点走 §8.4 契约 {module_id}.{action}(sudoku.*),由玩法内视图上报。
    /// </summary>
    public sealed class SudokuModule : IGameModule
    {
        public string Id => "sudoku";

        public UniTask OnEnter(ModuleContext ctx)
        {
            if (ctx.Args as string == "daily")
            {
                // 每日挑战:种子在模块内计算(大厅不再持有玩法状态,GameContext 收敛)
                GameContext.SetDaily(DailyChallengeStore.SeedFor(DateTime.UtcNow));
                SceneManager.LoadSceneAsync("Gameplay");
            }
            else
            {
                GameContext.Reset();
                UIService.Instance?.Router.PushAsync<DifficultySelectView>("UI/Popups/DifficultySelect").Forget();
            }
            return UniTask.CompletedTask;
        }

        public UniTask OnExit()
        {
            SceneManager.LoadSceneAsync("MainMenu");
            return UniTask.CompletedTask;
        }
    }
}
