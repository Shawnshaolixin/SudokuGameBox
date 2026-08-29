using System;
using Box.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// SettingsService 测试(Phase 5 5-2):PlayerPrefs 偏好(音量/音乐/主题/语言,§8.1 留在 PlayerPrefs)。
    /// 隔离:每次用例独立 keyPrefix,不污染真机偏好;TearDown 清键。
    /// </summary>
    public class SettingsServiceTests
    {
        string _prefix;

        [SetUp]
        public void SetUp()
        {
            _prefix = "test_" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(_prefix + ".sound");
            PlayerPrefs.DeleteKey(_prefix + ".music");
            PlayerPrefs.DeleteKey(_prefix + ".theme");
            PlayerPrefs.DeleteKey(_prefix + ".language");
        }

        [Test]
        public void Defaults_SoundOn_MusicOn_Theme0_English()
        {
            var s = new SettingsService(_prefix);
            Assert.IsTrue(s.SoundEnabled, "音效默认开");
            Assert.IsTrue(s.MusicEnabled, "音乐默认开");
            Assert.AreEqual(0, s.ThemeIndex, "0=浅色(默认)");
            // v1.0 固定英文(2026-08-29 Bug 清单②:设置页无语言切换入口,忽略 PlayerPrefs 旧 zh)
            Assert.AreEqual("en", s.Language, "默认英文");
        }

        [Test]
        public void Setter_Writes_Immediately_And_Persists_Across_Instances()
        {
            var s = new SettingsService(_prefix);
            s.SoundEnabled = false;
            s.MusicEnabled = false;
            s.ThemeIndex = 1;
            s.Language = "en";

            // 新实例(模拟重启)读回:setter 即时落盘无需显式 Save
            var s2 = new SettingsService(_prefix);
            Assert.IsFalse(s2.SoundEnabled);
            Assert.IsFalse(s2.MusicEnabled);
            Assert.AreEqual(1, s2.ThemeIndex);
            Assert.AreEqual("en", s2.Language);
        }

        [Test]
        public void Empty_Language_Falls_Back_To_English()
        {
            var s = new SettingsService(_prefix);
            s.Language = "";
            // 空值回退英文(与 v1.0 固定英文策略一致,见类注释)
            Assert.AreEqual("en", s.Language);
        }
    }
}