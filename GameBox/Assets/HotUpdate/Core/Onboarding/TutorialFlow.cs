using System;
using System.Collections.Generic;
using Box.Services;
using UnityEngine;

namespace Box.HotUpdate.Core.Onboarding
{
    /// <summary>
    /// 引导单步定义(代码配置形态,通用件不下沉为 SO 资产——热更组件不进 prefab 序列化,20 文档 §4;
    /// 步骤文案/目标天然随玩法版本走,无需运营热配,故以 C# 定义最贴纪律)。
    /// 目标以「屏幕矩形」表达(WorldCorners → 屏幕坐标):TutorialMask 的独立 Canvas 与玩法各层
    /// Canvas 缩放体系不同,统一经屏幕像素中转,任意目标控件(试管列/按钮/任意包围盒)都可定位。
    /// </summary>
    public sealed class TutorialStepDef
    {
        /// <summary>引导文案 key(L10n;掩码每次展示时实时取,语言切换即时生效)。</summary>
        public string CopyKey { get; }

        /// <summary>目标屏幕矩形解析(每步展示/盘面刷新后重定位时调用;解析失败返回空矩形 = 不挖洞)。</summary>
        public Func<Rect> TargetScreenRect { get; }

        public TutorialStepDef(string copyKey, Func<Rect> targetScreenRect)
        {
            CopyKey = copyKey;
            TargetScreenRect = targetScreenRect;
        }
    }

    /// <summary>
    /// 引导流程控制器(通用件,WS-14 / 10 文档 §16.7 9.5-3 形态;9.5 数独引导直接复用):
    /// 玩法侧持有步骤定义,在对局事件里驱动 Advance/Finish/Skip,Cancel 用于「离开引导局」。
    /// 职责 = 步骤推进状态机 + 状态持久化(OnboardingStore)+ 掩码展示生命周期;
    /// 埋点由玩法侧回调完成(本类零分析依赖)。
    ///
    /// 状态语义:
    ///   Start      → OnboardingStore 置 InProgress,展示第 1 步(onStepShown(0))
    ///   Advance()  → 完成当前步;末步 Advance 等价 Finish(走完即完成)
    ///   Finish()   → 提前完成(引导局解关 / 末步演示完成)→ 置 Done
    ///   Skip()     → 玩家点「跳过」→ 置 Skipped
    ///   Cancel()   → 离开引导局(面板切换/退模块):摘掩码,状态保留 InProgress(下次重进再播)
    ///   收尾统一回调 onEnded(finished):true=完成 / false=跳过;Cancel 不触发(玩法自己知道离开)。
    ///
    /// 掩码创建走 CreateMask() 虚方法(单测继承覆写为 null 即可空跑纯状态机,不碰 UI)。
    /// </summary>
    public class TutorialFlow
    {
        /// <summary>引导是否在场(Start 后 true;Finish/Skip/Cancel 后 false)。</summary>
        public bool IsActive { get; private set; }

        /// <summary>当前步 0-based(埋点 step_index 用 1-based = +1;玩法分支判定亦按此值)。</summary>
        public int StepIndex { get; private set; }

        /// <summary>总步数(Start 后恒定;玩法可按「引导步数走配置」先截断再传)。</summary>
        public int StepCount { get; }

        readonly string _gameId;
        readonly string _skipKey;
        readonly IReadOnlyList<TutorialStepDef> _steps;
        readonly Action<int> _onStepShown;
        readonly Action<bool> _onEnded;
        TutorialMask _mask;

        // 无头单测接缝:测试程序集注入返回「覆写 CreateMask=null」子类的构造器(纯状态机,不建 UI);
        // 产品路径恒 null → Start 直构本类。internal + InternalsVisibleTo 见 AssemblyInfo.cs
        internal static Func<string, IReadOnlyList<TutorialStepDef>, string, Action<int>, Action<bool>, TutorialFlow> CreateOverride;

        protected TutorialFlow(string gameId, IReadOnlyList<TutorialStepDef> steps, string skipKey,
            Action<int> onStepShown, Action<bool> onEnded)
        {
            _gameId = gameId;
            _steps = steps;
            _skipKey = skipKey;
            _onStepShown = onStepShown;
            _onEnded = onEnded;
            StepCount = steps.Count;
        }

        /// <summary>
        /// 开启一段引导:置 InProgress 并展示第 1 步。
        /// steps 为空 = 无步可播(玩法把配置截到 0):直接视为完成返回 null,调用方无需再持有。
        /// </summary>
        public static TutorialFlow Start(string gameId, IReadOnlyList<TutorialStepDef> steps, string skipKey,
            Action<int> onStepShown, Action<bool> onEnded)
        {
            if (steps == null || steps.Count == 0)
            {
                OnboardingStore.Set(gameId, OnboardingStatus.Done);
                onEnded?.Invoke(true);
                return null;
            }
            var flow = CreateOverride?.Invoke(gameId, steps, skipKey, onStepShown, onEnded)
                       ?? new TutorialFlow(gameId, steps, skipKey, onStepShown, onEnded);
            flow.IsActive = true;
            OnboardingStore.Set(gameId, OnboardingStatus.InProgress);
            flow._mask = flow.CreateMask(); // 子类可覆写为空(单测跑纯状态机)
            flow.ShowStep();
            return flow;
        }

        /// <summary>完成当前步(末步 = 整段完成)。玩法在对局事件满足时调用(如首次成功倒水)。</summary>
        public void Advance()
        {
            if (!IsActive) return;
            if (StepIndex + 1 >= _steps.Count) { Finish(); return; }
            StepIndex++;
            ShowStep();
        }

        /// <summary>提前完成(引导局解关 / 末步演示完成):置 Done 收尾。</summary>
        public void Finish()
        {
            if (!IsActive) return;
            IsActive = false;
            OnboardingStore.Set(_gameId, OnboardingStatus.Done);
            TearDown();
            _onEnded?.Invoke(true);
        }

        /// <summary>玩家跳过:置 Skipped 收尾(此后不再打扰)。</summary>
        public void Skip()
        {
            if (!IsActive) return;
            IsActive = false;
            OnboardingStore.Set(_gameId, OnboardingStatus.Skipped);
            TearDown();
            _onEnded?.Invoke(false);
        }

        /// <summary>
        /// 离开引导局(面板切走/退模块):摘掩码但状态保留 InProgress —— 玩家既没学完也没跳,
        /// 下次再进引导局从头再播(引导不强闯,但也不悄悄丢掉)。
        /// </summary>
        public void Cancel()
        {
            if (!IsActive) return;
            IsActive = false;
            TearDown();
        }

        /// <summary>当前步目标变化(如盘面刷新后步骤高亮的聚合对移位):重定位孔洞,不换步。</summary>
        public void RefreshTarget()
        {
            if (!IsActive) return;
            ShowStep();
        }

        // ---- 掩码生命周期 ----

        /// <summary>默认创建真实掩码(跳过钮已接到本流程);单测/预览环境覆写返回 null 空跑状态机。</summary>
        protected virtual TutorialMask CreateMask()
            => TutorialMask.Create("Tutorial_" + _gameId, L10n.Get(_skipKey), Skip);

        void ShowStep()
        {
            var def = _steps[StepIndex];
            _onStepShown?.Invoke(StepIndex);
            if (_mask == null) return; // headless:状态机照常,仅无视觉
            _mask.ShowStep(def.TargetScreenRect?.Invoke() ?? default, L10n.Get(def.CopyKey));
        }

        void TearDown()
        {
            _mask?.Close();
            _mask = null;
        }
    }
}
