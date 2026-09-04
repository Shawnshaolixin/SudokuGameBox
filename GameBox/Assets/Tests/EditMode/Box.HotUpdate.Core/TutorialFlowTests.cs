using System;
using System.Collections.Generic;
using Box.HotUpdate.Core.Onboarding;
using NUnit.Framework;
using UnityEngine;

namespace Box.HotUpdate.Core.Tests
{
    /// <summary>
    /// TutorialFlow 状态机单测(M3.3 引导通用件):经 CreateOverride 注入无头子类(掩码=null),
    /// 纯逻辑空跑不建任何 UI —— 验证 步骤推进/完成/跳过/离开保留 InProgress 的完整语义。
    /// </summary>
    public class TutorialFlowTests
    {
        const string GameId = "boxcore.tests.flow";

        /// <summary>无头子类:覆写掩码工厂返回 null(生产默认建 TutorialMask,单测不碰 UI)。</summary>
        sealed class HeadlessFlow : TutorialFlow
        {
            public HeadlessFlow(string gameId, IReadOnlyList<TutorialStepDef> steps, string skipKey,
                Action<int> onStepShown, Action<bool> onEnded)
                : base(gameId, steps, skipKey, onStepShown, onEnded) { }

            protected override TutorialMask CreateMask() => null;
        }

        readonly List<int> _shown = new List<int>();
        readonly List<bool> _ended = new List<bool>();

        [SetUp]
        public void SetUp()
        {
            // NUnitLite 同夹具内复用实例,实例字段跨用例累积 → 每例先清空记录
            _shown.Clear();
            _ended.Clear();
            TutorialFlow.CreateOverride = (id, steps, skipKey, shown, ended)
                => new HeadlessFlow(id, steps, skipKey, shown, ended);
        }

        [TearDown]
        public void TearDown()
        {
            TutorialFlow.CreateOverride = null; // 接缝还原,防串用例
            OnboardingStore.Clear(GameId);
        }

        static List<TutorialStepDef> MakeSteps(int n)
        {
            var list = new List<TutorialStepDef>(n);
            for (int i = 0; i < n; i++)
                list.Add(new TutorialStepDef("key" + i, () => default(Rect))); // 无头:目标矩形无所谓
            return list;
        }

        [Test]
        public void Start_置InProgress并展示首步()
        {
            var flow = TutorialFlow.Start(GameId, MakeSteps(3), "skip", _shown.Add, _ended.Add);
            Assert.IsNotNull(flow);
            Assert.IsTrue(flow.IsActive);
            Assert.AreEqual(0, flow.StepIndex);
            Assert.AreEqual(3, flow.StepCount);
            Assert.AreEqual(OnboardingStatus.InProgress, OnboardingStore.Get(GameId));
            CollectionAssert.AreEqual(new[] { 0 }, _shown, "启动即回调首步展示");
            Assert.IsEmpty(_ended);
        }

        [Test]
        public void Advance_逐步骤进_末步完成()
        {
            var flow = TutorialFlow.Start(GameId, MakeSteps(3), "skip", _shown.Add, _ended.Add);
            flow.Advance();
            Assert.IsTrue(flow.IsActive);
            Assert.AreEqual(1, flow.StepIndex, "第 1 次前进到步骤 2");
            flow.Advance();
            Assert.AreEqual(2, flow.StepIndex);
            flow.Advance(); // 末步前进 = 整段完成
            Assert.IsFalse(flow.IsActive);
            Assert.AreEqual(OnboardingStatus.Done, OnboardingStore.Get(GameId));
            CollectionAssert.AreEqual(new[] { true }, _ended);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, _shown);
        }

        [Test]
        public void Finish_提前完成_置Done并回调true()
        {
            var flow = TutorialFlow.Start(GameId, MakeSteps(2), "skip", _shown.Add, _ended.Add);
            flow.Finish(); // 玩法语义:引导局被直接解关 → 提前收尾
            Assert.IsFalse(flow.IsActive);
            Assert.AreEqual(OnboardingStatus.Done, OnboardingStore.Get(GameId));
            CollectionAssert.AreEqual(new[] { true }, _ended);
        }

        [Test]
        public void Skip_置Skipped并回调false()
        {
            var flow = TutorialFlow.Start(GameId, MakeSteps(2), "skip", _shown.Add, _ended.Add);
            flow.Skip(); // 玩家点「跳过」
            Assert.IsFalse(flow.IsActive);
            Assert.AreEqual(OnboardingStatus.Skipped, OnboardingStore.Get(GameId));
            CollectionAssert.AreEqual(new[] { false }, _ended);
        }

        [Test]
        public void Cancel_保留InProgress且不回调()
        {
            var flow = TutorialFlow.Start(GameId, MakeSteps(2), "skip", _shown.Add, _ended.Add);
            flow.Cancel(); // 离开引导局(选关/退模块):不完成不跳过
            Assert.IsFalse(flow.IsActive);
            Assert.AreEqual(OnboardingStatus.InProgress, OnboardingStore.Get(GameId), "中断保留 InProgress,重进续播");
            Assert.IsEmpty(_ended, "Cancel 不触发收尾回调(玩法自己知道离开)");
        }

        [Test]
        public void 结束后所有操作均为空操作()
        {
            var flow = TutorialFlow.Start(GameId, MakeSteps(1), "skip", _shown.Add, _ended.Add);
            flow.Finish();
            int shownCount = _shown.Count;
            flow.Advance(); // 已完成:不允许再推进/重播
            flow.Finish();
            flow.Skip();
            flow.Cancel();
            flow.RefreshTarget();
            Assert.AreEqual(OnboardingStatus.Done, OnboardingStore.Get(GameId), "状态不得被后续空操作改写");
            Assert.AreEqual(shownCount, _shown.Count, "不得重复展示步骤");
            Assert.AreEqual(1, _ended.Count, "收尾回调只应触发一次");
        }

        [Test]
        public void RefreshTarget_激活中重定位不换步()
        {
            var flow = TutorialFlow.Start(GameId, MakeSteps(3), "skip", _shown.Add, _ended.Add);
            flow.RefreshTarget(); // 盘面刷新后重定位孔洞(如聚合对漂移)
            Assert.AreEqual(0, flow.StepIndex, "重定位不得推进步骤");
            CollectionAssert.AreEqual(new[] { 0, 0 }, _shown, "重定位重新展示当前步");
        }

        [Test]
        public void Start_空步骤列表_直接完成并返回null()
        {
            var flow = TutorialFlow.Start(GameId, new List<TutorialStepDef>(), "skip", _shown.Add, _ended.Add);
            Assert.IsNull(flow);
            Assert.AreEqual(OnboardingStatus.Done, OnboardingStore.Get(GameId), "空步骤 = 配置关闭引导:直接置 Done");
            CollectionAssert.AreEqual(new[] { true }, _ended);
            CollectionAssert.IsEmpty(_shown);
        }
    }
}
