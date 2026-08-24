using System.Collections.Generic;
using Box.Services;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// 音频服务实现(Phase 8 体验打磨:音频系统)。
    /// 职责:BGM 循环(全局常驻,主菜单/对局不断音)+ SFX 播放(SFX 源池轮转,连点不丢音)+ 偏好联动(开关即时生效)。
    /// 架构:经 IAssetService(Addressables)异步加载 AudioClip 并缓存——首次播放有异步延迟,此后零延迟;
    /// 热更侧只调 IAudioService 接口(11 文档:Addressables/SDK 只在壳层)。
    /// 设计取舍:v1.0 仅 1 首 BGM + 短 SFX,用 AudioSource 分层音量(常量)即可,不引入 AudioMixer 资产;
    /// 未来需要动态混音/特效链路时再上 AudioMixer,接口不变。
    /// 生命周期:AppBootstrap 创建,承载 GameObject 挂 DontDestroyOnLoad(场景切换不中断 BGM)。
    /// </summary>
    public sealed class AudioManager : IAudioService
    {
        // 地址约定(Phase6AddressablesSetup.RegisterArtAssets):Art/Audio/{SFX,BGM}/{名字}(去扩展名)
        const string SfxAddressPrefix = "Art/Audio/SFX/";
        const string BgmAddressPrefix = "Art/Audio/BGM/";

        const float MusicVolume = 0.45f; // BGM 基础音量(换曲试听后微调)
        const float SfxVolume = 0.8f;    // SFX 基础音量
        const int SfxPoolSize = 4;       // SFX 源池大小:填数连点/提示与点击同帧并发时不丢音

        readonly IAssetService _assets;
        readonly ISettingsService _settings;
        readonly Dictionary<string, AudioClip> _sfxCache = new(); // 已加载 SFX 缓存(命中即播,免重复异步加载)
        readonly Dictionary<string, AudioClip> _bgmCache = new();
        readonly AudioSource[] _sfxPool;
        int _sfxCursor;

        AudioSource _bgmSource;
        GameObject _root;

        public AudioManager(IAssetService assets, ISettingsService settings)
        {
            _assets = assets;
            _settings = settings;
            _sfxPool = new AudioSource[SfxPoolSize];
        }

        /// <summary>创建常驻对象与音频源,随后按偏好播放 BGM。</summary>
        public void Initialize()
        {
            _root = new GameObject("AudioManager");
            Object.DontDestroyOnLoad(_root); // 跨场景常驻:主菜单→对局→结算 BGM 不中断

            var bgmGo = new GameObject("BGM");
            bgmGo.transform.SetParent(_root.transform, false);
            _bgmSource = bgmGo.AddComponent<AudioSource>();
            _bgmSource.loop = true;       // BGM 循环
            _bgmSource.playOnAwake = false;
            _bgmSource.spatialBlend = 0f; // 2D 全局声
            _bgmSource.volume = MusicVolume;

            for (int i = 0; i < SfxPoolSize; i++)
            {
                var go = new GameObject("SFX" + i);
                go.transform.SetParent(_root.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.volume = SfxVolume;
                _sfxPool[i] = src;
            }

            // 主 BGM 常驻(PlayBgm 内部检查音乐开关,关闭时只缓存不播)
            PlayBgm("bgm_main");
        }

        /// <summary>播放短音效;开关关闭或资源缺失时静默跳过(缺失已由资源服务 LogWarning)。</summary>
        public void PlaySfx(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_settings != null && !_settings.SoundEnabled) return;
            if (_sfxPool.Length == 0) return;

            if (_sfxCache.TryGetValue(name, out var clip))
            {
                PlayClip(clip);
                return;
            }

            // 首次播放:异步加载,完成后直接播(此后缓存命中零延迟)
            _assets?.LoadAsset<AudioClip>(SfxAddressPrefix + name, clip =>
            {
                if (clip == null) return;
                _sfxCache[name] = clip;
                if (_settings != null && !_settings.SoundEnabled) return; // 加载期间用户关了音效:放弃本次
                PlayClip(clip);
            });
        }

        /// <summary>切换 BGM(循环播放);音乐开关关闭时只缓存不播,开启后续播。</summary>
        public void PlayBgm(string name)
        {
            if (string.IsNullOrEmpty(name) || _bgmSource == null) return;
            if (_bgmCache.TryGetValue(name, out var clip))
            {
                SetBgmAndPlay(clip);
                return;
            }

            _assets?.LoadAsset<AudioClip>(BgmAddressPrefix + name, clip =>
            {
                if (clip == null) return;
                _bgmCache[name] = clip;
                SetBgmAndPlay(clip);
            });
        }

        void SetBgmAndPlay(AudioClip clip)
        {
            if (_bgmSource.clip == clip) return; // 同曲不重载
            _bgmSource.clip = clip;
            if (_settings == null || _settings.MusicEnabled) _bgmSource.Play();
        }

        /// <summary>设置音效开关:写偏好(PlayerPrefs)并即时生效。</summary>
        public void SetSoundEnabled(bool enabled)
        {
            if (_settings != null)
            {
                _settings.SoundEnabled = enabled;
                _settings.Save();
            }
        }

        /// <summary>设置音乐开关:写偏好并即时生效(关闭即停,再开续播已加载的 BGM)。</summary>
        public void SetMusicEnabled(bool enabled)
        {
            if (_settings != null)
            {
                _settings.MusicEnabled = enabled;
                _settings.Save();
            }
            if (_bgmSource == null) return;
            if (enabled)
            {
                if (_bgmSource.clip != null) _bgmSource.Play(); // 已有曲目直接续播
                else PlayBgm("bgm_main");                        // 从未加载过:走加载链路
            }
            else
            {
                _bgmSource.Stop();
            }
        }

        void PlayClip(AudioClip clip)
        {
            var src = _sfxPool[_sfxCursor];
            _sfxCursor = (_sfxCursor + 1) % _sfxPool.Length;
            src.PlayOneShot(clip); // PlayOneShot:不打断上一音,连点自然重叠
        }
    }
}
