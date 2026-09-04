using System;
using System.Collections.Generic;

namespace Box.Services
{
    /// <summary>
    /// 轻量本地化表(FR-17 首期 zh/en;不引第三方库,红线 2)。
    /// 用法:视图在 OnCreate/OnShow 订阅 L10n.LanguageChanged 刷新文案,OnHide/OnDestroy 退订。
    /// SetLanguage:同时写 ISettingsService.Language(PlayerPrefs 持久化)并广播事件 → 全局 UI 即时切换。
    /// 缺键回退:en 缺 → zh → 原始 key(测试/新键不炸)。
    /// 文本含 {0}/{1} 占位符,用 Format(key, args) 取。
    /// 热更侧(Box.HotUpdate.Sudoku)可引本类(纯 C#,无 UnityEngine 依赖)。
    /// v1.0 语言固定英文(设置页无切换入口,Bug 清单 2026-08-29);zh 表保留备未来多语言扩展。
    /// </summary>
    public static class L10n
    {
        /// <summary>当前语言代码(zh/en);v1.0 默认 en(英语市场)。</summary>
        public static string Current { get; private set; } = "en";

        /// <summary>语言切换事件(所有打开的视图订阅后即时刷新)。</summary>
        public static event Action LanguageChanged;

        /// <summary>启动同步(静默设置,不广播;AppBootstrap 在视图创建前调用)。</summary>
        public static void Init(string language)
        {
            Current = string.IsNullOrEmpty(language) ? "en" : language;
        }

        /// <summary>
        /// 切换语言:写入偏好(PlayerPrefs 持久化)+ 广播事件。
        /// 设置弹窗/任何代码调用本方法后,所有订阅视图立即刷新。
        /// (v1.0 设置页已移除语言切换入口,方法保留备未来多语言扩展)
        /// </summary>
        public static void SetLanguage(string language)
        {
            var code = string.IsNullOrEmpty(language) ? "en" : language;
            if (code == Current) return; // 无变化不广播
            Current = code;
            if (ServiceLocator.Settings != null) ServiceLocator.Settings.Language = code; // 持久化
            LanguageChanged?.Invoke();
        }

        /// <summary>按当前语言取文案;缺键回退 zh,再缺回退 key 本身。</summary>
        public static string Get(string key)
        {
            if (Current == "en" && _en.TryGetValue(key, out var en)) return en;
            if (_zh.TryGetValue(key, out var zh)) return zh;
            return key;
        }

        /// <summary>取文案并格式化占位符(如 "数独 - {0}")。</summary>
        public static string Format(string key, params object[] args)
        {
            var t = Get(key);
            return args == null || args.Length == 0 ? t : string.Format(t, args);
        }

        // ---- 文本表 ---- //

