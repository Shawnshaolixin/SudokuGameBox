using Box.Services;
using NUnit.Framework;
using UnityEngine;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// 插屏频控单元测试(M3.2 全局化):键迁移、计数与展示解耦、间隔落盘、参数表生效。
    /// 关键回归守卫:旧键 sudoku.ads.* → 新键 box.ads.* 一次性迁移(升级用户「前 3 局/间隔」判定不得重置)。
    /// 用例只触碰频控 4 个键(TearDown 清理),不污染其它用例的 PlayerPrefs。
    /// </summary>
    public class AdFrequencyTests
    {
        const string NewGames = "box.ads.gamesPlayed";
        const string NewNext = "box.ads.nextInterstitialAllowedAt";
        const string LegacyGames = "sudoku.ads.gamesPlayed";
        const string LegacyNext = "sudoku.ads.nextInterstitialAllowedAt";

        [SetUp]
        [TearDown]
        public void CleanState()
        {
            PlayerPrefs.DeleteKey(NewGames);
            PlayerPrefs.DeleteKey(NewNext);
            PlayerPrefs.DeleteKey(LegacyGames);
            PlayerPrefs.DeleteKey(LegacyNext);
            PlayerPrefs.Save();
            // 还原参数默认表(防用例失败/异常把静态表留在覆写态,污染后序用例)
            AdFrequencySettings.NoInterstitialFirstLevels = 3;
            AdFrequencySettings.MinIntervalSec = 4 * 60;
            AdFrequencySettings.MaxIntervalSec = 6 * 60;
        }

        [Test]
        public void LegacyKeys_MigratedOnce_OnConstruct()
        {
            // 老用户旧键有值:构造(等价升级后首启)应平迁到新键并删除旧键
            PlayerPrefs.SetInt(LegacyGames, 5);
            PlayerPrefs.SetInt(LegacyNext, 123456);
            PlayerPrefs.Save();

            var c = new AdFrequencyController();
            Assert.AreEqual(5, c.GamesPlayed, "旧键计数应迁移到新键(前 3 局保护不重置)");
            Assert.IsFalse(PlayerPrefs.HasKey(LegacyGames), "迁移后旧键应删除(幂等)");
            Assert.IsFalse(PlayerPrefs.HasKey(LegacyNext), "迁移后旧键应删除(幂等)");
        }

        [Test]
        public void NewKeys_Precedence_OverLegacy()
        {
            // 已在新版玩过(新键有值)+ 旧键残留(理论无,防御):新键为准,旧键清除
            PlayerPrefs.SetInt(NewGames, 7);
            PlayerPrefs.SetInt(LegacyGames, 2);
            PlayerPrefs.Save();

            var c = new AdFrequencyController();
            Assert.AreEqual(7, c.GamesPlayed, "新键优先,旧值不得覆盖");
        }

        [Test]
        public void Notify_Counts_EveryCompletion()
        {
            // 计数与展示解耦:过关计数只累计,判定(Canshow)由间隔/前 N 局表决定
            var c = new AdFrequencyController();
            c.NotifyLevelCompleted();
            c.NotifyLevelCompleted();
            Assert.AreEqual(2, c.GamesPlayed);
            Assert.IsFalse(c.CanShowInterstitial(), "前 3 局保护期内不应展示");
            c.NotifyLevelCompleted();
            Assert.IsTrue(c.CanShowInterstitial(), "满前 3 局且无历史间隔 → 允许展示");
        }

        [Test]
        public void OnInterstitialShown_SetsCooldown()
        {
            var c = new AdFrequencyController();
            c.NotifyLevelCompleted(); c.NotifyLevelCompleted(); c.NotifyLevelCompleted();
            Assert.IsTrue(c.CanShowInterstitial(), "前置条件:首次可展示");

            c.OnInterstitialShown();
            Assert.IsFalse(c.CanShowInterstitial(), "展示后应进入 4~6 分钟冷却(NextAllowedAt 在未来)");
        }

        [Test]
        public void SettingsTable_Drives_Frequency()
        {
            // 参数表生效验证(M3.2 配置化:运营覆写静态表即全局生效)。
            // 冷却时间在展示时按当时参数落盘:先覆写为 0 再展示 → 冷却 0 → 判定立即可展示。
            var c = new AdFrequencyController();
            c.NotifyLevelCompleted(); c.NotifyLevelCompleted(); c.NotifyLevelCompleted();
            c.OnInterstitialShown();
            Assert.IsFalse(c.CanShowInterstitial(), "默认间隔(4~6 分钟)落盘后应进入冷却");

            AdFrequencySettings.MinIntervalSec = AdFrequencySettings.MaxIntervalSec = 0;
            c.OnInterstitialShown(); // 覆写后落盘:冷却 = now + 0
            AdFrequencySettings.MinIntervalSec = 4 * 60; // 先还原默认表,防断言失败污染
            AdFrequencySettings.MaxIntervalSec = 6 * 60;
            Assert.IsTrue(c.CanShowInterstitial(), "覆写参数表应即时改变判定");
        }
    }
}
