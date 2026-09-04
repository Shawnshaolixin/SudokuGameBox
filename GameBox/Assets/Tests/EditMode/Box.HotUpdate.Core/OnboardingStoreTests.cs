using Box.HotUpdate.Core.Onboarding;
using NUnit.Framework;
using UnityEngine;

namespace Box.HotUpdate.Core.Tests
{
    /// <summary>
    /// OnboardingStore 单测(M3.3 引导通用件):box.onboarding.{gameId} 分区读写与终态判定。
    /// 每用例用独立 gameId + TearDown 清理,防 PlayerPrefs 跨用例污染(EditMode 同进程持久)。
    /// </summary>
    public class OnboardingStoreTests
    {
        const string IdA = "boxcore.tests.a";
        const string IdB = "boxcore.tests.b";

        [TearDown]
        public void TearDown()
        {
            OnboardingStore.Clear(IdA);
            OnboardingStore.Clear(IdB);
        }

        [Test]
        public void 缺省状态为未开始()
        {
            Assert.AreEqual(OnboardingStatus.Unseen, OnboardingStore.Get(IdA));
        }

        [Test]
        public void 状态往返读写()
        {
            foreach (var status in new[] { OnboardingStatus.InProgress, OnboardingStatus.Done, OnboardingStatus.Skipped })
            {
                OnboardingStore.Set(IdA, status);
                Assert.AreEqual(status, OnboardingStore.Get(IdA), "状态 {0} 应原样读回", status);
            }
        }

        [Test]
        public void 仅完成与跳过视为已终结()
        {
            Assert.IsFalse(OnboardingStore.IsFinished(IdA)); // Unseen
            OnboardingStore.Set(IdA, OnboardingStatus.InProgress);
            Assert.IsFalse(OnboardingStore.IsFinished(IdA), "引导中不算终结(离开重进要续播)");
            OnboardingStore.Set(IdA, OnboardingStatus.Done);
            Assert.IsTrue(OnboardingStore.IsFinished(IdA));
            OnboardingStore.Set(IdA, OnboardingStatus.Skipped);
            Assert.IsTrue(OnboardingStore.IsFinished(IdA));
        }

        [Test]
        public void 不同玩法分区互不干扰()
        {
            OnboardingStore.Set(IdA, OnboardingStatus.Done);
            Assert.AreEqual(OnboardingStatus.Unseen, OnboardingStore.Get(IdB), "另一玩法键必须仍为未开始");
            OnboardingStore.Clear(IdA);
            Assert.AreEqual(OnboardingStatus.Unseen, OnboardingStore.Get(IdA), "清除后回未开始");
        }
    }
}
