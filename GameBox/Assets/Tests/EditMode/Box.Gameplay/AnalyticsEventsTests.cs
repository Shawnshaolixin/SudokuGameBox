using System.Collections.Generic;
using Box.Services;
using NUnit.Framework;
using UnityEngine;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// 埋点事件名契约测试(04 文档 §6.1,2026-09-05 全库清理后立规)。
    /// 历史教训:带点/斜杠命名(watersort.tutorial_step 等)违反 Firebase Analytics 字符限制,
    /// 被 SDK 静默拒收且无任何报错——本组用例把规则锁进回归:实装事件名快照必须全合规,
    /// 桩对违规名必须丢弃(开发/CI 期暴露,而非真机静默)。
    /// </summary>
    public class AnalyticsEventsTests
    {
        [Test]
        public void LiveEvents_All_Valid()
        {
            // 04 §6.2 实装字典快照:新增事件先补字典再写码,并加入本快照
            var live = new[]
            {
                "services_initialized",
                "ui_show",
                "sudoku_level_start",
                "sudoku_level_complete",
                "sudoku_hint_used",
                "watersort_ad_reward",
                "watersort_coin_spend",
                "watersort_coin_reward",
                "watersort_tutorial_step",
            };
            foreach (var name in live)
                Assert.IsTrue(AnalyticsEvents.IsValidName(name), $"实装事件名违规(被 FA 静默拒收): {name}");
        }

        [TestCase("a")]
        [TestCase("ui_show")]
        [TestCase("sudoku_level_start")]
        [TestCase("watersort_tutorial_step")]
        [TestCase("services_initialized")]
        public void ValidNames_Accepted(string name)
        {
            Assert.IsTrue(AnalyticsEvents.IsValidName(name));
        }

        [TestCase("sudoku.level_start")] // 点号(FA 拒收源)
        [TestCase("UI/Popups/MoreGamesPopup.ui_show")] // 斜杠+大小写(旧自动埋点拼法)
        [TestCase("LevelStart")] // 大写
        [TestCase("1level_start")] // 数字开头
        [TestCase("level-start")] // 连字符
        [TestCase("level start")] // 空格
        [TestCase("")]
        public void InvalidNames_Rejected(string name)
        {
            Assert.IsFalse(AnalyticsEvents.IsValidName(name));
        }

        [Test]
        public void LengthBoundary_40Ok_41Rejected()
        {
            Assert.IsTrue(AnalyticsEvents.IsValidName(new string('a', 40)));
            Assert.IsFalse(AnalyticsEvents.IsValidName(new string('a', 41)));
        }

        [Test]
        public void Null_Rejected()
        {
            Assert.IsFalse(AnalyticsEvents.IsValidName(null));
        }

        [Test]
        public void Stub_DropsInvalid_PrintsValid()
        {
            // 捕获 Debug 日志断言桩行为:违规 = Warning + 不上报;合规 = 正常打印
            var lines = new List<string>();
            void Handler(string condition, string stack, LogType type) => lines.Add($"{type}:{condition}");
            Application.logMessageReceived += Handler;
            try
            {
                var stub = new AnalyticsServiceStub();
                stub.LogEvent("sudoku.level_start"); // 违规
                stub.LogEvent("sudoku_level_start"); // 合规
                Assert.IsFalse(lines.Exists(l => l.Contains("[AnalyticsStub] 事件:sudoku.level_start")),
                    "违规事件名不应被桩上报");
                Assert.IsTrue(lines.Exists(l => l.Contains("Warning") && l.Contains("非法")),
                    "违规事件名应打 Warning 提醒(开发期暴露)");
                Assert.IsTrue(lines.Exists(l => l.Contains("[AnalyticsStub] 事件:sudoku_level_start")),
                    "合规事件名应正常上报");
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }
        }
    }
}