        static readonly Dictionary<string, string> _zh = new Dictionary<string, string>
        {
            // 主菜单
            { "menu.title", "数独游戏盒" },
            { "menu.start", "开始游戏" },
            { "menu.daily", "每日挑战" },
            { "menu.settings", "设置" },
            { "menu.moreGames", "更多游戏" },

            // More Games 弹窗
            { "moreGames.title", "更多游戏" },
            { "moreGames.close", "完成" },
            { "moreGames.comingSoon", "敬请期待" }, // 无更多游戏时按钮点击提示(2026-08-29 Bug 清单)

            // 设置弹窗
            { "settings.title", "设置" },
            { "settings.soundOn", "音效:开" },
            { "settings.soundOff", "音效:关" },
            { "settings.musicOn", "音乐:开" },
            { "settings.musicOff", "音乐:关" },
            { "settings.themeLight", "主题:浅色" },
            { "settings.themeDark", "主题:深色" },
            { "settings.langZh", "语言:中文" },
            { "settings.langEn", "语言:English" },
            { "settings.done", "完成" },
            { "settings.removeAds", "去广告" },
            { "settings.removeAdsPurchased", "去广告:已购买" },
            { "settings.privacy", "隐私政策" },
            { "iap.notReady", "商店暂不可用,请稍后重试" },

            // 难度选择
            { "diff.title", "选择难度" },
            { "diff.easy", "简单" },
            { "diff.medium", "中等" },
            { "diff.hard", "困难" },

            // 对局视图
            { "game.title.daily", "每日挑战" },
            { "game.title.normal", "数独 - {0}" },
            { "game.diff.easy", "简单" },
            { "game.diff.medium", "中等" },
            { "game.diff.hard", "困难" },
            { "game.mode.input", "数字" },
            { "game.mode.note", "笔记" },
            { "game.undo", "撤销" },
            { "game.redo", "重做" },
            { "game.erase", "擦除" },
            { "game.hint", "提示" },
            { "game.back", "返回" },
            { "game.hintcount", "提示 {0}/{1}" },
            { "hint.ad.title", "提示已用尽" },
            { "hint.ad.message", "观看广告获得 1 次提示?本局最多可看 {0} 次" },
            { "hint.ad.unavailable", "广告暂不可用,请稍后重试" },
            { "game.time", "用时 {0}" },
            { "game.exit.title", "退出对局" },
            { "game.exit.message", "当前进度将丢失,确定退出?" },
            { "game.exit.confirm", "退出" }, // 退出确认框按钮(2026-08-29 Bug 清单:按钮文案英语化)
            { "game.exit.cancel", "取消" },
            { "hint.ad.confirm", "看广告" }, // 广告提示确认框按钮
            { "hint.ad.cancel", "取消" },

            // 结算弹窗
            { "settlement.title.daily", "每日挑战完成" },
            { "settlement.title.normal", "对局完成" },
            { "settlement.message", "星级 {0}/3   用时 {1}   错误 {2}{3}" },
            { "settlement.hints", "  提示 {0}" },
            { "settlement.next", "再来一局" }, // 结算按钮(2026-08-29 Bug 清单:按钮文案英语化)
            { "settlement.home", "返回菜单" },

            // 水排序(第二玩法,19 文档)
            { "watersort.select.title", "选择关卡" },
            { "watersort.level.title", "第 {0} 关 · {1}" }, // 对局标题(难度复用 diff.* 文案)
            { "watersort.coins", "金币 {0}" },
            { "watersort.step", "步数 {0}" },
            { "watersort.settle.title", "过关成功" },
            { "watersort.settle.result", "步数 {0} · {1}" }, // 结算:步数与难度(难度复用 diff.*)
            { "watersort.btn.restart", "重开" },
            { "watersort.btn.retry", "重试" },
            { "watersort.btn.next", "下一关" },
            { "watersort.btn.hint", "提示" }, // M1.4 金币消费点(单价/上限见 WaterSortConfig)
            { "watersort.btn.tube", "空瓶" }, // 额外空瓶(金币购买 +1 支空管)
            { "watersort.settle.reward", "金币 +{0}" }, // 首通奖励行(结算,仅首通显示)
            { "watersort.toast.noCoins", "金币不足" },
            { "watersort.toast.hintFail", "暂无可提示的走法,试试撤销" }, // 求解失败(死局/超时)不扣币
            { "watersort.toast.noLevels", "关卡包缺失,请稍后重进" }, // 题库加载失败(构建期错误兜底)
            { "watersort.toast.badLevel", "关卡数据异常" }, // 越界/损坏条目

            // 水排序每日挑战(M2.3,WS-09)
            { "watersort.daily.title", "每日挑战" }, // 对局标题 + 选关页入口按钮共用
            { "watersort.daily.state.new", "今日尚未完成" }, // 每日主页状态行
            { "watersort.daily.state.done", "今日已完成" },
            { "watersort.daily.streak", "已连续 {0} 天" }, // 连续完成天数(Streak)
            { "watersort.daily.play", "开始挑战" }, // 今日未完成时按钮
            { "watersort.daily.replay", "再玩一次" }, // 今日已完成时按钮(重玩不重复落完成/不发奖)

            // 激励视频三点位(M3.1,WS-12/13):确认面板消息 + 结算翻倍按钮;按钮文案复用 hint.ad.confirm/cancel
            { "watersort.ad.hint", "金币不足,看广告免费获得 1 次提示(每关最多 {0} 次)" },
            { "watersort.ad.tube", "金币不足,看广告免费获得 1 支空瓶(每关最多 {0} 次)" },
            { "watersort.ad.double", "观看完整广告,首通金币奖励翻倍(每关最多 {0} 次)" },
            { "watersort.btn.double", "翻倍奖励" }, // 结算翻倍按钮(仅首通结算显示)
            { "watersort.btn.doubled", "已翻倍" },  // 翻倍完成后按钮文案(禁用态)

            // 新手引导(M3.3,WS-14):首次 3 步(点击倒水/同色聚合/卡关求助)+ 跳过;步数走 WaterSortConfig
            { "watersort.tutorial.pour", "点选一根试管,再点另一根,把水倒过去" },
            { "watersort.tutorial.merge", "把相同颜色的水倒到一起,它们会叠起来" },
            { "watersort.tutorial.hint", "卡住了?点「提示」,它会帮你走一步" },
            { "watersort.tutorial.skip", "跳过引导" },
        };

        static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            // 主菜单
            { "menu.title", "Sudoku Box" },
            { "menu.start", "Start" },
            { "menu.daily", "Daily Challenge" },
            { "menu.moreGames", "More Games" },
            { "menu.settings", "Settings" },

            // More Games 弹窗
            { "moreGames.title", "More Games" },
            { "moreGames.close", "Done" },
            { "moreGames.comingSoon", "Coming Soon" },

