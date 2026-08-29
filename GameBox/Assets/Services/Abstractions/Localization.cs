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
        };
    }
}