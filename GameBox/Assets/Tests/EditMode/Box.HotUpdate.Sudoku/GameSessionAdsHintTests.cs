using NUnit.Framework;

namespace Box.HotUpdate.Sudoku.Tests
{
    /// <summary>
    /// 广告回奖提示模型单测(Phase 7 7-1):
    /// 免费提示(3 次)用尽后,激励视频回奖(GrantAdHint)可继续提示;
    /// 每局广告提示上限 MaxAdsBonusHints=2(防刷,04 文档「提示币耗尽点提示」激励点)。
    /// 纯 C# 逻辑,无引擎依赖。
    /// </summary>
    public class GameSessionAdsHintTests
    {
        FakeClock _clock;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
        }

        /// <summary>8 洞谜题:免费 3 次 + 广告 2 次 = 5 次提示额度,测试全程不触底(留 3 洞未填)。</summary>
        GameSession MakeSession() =>
            new GameSession(TestPuzzles.MakePuzzle(0, 1, 2, 3, 4, 5, 6, 7), _clock);

        [Test]
        public void Initial_AdHintState()
        {
            var s = MakeSession();
            Assert.AreEqual(0, s.AdsBonusHints);
            Assert.IsTrue(s.CanUseHint, "初始应可用免费提示");
            Assert.IsTrue(s.CanRequestAdHint, "初始应可请求广告提示");
        }

        [Test]
        public void FreeHints_Exhausted_ThenAdHint_GrantsMore()
        {
            var s = MakeSession();

            // 免费 3 次提示用尽
            for (int i = 0; i < 3; i++) Assert.IsTrue(s.TryUseHint(), "免费提示第 {0} 次应成功", i + 1);
            Assert.IsFalse(s.CanUseHint, "免费提示用尽后不可再用");
            Assert.IsFalse(s.IsFinished, "8 洞谜题:提示 3 次后仍未完成");
            Assert.IsTrue(s.CanRequestAdHint);

            // 广告回奖 1 次 → 提示额度恢复
            s.GrantAdHint();
            Assert.AreEqual(1, s.AdsBonusHints);
            Assert.IsTrue(s.CanUseHint, "广告回奖后应恢复可用");

            Assert.IsTrue(s.TryUseHint(), "广告回奖的提示应可使用");
            Assert.AreEqual(4, s.HintsUsed);
        }

        [Test]
        public void AdHint_Bonus_CappedAtTwoPerGame()
        {
            var s = MakeSession();

            s.GrantAdHint();
            s.GrantAdHint();
            Assert.AreEqual(2, s.AdsBonusHints, "每局广告提示上限 2 次");
            Assert.IsFalse(s.CanRequestAdHint, "达到上限后不可再请求");

            s.GrantAdHint(); // 超限调用:应被忽略(上限保护)
            Assert.AreEqual(2, s.AdsBonusHints, "超限回奖应无效果");
        }

        [Test]
        public void AllHints_Exhausted_FiresEvent_AfterAdsBonus()
        {
            var s = MakeSession();
            int exhaustedCount = 0;
            s.HintExhausted += () => exhaustedCount++;

            // 免费 3 次 + 广告 2 次 = 5 次额度全部用尽 → HintExhausted 触发(计入广告回奖)
            s.GrantAdHint();
            s.GrantAdHint();
            for (int i = 0; i < 5; i++) Assert.IsTrue(s.TryUseHint(), "第 {0} 次提示应成功", i + 1);

            Assert.IsFalse(s.CanUseHint);
            Assert.IsFalse(s.CanRequestAdHint);
            Assert.AreEqual(1, exhaustedCount, "额度用尽应触发一次 HintExhausted(按钮置灰)");
        }

        [Test]
        public void AdHint_Request_BlockedAfterFinish()
        {
            var s = new GameSession(TestPuzzles.MakePuzzle(0), _clock); // 1 洞:1 次提示即完成
            s.TryUseHint();

            Assert.IsTrue(s.IsFinished);
            Assert.IsFalse(s.CanRequestAdHint, "局已完成后不可再请求广告提示");
            s.GrantAdHint();
            Assert.AreEqual(0, s.AdsBonusHints, "完成后回奖应被忽略");
        }
    }
}
