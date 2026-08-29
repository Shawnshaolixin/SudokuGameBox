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
    /// 设置弹窗(Phase 5 5-2):音效/音乐 基础设置。
    /// 值走 ISettingsService(PlayerPrefs 偏好,§8.1「PlayerPrefs 只留音量/语言等偏好」);切换即生效并落盘。
    /// 主题固定浅色(2026-08-29 Bug 清单:主题按钮已移除,卡片与文字恒为设计 token 浅色值;玩法场景换肤 v1.0 后置,注释见 §10.2);
    /// 语言按钮已移除(2026-08-29 Bug 清单:v1.0 固定英文,无切换入口;L10n 机制保留备未来)。
    /// prefab: Assets/UI/Prefabs/Popups/SettingsPopup(Phase5SceneSetup 生成):Title + 2 个切换按钮 + 去广告/隐私 + CloseButton,
    /// 按钮文案子节点约定 "Label"(与 Phase4 弹窗一致)。
    /// </summary>
    public sealed class SettingsView : UIView
    {
        // 主题固定浅色:取设计 token Panel 作卡片底色(弹窗改造 2026-08 后卡片底色,与 UITheme 同源,防漂移)
        static readonly Color ThemeLightBg = UITheme.Panel; // #FFF9E9

        /// <summary>隐私政策 URL(合规 FR-17,文档见 docs/ 与 GitHub Pages 同源)。</summary>
        const string PrivacyUrl = "https://shawnshaolixin.github.io/SudokuGameBox/privacy-policy.html";

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
            // 主题/语言切换按钮已移除(2026-08-29 Bug 清单:固定浅色 + 英文)
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

            SetLabel("SoundButton", L10n.Get(sound ? "settings.soundOn" : "settings.soundOff"));
            SetLabel("MusicButton", L10n.Get(music ? "settings.musicOn" : "settings.musicOff"));
            // 主题/语言按钮已移除(2026-08-29 Bug 清单),不再刷新
            SetLabel("CloseButton", L10n.Get("settings.done"));
            bool purchased = _iap != null && _iap.IsRemoveAdsPurchased;
            SetLabel("RemoveAdsButton", L10n.Get(purchased ? "settings.removeAdsPurchased" : "settings.removeAds"));
            SetLabel("PrivacyButton", L10n.Get("settings.privacy"));

            // 主题固定浅色(2026-08-29 移除主题切换按钮):卡片与文字恒为设计 token 浅色值
            if (_bg != null) _bg.color = ThemeLightBg;
            if (_title != null) _title.color = UITheme.TextPrimary; // #3A2A1A
            foreach (var label in transform.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (label != _title)
                    label.color = UITheme.TextPrimary;
        }

        void SetLabel(string path, string text)
        {
            var t = FindInCard(path + "/Label")?.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }
    }
}