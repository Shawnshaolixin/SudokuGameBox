using System.Collections;
using System.Reflection;
using Box.Gameplay;
using Box.HotUpdate.Sudoku;
using Box.ModuleFramework;
using Box.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sudoku.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// Phase 4 冒烟(PlayMode):场景直挂视图的真实生命周期(prefab→代码接线)、
    /// 主菜单→难度→对局→结算全链路、返回键语义。核心逻辑由 EditMode 单测覆盖,此处只验集成。
    /// </summary>
    public class GameplaySmokeTests
    {
        [UnityTest]
        public IEnumerator MainMenu_Start_To_Gameplay_Flow() => UniTask.ToCoroutine(async () =>
        {
            GameContext.Reset();
            await LoadScene("MainMenu");

            var menu = Object.FindFirstObjectByType<MainMenuView>();
            Assert.NotNull(menu, "MainMenuView 场景实例存在");
            Assert.NotNull(UIService.Instance, "AppBootstrap 已注册 UIService");

            // 点 Start → DifficultySelect 弹窗入栈
            Click(menu.transform, "StartButton");
            await UniTask.WaitUntil(() => Object.FindFirstObjectByType<DifficultySelectView>() != null);
            Assert.AreEqual(1, UIService.Instance.Router.StackCount, "难度弹窗入栈");

            // 点 Easy → 关弹窗 → 切 Gameplay
            var popup = Object.FindFirstObjectByType<DifficultySelectView>();
            Click(popup.transform, "EasyButton");
            await UniTask.WaitUntil(() => SceneManager.GetActiveScene().name == "Gameplay");
            await UniTask.DelayFrame(2);

            var view = Object.FindFirstObjectByType<GameplayView>();
            Assert.NotNull(view, "GameplayView 场景实例存在");
            Assert.AreEqual(Difficulty.Easy, GameContext.Difficulty, "难度参数传递正确");
            Assert.NotNull(UIService.Instance.CustomBackHandler, "返回键 handler 已注册(OnShow)");

            // 棋盘 81 格 + 按钮接线
            var board = view.transform.Find("BoardPlaceholder");
            Assert.NotNull(board, "BoardPlaceholder 存在");
            int boxes = 0, cells = 0;
            for (int b = 0; b < 9; b++)
            {
                var box = board.Find("Box" + b);
                if (box == null) continue;
                boxes++;
                // 格命名是全局行优先 index("C"+0..80,宫 0 含 C0/C1/C2 等,见 GameplayView
                // BuildBoardCells 索引换算),旧 box.Find("C"+b*9+k) 映射已错位 → 按结构数
                for (int k = 0; k < 9; k++)
                    if (box.GetChild(k) != null) cells++;
            }
            Assert.AreEqual(9, boxes, "9 个宫生成");
            Assert.AreEqual(81, cells, "81 格生成");
            // 数字盘在 NumberPanel 容器内(与 GameplayView 绑定路径一致,见 ClickNum 注释)
            for (int i = 1; i <= 9; i++)
                Assert.NotNull(view.transform.Find("NumberPanel/Num" + i), $"数字盘 Num{i}");
            foreach (var name in new[] { "ModeButton", "UndoButton", "RedoButton", "EraseButton", "HintButton" })
                Assert.NotNull(view.transform.Find(name), $"工具按钮 {name}");

            // 输入链路:选第一个可编辑格 → 填 solution 值 → 可撤销;返回键栈空时消费为 Undo
            var session = GetSession(view);
            int target = -1;
            for (int i = 0; i < 81 && target < 0; i++)
                if (!session.IsGiven(i)) target = i;
            Assert.GreaterOrEqual(target, 0, "存在可编辑格");
            ClickCell(view, target);
            ClickNum(view, session.Solution[target]);
            await UniTask.DelayFrame(1);
            Assert.True(session.CanUndo, "输入后可撤销");
            bool consumed = await UIService.Instance.CustomBackHandler();
            Assert.True(consumed, "栈空时返回键由对局消费(Undo)");
            Assert.False(session.CanUndo, "Undo 已生效");
        });

        [UnityTest]
        public IEnumerator Completion_Shows_Settlement_And_Next_Restarts() => UniTask.ToCoroutine(async () =>
        {
            GameContext.SetNormalGame(Difficulty.Easy);
            await LoadScene("Gameplay");

            var view = Object.FindFirstObjectByType<GameplayView>();
            Assert.NotNull(view, "GameplayView 场景实例存在");

            var session = GetSession(view);
            for (int i = 0; i < 81; i++) // 用 solution 快速完成(逻辑 EditMode 已测,此处验集成链路)
            {
                session.SelectCell(i);
                session.InputNumber(session.Solution[i]);
            }

            await UniTask.WaitUntil(() => Object.FindFirstObjectByType<SettlementPopupView>() != null);
            Assert.True(session.IsFinished, "填满后完成");
            Assert.AreEqual(3, session.StarRating, "无错误三星");

            // 点 Confirm(Next) → 弹窗关 → 同难度新局
            var popup = Object.FindFirstObjectByType<SettlementPopupView>();
            Click(popup.transform, "Confirm");
            await UniTask.WaitUntil(() => Object.FindFirstObjectByType<SettlementPopupView>() == null);
            Assert.AreEqual(0, UIService.Instance.Router.StackCount, "结算弹窗已出栈");
            var fresh = GetSession(view);
            Assert.False(fresh.IsFinished, "Next 后新对局开始");
        });

        [UnityTest]
        public IEnumerator BackKey_Pops_Popup_When_Open() => UniTask.ToCoroutine(async () =>
        {
            GameContext.SetNormalGame(Difficulty.Easy);
            await LoadScene("Gameplay");
            var view = Object.FindFirstObjectByType<GameplayView>();
            Assert.NotNull(view, "GameplayView 场景实例存在");

            // 弹窗打开:返回键交还路由关闭弹窗(handler 返回 false)
            var popup = await UIService.Instance.Router.PushAsync<DifficultySelectView>("Sudoku/Prefabs/DifficultySelect");
            Assert.NotNull(popup, "难度弹窗可推入");
            Assert.AreEqual(1, UIService.Instance.Router.StackCount);
            bool consumed = await UIService.Instance.CustomBackHandler();
            Assert.False(consumed, "弹窗打开时 handler 交还路由");

            await UIService.Instance.HandleBackAsync();
            await UniTask.WaitUntil(() => Object.FindFirstObjectByType<DifficultySelectView>() == null);
            Assert.AreEqual(0, UIService.Instance.Router.StackCount, "返回键关闭弹窗");
        });

        [UnityTest]
        public IEnumerator Startup_Under_Timeout_With_HotUpdate_Degrade() => UniTask.ToCoroutine(async () =>
        {
            // 验证②(10 文档 §16.4):Editor 下 HybridCLR.Runtime 存在 → 热更链路走本地降级,
            // 启动(场景加载 + AppBootstrap 同步注册 + 热更 Begin fire-and-forget)不得被阻塞。
            GameContext.Reset();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await LoadScene("MainMenu");
            sw.Stop();
            Assert.NotNull(UIService.Instance, "AppBootstrap 已注册 UIService");
            Assert.NotNull(ModuleLoader.Instance, "ModuleLoader 已注册");
            Assert.Less(sw.ElapsedMilliseconds, 2500, "启动(场景加载+服务注册)应 ≤2.5s,热更链路不得阻塞");
        });

        // ---- helpers ----

        static async UniTask LoadScene(string name)
        {
            var op = SceneManager.LoadSceneAsync(name);
            await UniTask.WaitUntil(() => op.isDone);
            await UniTask.DelayFrame(2); // 场景根视图 Awake→Create→Show 异步链
        }

        static void Click(Transform root, string path)
        {
            // 弹窗改造(2026-08):内容在 Card 子节点,先查 Card/路径,未迁移 prefab 回退根直查
            var go = root.Find("Card/" + path) ?? root.Find(path);
            Assert.NotNull(go, $"节点 {path} 存在");
            var btn = go.GetComponent<Button>();
            Assert.NotNull(btn, $"{path} 有 Button");
            btn.onClick.Invoke(); // BoxButton.Awake 已挂 Fire → 回调链
        }

        static void ClickCell(GameplayView view, int index)
        {
            var cell = view.transform.Find($"BoardPlaceholder/Box{index / 9}/C{index}");
            Assert.NotNull(cell, $"格子 C{index}");
            cell.GetComponent<Button>().onClick.Invoke();
        }

        static void ClickNum(GameplayView view, int n)
        {
            // 数字按钮在 NumberPanel 内(HLG 排布),与 GameplayView 绑定路径保持一致
            var btn = view.transform.Find("NumberPanel/Num" + n);
            Assert.NotNull(btn, $"数字 {n}");
            btn.GetComponent<Button>().onClick.Invoke();
        }

        static GameSession GetSession(GameplayView view)
        {
            var f = typeof(GameplayView).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f, "_session 字段存在");
            return (GameSession)f.GetValue(view);
        }
    }
}
