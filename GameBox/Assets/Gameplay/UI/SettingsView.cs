using System;
using Box.Services;
using Box.UI;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Box.Gameplay
{
    /// <summary>
    /// 设置弹窗(Phase 5 5-2):音效/音乐/主题/语言 基础设置。
    /// 值走 ISettingsService(PlayerPrefs 偏好,§8.1「PlayerPrefs 只留音量/语言等偏好」);切换即生效并落盘。
    /// 主题:0=浅色 1=深色,弹窗背景色即时预览(玩法场景换肤 v1.0 后置,注释见 §10.2);
    /// 语言:zh 中文 / en English(本地化管线后置,此处仅存偏好,按钮文案仍中文)。
    /// prefab: Resources/UI/Popups/SettingsPopup(Phase5SceneSetup 生成):Title + 4 个切换按钮 + CloseButton,
    /// 按钮文案子节点约定 "Label"(与 Phase4 弹窗一致)。
    /// </summary>
    public sealed class SettingsView : UIView
    {
        static readonly Color ThemeLightBg = new Color(0.94f, 0.94f, 0.92f, 0.98f);
        static readonly Color ThemeDarkBg = new Color(0.08f, 0.08f, 0.10f, 0.97f);

        UIService _svc;
        Image _bg;
        TextMeshProUGUI _title;

        protected override void Awake()
        {
            Layer = UILayer.Popup; // 返回键可关
            base.Awake();
        }

        protected override UniTask OnCreate()
        {
            _svc = UIService.Instance;
            _bg = GetComponent<Image>();
            _title = transform.Find("Title")?.GetComponent<TextMeshProUGUI>();
            Bind("SoundButton", ToggleSound);
            Bind("MusicButton", ToggleMusic);
            Bind("ThemeButton", ToggleTheme);
            Bind("LangButton", ToggleLanguage);
            Bind("CloseButton", () => Close().Forget());
            return UniTask.CompletedTask;
        }

        protected override async UniTask OnShow(object args)
        {
            Refresh(); // 显示时同步一次当前偏好
            await BoxTween.ScalePulse(transform, 0.8f, 1f, 0.22f); // 弹入(D-15)
        }

        void Bind(string path, Action onClick)
        {
            var btn = transform.Find(path)?.GetComponent<BoxButton>();
            if (btn != null) btn.OnClick(onClick);
        }

        // ---- 切换(读-翻转-写;ISettingsService setter 即时落盘) ----

        void ToggleSound()
        {
            var s = ServiceLocator.Settings;
            if (s != null) s.SoundEnabled = !s.SoundEnabled;
            Refresh();
        }

        void ToggleMusic()
        {
            var s = ServiceLocator.Settings;
            if (s != null) s.MusicEnabled = !s.MusicEnabled;
            Refresh();
        }

        void ToggleTheme()
        {
            var s = ServiceLocator.Settings;
            if (s != null) s.ThemeIndex = s.ThemeIndex == 0 ? 1 : 0;
            Refresh();
        }

        void ToggleLanguage()
        {
            var s = ServiceLocator.Settings;
            if (s != null) s.Language = s.Language == "zh" ? "en" : "zh";
            Refresh();
        }

        async UniTask Close()
        {
            if (_svc != null) await _svc.Router.PopAsync(); // 返回键/完成按钮同路径
        }

        // ---- 刷新文案与主题预览 ----

        void Refresh()
        {
            var s = ServiceLocator.Settings;
            bool sound = s == null || s.SoundEnabled;
            bool music = s == null || s.MusicEnabled;
            int theme = s == null ? 0 : s.ThemeIndex;
            string lang = s == null ? "zh" : s.Language;

            SetLabel("SoundButton", "音效:" + (sound ? "开" : "关"));
            SetLabel("MusicButton", "音乐:" + (music ? "开" : "关"));
            SetLabel("ThemeButton", "主题:" + (theme == 0 ? "浅色" : "深色"));
            SetLabel("LangButton", "语言:" + (lang == "zh" ? "中文" : "English"));
            SetLabel("CloseButton", "完成");

            // 主题即时预览:弹窗背景色切换(玩法场景换肤属于全 UI 主题系统,v1.0 后置)
            if (_bg != null) _bg.color = theme == 0 ? ThemeLightBg : ThemeDarkBg;
            if (_title != null) _title.color = theme == 0 ? new Color(0.12f, 0.12f, 0.14f) : Color.white;
            foreach (var label in transform.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (label != _title)
                    label.color = theme == 0 ? new Color(0.12f, 0.12f, 0.14f) : Color.white;
        }

        void SetLabel(string path, string text)
        {
            var t = transform.Find(path + "/Label")?.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }
    }
}