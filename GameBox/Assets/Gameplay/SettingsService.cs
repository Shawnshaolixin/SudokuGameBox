using Box.Services;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// 偏好设置实现(11 文档 §8.1:「PlayerPrefs 只留音量/语言等偏好」)。
    /// 音效/音乐/主题/语言属于全局偏好,不进加密存档,用 PlayerPrefs 落盘。
    /// keyPrefix 可注入(测试用独立前缀,避免污染真实偏好);默认 "settings"。
    /// 属壳层(AOT):热更侧只依赖 Abstractions 的 ISettingsService。
    /// v1.1 起偏好同步/漫游(云)可在此扩展,接口不变。
    /// 语言:v1.0 固定英文(设置页无切换入口,2026-08-29 Bug 清单),
    /// 不读 PlayerPrefs 旧值(强制 en,防历史 zh 残留);L10n 机制保留备未来多语言。
    /// </summary>
    public sealed class SettingsService : ISettingsService
    {
        const string KeySound = ".sound";
        const string KeyMusic = ".music";
        const string KeyTheme = ".theme";
        const string KeyLanguage = ".language";

        readonly string _prefix;

        // 直接公开实现接口属性(接口 ISettingsService 要求 get+set 均可访问);
        // setter 即改内存即落盘 —— 偏好量小且要立即生效,不依赖显式 Save。
        public bool SoundEnabled
        {
            get => _sound;
            set
            {
                _sound = value;
                PlayerPrefs.SetInt(_prefix + KeySound, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public bool MusicEnabled
        {
            get => _music;
            set
            {
                _music = value;
                PlayerPrefs.SetInt(_prefix + KeyMusic, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public int ThemeIndex
        {
            get => _theme;
            set
            {
                _theme = value;
                PlayerPrefs.SetInt(_prefix + KeyTheme, value);
                PlayerPrefs.Save();
            }
        }

        public string Language
        {
            get => _language;
            set
            {
                _language = string.IsNullOrEmpty(value) ? "en" : value;
                PlayerPrefs.SetString(_prefix + KeyLanguage, _language);
                PlayerPrefs.Save();
            }
        }

        bool _sound, _music;
        int _theme;
        string _language;

        public SettingsService(string keyPrefix = "settings")
        {
            _prefix = keyPrefix;
            _sound = PlayerPrefs.GetInt(_prefix + KeySound, 1) == 1;   // 默认开
            _music = PlayerPrefs.GetInt(_prefix + KeyMusic, 1) == 1;   // 默认开
            _theme = PlayerPrefs.GetInt(_prefix + KeyTheme, 0);        // 0=浅色(默认)
            _language = "en"; // v1.0 固定英文:不读 PlayerPrefs(忽略历史 zh 残留),见类注释
        }

        public void Save() => PlayerPrefs.Save();
    }
}