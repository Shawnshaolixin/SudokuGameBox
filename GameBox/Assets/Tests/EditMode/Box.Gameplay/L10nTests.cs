using System;
using System.IO;
using Box.Services;
using NUnit.Framework;
using UnityEngine;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// 轻量本地化表测试(FR-17 首期 zh/en,不引第三方库):
    /// 查表/格式化占位符/缺键回退；SetLanguage 写 Settings 偏好(PlayerPrefs)+ 广播 LanguageChanged——
    /// 这是「语言切换后全局 UI 即时刷新」的根:各视图订阅事件刷新文案。
    /// 隔离:SettingsService 注入测试前缀;SaveService 注入临时目录;事件订阅成对退订。
    /// </summary>
    public class L10nTests
    {
        const string Prefix = "test_l10n_";

        string _dir;
        int _changed;

        [SetUp]
        public void SetUp()
        {
            L10n.Init("zh");
            _changed = 0;
            ServiceLocator.Reset();
            _dir = Path.Combine(Path.GetTempPath(), "L10n" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(Prefix + ".sound");
            PlayerPrefs.DeleteKey(Prefix + ".music");
            PlayerPrefs.DeleteKey(Prefix + ".theme");
            PlayerPrefs.DeleteKey(Prefix + ".language");
            ServiceLocator.Reset();
            L10n.Init("zh");
            try { Directory.Delete(_dir, true); } catch { /* 尽力清理 */ }
        }

        string SavePath() => Path.Combine(_dir, "box.save");

        // ---- 查表 ----

        [Test]
        public void Get_Defaults_To_Chinese()
        {
            L10n.Init("zh");
            Assert.AreEqual("开始游戏", L10n.Get("menu.start"), "zh 应命中中文表");
            Assert.AreEqual("数独游戏盒", L10n.Get("menu.title"));
        }

        [Test]
        public void SetLanguage_Switches_English_Lookup()
        {
            L10n.SetLanguage("en");
            Assert.AreEqual("Start", L10n.Get("menu.start"), "en 应命中英文表");
            Assert.AreEqual("Sudoku Box", L10n.Get("menu.title"));
        }

        [Test]
        public void Format_Replaces_Placeholders()
        {
            L10n.Init("zh");
            Assert.AreEqual("数独 - 简单", L10n.Format("game.title.normal", "简单"));
            Assert.AreEqual("提示 2/3", L10n.Format("game.hintcount", 2, 3));
        }

        [Test]
        public void Missing_Key_Falls_Back_To_Key()
        {
            L10n.Init("en");
            Assert.AreEqual("no.such.key", L10n.Get("no.such.key"), "缺键回退原始 key,不炸");
        }

        // ---- 切换事件链(全局刷新的根) ----

        [Test]
        public void SetLanguage_Persists_And_Broadcasts_Event()
        {
            // 真实 ServiceLocator 链:SettingsService(注入前缀)+ SaveService(临时目录)
            var settings = new SettingsService(Prefix);
            ServiceLocator.Register(new SaveService(SavePath()), settings);

            L10n.LanguageChanged += OnChanged;
            try
            {
                L10n.SetLanguage("en");

                Assert.AreEqual("en", settings.Language, "偏好应同步为 en(PlayerPrefs 落盘)");
                Assert.AreEqual("en", PlayerPrefs.GetString(Prefix + ".language", ""), "PlayerPrefs 应落盘 en");
                Assert.AreEqual(1, _changed, "LanguageChanged 应触发一次");
            }
            finally
            {
                L10n.LanguageChanged -= OnChanged;
            }
        }

        [Test]
        public void SetLanguage_Same_Code_No_Event()
        {
            var settings = new SettingsService(Prefix);
            ServiceLocator.Register(new SaveService(SavePath()), settings);

            L10n.LanguageChanged += OnChanged;
            try
            {
                L10n.SetLanguage("zh"); // Current 已是 zh(SetUp),无变化不广播
                Assert.AreEqual(0, _changed, "同语言切换不应触发事件");
            }
            finally
            {
                L10n.LanguageChanged -= OnChanged;
            }
        }

        [Test]
        public void Refresh_After_SetLanguage_Uses_New_Language()
        {
            // 模拟视图刷新:语言切换后再取键,应拿到新语言文案
            L10n.SetLanguage("en");
            Assert.AreEqual("Done", L10n.Get("settings.done"));

            L10n.SetLanguage("zh");
            Assert.AreEqual("完成", L10n.Get("settings.done"));
        }

        void OnChanged() => _changed++;
    }
}