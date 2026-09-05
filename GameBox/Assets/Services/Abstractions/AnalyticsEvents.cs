using System.Text.RegularExpressions;

namespace Box.Services
{
    /// <summary>
    /// 埋点事件名契约(04 文档 §6.1,2026-09-05 全库清理后立规)。
    ///
    /// Firebase Analytics 对事件名有硬性字符限制:仅小写字母/数字/下划线,须以字母开头,
    /// 长度 ≤40 —— 违反的事件被 SDK 静默拒收(真机实测 watersort.tutorial_step 等带点/
    /// 斜杠命名全部丢弃,DebugView/后台不可见)。所有 IAnalyticsService 实现须在入口
    /// 校验一次,违规打 Warning 提醒,防"埋了但从未上报"的静默失效。
    ///
    /// 命名规范:全小写 snake_case `<模块>_<动作>[_<细节>]`,模块前缀必带
    /// (多玩法盒子需归属),如 sudoku_level_start / watersort_tutorial_step / ui_show。
    /// </summary>
    public static class AnalyticsEvents
    {
        // FA 规则:^[a-z][a-z0-9_]{0,39}$(字母开头 + 总长 ≤40)
        static readonly Regex ValidName = new Regex("^[a-z][a-z0-9_]{0,39}$", RegexOptions.Compiled);

        /// <summary>事件名是否合规(非法字符/大写/超长/数字开头均 false)。</summary>
        public static bool IsValidName(string eventName) => eventName != null && ValidName.IsMatch(eventName);
    }
}
