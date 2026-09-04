using UnityEngine;

namespace Box.HotUpdate.Core.Onboarding
{
    /// <summary>引导状态(见 OnboardingStore):未开始 / 进行中 / 完成 / 跳过。</summary>
    public enum OnboardingStatus
    {
        /// <summary>从未开始:首次进入「引导局」触发引导。</summary>
        Unseen = 0,
        /// <summary>引导中(中途离开保留):重进引导局从头再播,直到完成或跳过。</summary>
        InProgress = 1,
        /// <summary>已学完(走完步骤/引导局过关):永不再播。</summary>
        Done = 2,
        /// <summary>玩家主动跳过:永不再播。</summary>
        Skipped = 3,
    }

    /// <summary>
    /// 引导状态持久化(10 文档 §16.7 9.5-4 OnboardingService 的落点,随 M3.3 下沉为通用服务):
    /// 键 box.onboarding.{gameId},与 box.coins / box.ads.* 同属盒级共享分区
    /// (玩法私有存档在 watersort.* / sudoku.* 分区;引导状态跨玩法由盒统一管,故放 box.*)。
    /// 玩法侧判定时机:进入本玩法「引导局」时查 IsFinished —— 未完成/未跳过才开播引导。
    /// </summary>
    public static class OnboardingStore
    {
        const string Prefix = "box.onboarding.";

        /// <summary>读状态(缺省 = 未开始;新用户首启默认不打扰任何旧逻辑)。</summary>
        public static OnboardingStatus Get(string gameId)
            => (OnboardingStatus)PlayerPrefs.GetInt(Prefix + gameId, (int)OnboardingStatus.Unseen);

        /// <summary>写状态并落盘(box.* 键与玩法存档一致:显式 Save)。</summary>
        public static void Set(string gameId, OnboardingStatus status)
        {
            PlayerPrefs.SetInt(Prefix + gameId, (int)status);
            PlayerPrefs.Save();
        }

        /// <summary>引导是否已终结(完成/跳过 = 不再打扰;测试/调试亦可直接查状态)。</summary>
        public static bool IsFinished(string gameId)
        {
            var s = Get(gameId);
            return s == OnboardingStatus.Done || s == OnboardingStatus.Skipped;
        }

        /// <summary>清状态(EditMode 测试隔离/本地调试用;产品路径无删除需求)。</summary>
        public static void Clear(string gameId)
        {
            PlayerPrefs.DeleteKey(Prefix + gameId);
            PlayerPrefs.Save();
        }
    }
}
