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
    /// 语言:zh 中文 / en English —— L10n.SetLanguage 广播 LanguageChanged,其余视图订阅后即时刷新(FR-17)。
    /// prefab: Resources/UI/Popups/SettingsPopup(Phase5SceneSetup 生成):Title + 4 个切换按钮 + CloseButton,
    /// 按钮文案子节点约定 "Label"(与 Phase4 弹窗一致)。
    /// </summary>
    public sealed class SettingsView : UIView
    {
        // 主题预览色:浅色取设计 token Surface/Primary(弹窗改造 2026-08 后卡片底色);深色保留原值
        static readonly Color ThemeLightBg = UITheme.Panel; // #FFF9E9(与 UITheme 同源,防漂移)
        static readonly Color ThemeDarkBg = new Color(0.08f, 0.08f, 0.10f, 0.97f);

        /// <summary>隐私政策 URL 占位(A6:账号 + GitHub Pages 就绪后替换为真实地址)。</summary>
        const string PrivacyUrl = "https://shawnshaolixin.github.io/SudokuGameBox/privacy-policy.html"; // TODO(A6): 替换真实 URL

        UIService _svc;
        IIapService _iap;
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
            // 弹窗改造(2026-08):卡片背景在 Card 子节点(根为全屏遮罩),未迁移 prefab 回退根 Image
            var card = transform.Find("Card");
            _bg = card != null ? card.GetComponent<Image>() : GetComponent<Image>();
            _title = FindInCard("Title")?.GetComponent<TextMeshProUGUI>();
            Bind("SoundButton", ToggleSound);
            Bind("MusicButton", ToggleMusic);
            Bind("ThemeButton", ToggleTheme);
            Bind("LangButton", ToggleLanguage);
            Bind("CloseButton", () => Close().Forget());

            // Phase 7 7-1:去广告购买 + 隐私政策按钮
            _iap = ServiceLocator.Iap;
            if (_iap != null) _iap.PurchaseCompleted += OnPurchaseCompleted; // 购买成功 → 刷新按钮文案
            Bind("RemoveAdsButton", OnRemoveAds);
            Bind("PrivacyButton", OnPrivacy);
            return UniTask.CompletedTask;
        }

        // 隐藏 MonoBehaviour.OnDestroy:退订防泄漏(sealed 类用 private new 避免 CS0628,同 GameplayView)
        private new void OnDestroy()
        {
            if (_iap != null) _iap.PurchaseCompleted -= OnPurchaseCompleted;
        }

        /// <summary>去广告购买(非消耗品,已购幂等);成功后由 PurchaseCompleted 刷新文案。</summary>
        void OnRemoveAds()
        {
            if (_iap == null) return;
            if (!_iap.IsInitialized)
            {
                // 商店未初始化(无 GMS/网络不可达):明确反馈,避免"点了没反应"(Phase 9)
                BoxToast.Show(L10n.Get("iap.notReady"));
                return;
            }
            if (!_iap.IsRemoveAdsPurchased) _iap.BuyRemoveAds();
        }

        /// <summary>打开隐私政策页(浏览器,合规 FR-17/05 文档)。</summary>
        void OnPrivacy()
        {
            Application.OpenURL(PrivacyUrl);
        }

        void OnPurchaseCompleted() => Refresh();

        protected override async UniTask OnShow(object args)
        {
            Refresh(); // 显示时同步一次当前偏好
            // 弹入(D-15):缩放卡片,不缩全屏遮罩(防脉冲期间遮罩露出屏幕边缘接缝)
            var card = transform.Find("Card");
            await BoxTween.ScalePulse(card != null ? card : transform, 0.8f, 1f, 0.22f);
        }

        void Bind(string path, Action onClick)
        {
            var btn = FindInCard(path)?.GetComponent<BoxButton>();
            if (btn != null) btn.OnClick(onClick);
        }

        // ---- 切换(读-翻转-写;ISettingsService setter 即时落盘) ----

        // 开关经音频服务代理:写偏好 + 即时生效(BGM 停/续播)。无音频服务时(测试/启动早期)直接翻偏好兜底。
        void ToggleSound()
        {
            var s = ServiceLocator.Settings;
            if (s == null) return;
            var audio = ServiceLocator.Audio;
            if (audio != null) audio.SetSoundEnabled(!s.SoundEnabled);
            else s.SoundEnabled = !s.SoundEnabled;
            Refresh();
        }

        void ToggleMusic()
        {
            var s = ServiceLocator.Settings;
            if (s == null) return;
            var audio = ServiceLocator.Audio;
            if (audio != null) audio.SetMusicEnabled(!s.MusicEnabled);
            else s.MusicEnabled = !s.MusicEnabled;
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
            string next = s != null && s.Language == "en" ? "zh" : "en";
            L10n.SetLanguage(next); // 写偏好 + 广播 LanguageChanged → 全 UI 即时刷新
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

            SetLabel("SoundButton", L10n.Get(sound ? "settings.soundOn" : "settings.soundOff"));
            SetLabel("MusicButton", L10n.Get(music ? "settings.musicOn" : "settings.musicOff"));
            SetLabel("ThemeButton", L10n.Get(theme == 0 ? "settings.themeLight" : "settings.themeDark"));
            SetLabel("LangButton", L10n.Get(lang == "en" ? "settings.langEn" : "settings.langZh"));
            SetLabel("CloseButton", L10n.Get("settings.done"));
            bool purchased = _iap != null && _iap.IsRemoveAdsPurchased;
            SetLabel("RemoveAdsButton", L10n.Get(purchased ? "settings.removeAdsPurchased" : "settings.removeAds"));
            SetLabel("PrivacyButton", L10n.Get("settings.privacy"));

            // 主题即时预览:卡片背景色切换(玩法场景换肤属于全 UI 主题系统,v1.0 后置)
            if (_bg != null) _bg.color = theme == 0 ? ThemeLightBg : ThemeDarkBg;
            if (_title != null) _title.color = theme == 0 ? UITheme.TextPrimary : Color.white; // 浅色=token #3A2A1A
            foreach (var label in transform.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (label != _title)
                    label.color = theme == 0 ? UITheme.TextPrimary : Color.white;
        }

        void SetLabel(string path, string text)
        {
            var t = FindInCard(path + "/Label")?.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }
    }
}