            // 设置弹窗
            { "settings.title", "Settings" },
            { "settings.soundOn", "Sound: On" },
            { "settings.soundOff", "Sound: Off" },
            { "settings.musicOn", "Music: On" },
            { "settings.musicOff", "Music: Off" },
            { "settings.themeLight", "Theme: Light" },
            { "settings.themeDark", "Theme: Dark" },
            { "settings.langZh", "Language: 中文" },
            { "settings.langEn", "Language: English" },
            { "settings.done", "Done" },
            { "settings.removeAds", "Remove Ads" },
            { "settings.removeAdsPurchased", "Remove Ads: Purchased" },
            { "settings.privacy", "Privacy Policy" },
            { "iap.notReady", "Store not ready. Try again later." },

            // 难度选择
            { "diff.title", "Select Difficulty" },
            { "diff.easy", "Easy" },
            { "diff.medium", "Medium" },
            { "diff.hard", "Hard" },

            // 对局视图
            { "game.title.daily", "Daily Challenge" },
            { "game.title.normal", "Sudoku - {0}" },
            { "game.diff.easy", "Easy" },
            { "game.diff.medium", "Medium" },
            { "game.diff.hard", "Hard" },
            { "game.mode.input", "Number" },
            { "game.mode.note", "Notes" },
            { "game.undo", "Undo" },
            { "game.redo", "Redo" },
            { "game.erase", "Erase" },
            { "game.hint", "Hint" },
            { "game.back", "Back" },
            { "game.hintcount", "Hints {0}/{1}" },
            { "hint.ad.title", "Hints Exhausted" },
            { "hint.ad.message", "Watch an ad for 1 more hint? Max {0} per game" },
            { "hint.ad.unavailable", "Ads not ready. Try again later." },
            { "game.time", "Time {0}" },
            { "game.exit.title", "Quit Game" },
            { "game.exit.message", "Progress will be lost. Quit?" },
            { "game.exit.confirm", "Quit" },
            { "game.exit.cancel", "Cancel" },
            { "hint.ad.confirm", "Watch" },
            { "hint.ad.cancel", "Cancel" },

            // 结算弹窗
            { "settlement.title.daily", "Daily Challenge Complete" },
            { "settlement.title.normal", "Level Complete" },
            { "settlement.message", "Stars {0}/3   Time {1}   Mistakes {2}{3}" },
            { "settlement.hints", "   Hints {0}" },
            { "settlement.next", "Next" },
            { "settlement.home", "Menu" },

            // Water Sort (2nd mode, doc 19)
            { "watersort.select.title", "Select Level" },
            { "watersort.level.title", "Level {0} · {1}" },
            { "watersort.coins", "Coins {0}" },
            { "watersort.step", "Moves {0}" },
            { "watersort.settle.title", "Level Cleared!" },
            { "watersort.settle.result", "Moves {0} · {1}" },
            { "watersort.btn.restart", "Restart" },
            { "watersort.btn.retry", "Retry" },
            { "watersort.btn.next", "Next" },
            { "watersort.btn.hint", "Hint" },
            { "watersort.btn.tube", "Extra" },
            { "watersort.settle.reward", "+{0} Coins" },
            { "watersort.toast.noCoins", "Not enough coins" },
            { "watersort.toast.hintFail", "No hint available. Try Undo." },
            { "watersort.toast.noLevels", "Level pack missing. Re-enter later." },
            { "watersort.toast.badLevel", "Level data error" },

            // Water Sort Daily Challenge (M2.3, WS-09)
            { "watersort.daily.title", "Daily Challenge" }, // Game title + select-page entry button share
            { "watersort.daily.state.new", "Not cleared today" },
            { "watersort.daily.state.done", "Cleared today" },
            { "watersort.daily.streak", "{0}-day streak" },
            { "watersort.daily.play", "Start" },
            { "watersort.daily.replay", "Play again" },

            // Rewarded-video points (M3.1, WS-12/13); buttons reuse hint.ad.confirm/cancel
            { "watersort.ad.hint", "Low on coins? Watch an ad for a free hint (up to {0} per level)" },
            { "watersort.ad.tube", "Low on coins? Watch an ad for a free empty tube (up to {0} per level)" },
            { "watersort.ad.double", "Watch a full ad to double your first-win coins (up to {0} per level)" },
            { "watersort.btn.double", "Double Reward" },
            { "watersort.btn.doubled", "Doubled" },

            // Onboarding (M3.3, WS-14): 3 first-run steps (tap-to-pour / same-color merge / stuck hint) + skip
            { "watersort.tutorial.pour", "Tap a tube, then tap another to pour the water." },
            { "watersort.tutorial.merge", "Pour matching colors onto each other to stack them." },
            { "watersort.tutorial.hint", "Stuck? Tap Hint to take one step." },
            { "watersort.tutorial.skip", "Skip" },
        };
    }
}