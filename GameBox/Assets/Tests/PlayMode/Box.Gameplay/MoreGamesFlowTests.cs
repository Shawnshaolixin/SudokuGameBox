using System.Collections;
using Box.Gameplay;
using Box.HotUpdate.Sudoku; // GameContext(与 GameplaySmokeTests 同款用例隔离复位)
using Box.ModuleFramework;
using Box.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// M1/M3 冒烟(PlayMode):More Games → 水排序 完整入口链(19 文档 WS-20)。
    /// 与 GameplaySmokeTests(数独链路)互补:验「清单动态入口 → 弹窗 → watersort 模块进栈」,
    /// 断言只用 AOT 壳层通用面(UIView 类型名/栈深),不反向引用热更玩法类型(11 §4.4 红线)。
    /// Editor 默认 Use Asset Database 模式跑;「Use Existing Build」模式下同一用例直读真实
    /// catalog + bundle(本地自验打包内容链路的车辆,见 19 文档 §10 M3 验收)。
    /// </summary>
    public class MoreGamesFlowTests
    {
        /// <summary>热更视图类型名(HotViewBinder 反射 AddComponent,壳层不得编译期引用)。</summary>
        const string WaterSortViewTypeName = "WaterSortView";

        [UnityTest]
        public IEnumerator MoreGames_Opens_WaterSort_Module() => UniTask.ToCoroutine(async () =>
        {
            GameContext.Reset();
            await LoadScene("MainMenu");

            var menu = Object.FindFirstObjectByType<MainMenuView>();
            Assert.NotNull(menu, "MainMenuView 场景实例存在");

            // 前置:清单含 watersort(MoreGamesView 渲染 + 进入的前提,依赖 ModuleCatalog.asset 条目)
            var loader = ModuleLoader.Instance;
            Assert.NotNull(loader, "ModuleLoader 已注册");
            var wsEntry = FindEntry(loader, WaterSortModuleId);
            Assert.NotNull(wsEntry, $"模块清单含 {WaterSortModuleId} 条目");
            Assert.True(wsEntry.enabled, $"{WaterSortModuleId} 条目启用");

            // 点 More Games → 弹窗入栈(有 other module 才开弹窗,空则 toast)
            Click(menu.transform, "MoreGamesButton");
            await UniTask.WaitUntil(() => Object.FindFirstObjectByType<MoreGamesView>() != null);
            // 等弹窗入场动画收尾:MoreGamesView.OnShow = ScalePulse 220ms,UIRouter.PushAsync 全程持
            // _transitioning 转场锁(防连点),动画结束前再 Push 会被守卫拒(return null → 模块误判自退)。
            // 模拟真实用户"看动画播完再点"的节奏,而非 GO 一出现就点。
            await UniTask.Delay(400);
            Assert.AreEqual(1, UIService.Instance.Router.StackCount, "More Games 弹窗入栈");

            // 列表渲染:sudoku 被排除(MainModuleId),watersort 是唯一 other module → 只有 Item0
            var popup = Object.FindFirstObjectByType<MoreGamesView>();
            var content = popup.transform.Find("Card/Content");
            Assert.NotNull(content, "弹窗 Content 容器存在");
            Assert.NotNull(content.Find("Item0"), "第一项渲染(水排序)");
            Assert.Null(content.Find("Item1"), "仅一个 other module,sudoku 不重复渲染");

            // 点第一项 → watersort 模块 OnEnter → WaterSortView 入栈(叠在弹窗上)
            var item = content.Find("Item0").GetComponent<Button>();
            Assert.NotNull(item, "Item0 有 Button");
            item.onClick.Invoke();
            await UniTask.WaitUntil(() => FindActiveViewByTypeName(WaterSortViewTypeName) != null);
            Assert.AreEqual(2, UIService.Instance.Router.StackCount, "水排序主视图叠入弹窗之上");

            // 收尾:弹掉主视图(OnHide 收口 ExitAsync)与弹窗,恢复空栈,防模块残留污染后续用例
            await UIService.Instance.Router.PopAsync(); // WaterSortView 自弹 → 模块退出
            await UniTask.WaitUntil(() => UIService.Instance.Router.StackCount == 1);
            await UIService.Instance.Router.PopAsync(); // MoreGames 弹窗
            await UniTask.WaitUntil(() => UIService.Instance.Router.StackCount == 0);
            Assert.Null(Object.FindFirstObjectByType<MoreGamesView>(), "弹窗已出栈销毁");
        });

        // ---- helpers(与 GameplaySmokeTests 同款,保持用例自包含)----

        const string WaterSortModuleId = "watersort";

        /// <summary>按 id 找清单条目(免 Linq,保持仓库风格)。</summary>
        static ModuleEntry FindEntry(ModuleLoader loader, string id)
        {
            if (loader?.Entries == null) return null;
            foreach (var e in loader.Entries)
                if (e != null && e.id == id) return e;
            return null;
        }

        /// <summary>按运行时类型名找活跃视图(热更视图类型壳层不可编译期引用,故用类型名匹配)。</summary>
        static UIView FindActiveViewByTypeName(string typeName)
        {
            var views = Object.FindObjectsByType<UIView>(FindObjectsSortMode.None);
            foreach (var v in views)
                if (v.gameObject.activeInHierarchy && v.GetType().Name == typeName)
                    return v;
            return null;
        }

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
    }
}
