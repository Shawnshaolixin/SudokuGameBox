using Box.ModuleFramework;
using Box.Services;
using NUnit.Framework;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// More Games 弹窗列表测试(13 文档 §5 落地,大厅动态化):
    /// CollectEntries 是弹窗列表的唯一数据来源 —— 过滤 enabled、忽略空 id、sortOrder 升序,
    /// 确保「新增玩法只需清单加 1 条」的闭环在数据层先立住;
    /// 顺带断言 MoreGames 相关 L10n key 已就位(菜单按钮/弹窗标题/关闭按钮)。
    /// 纯静态方法,无实例化,EditMode 直接测(不引第三方库)。
    /// </summary>
    public class MoreGamesListTests
    {
        static ModuleEntry Entry(string id, bool enabled, int sortOrder)
        {
            return new ModuleEntry
            {
                id = id,
                entryType = "Box.HotUpdate.TestModule",
                displayName = id,
                enabled = enabled,
                sortOrder = sortOrder,
            };
        }

        [Test]
        public void CollectEntries_NullInput_ReturnsEmpty()
        {
            var result = MoreGamesView.CollectEntries(null);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void CollectEntries_FiltersDisabledAndEmptyId()
        {
            var entries = new[]
            {
                Entry("a", true, 1),
                Entry("b", false, 2), // disabled → 剔除
                Entry("", true, 3),   // 空 id → 剔除
                Entry("c", true, 4),
            };
            var result = MoreGamesView.CollectEntries(entries);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("a", result[0].id);
            Assert.AreEqual("c", result[1].id);
        }

        [Test]
        public void CollectEntries_SortsBySortOrderAscending()
        {
            var entries = new[]
            {
                Entry("c", true, 30),
                Entry("a", true, 10),
                Entry("b", true, 20),
            };
            var result = MoreGamesView.CollectEntries(entries);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("a", result[0].id);
            Assert.AreEqual("b", result[1].id);
            Assert.AreEqual("c", result[2].id);
        }

        [Test]
        public void CollectEntries_PreservesEnabledDefault()
        {
            // enabled 缺省为 true(ModuleEntry 字段默认值),不应被误过滤
            var entries = new[] { new ModuleEntry { id = "x", entryType = "T", sortOrder = 0 } };
            var result = MoreGamesView.CollectEntries(entries);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("x", result[0].id);
        }

        [Test]
        public void MoreGames_L10nKeys_Registered()
        {
            L10n.Init("zh");
            Assert.AreEqual("更多游戏", L10n.Get("menu.moreGames"));
            Assert.AreEqual("更多游戏", L10n.Get("moreGames.title"));
            Assert.AreEqual("完成", L10n.Get("moreGames.close"));

            L10n.Init("en");
            Assert.AreEqual("More Games", L10n.Get("menu.moreGames"));
            Assert.AreEqual("More Games", L10n.Get("moreGames.title"));
            Assert.AreEqual("Done", L10n.Get("moreGames.close"));
        }
    }
}