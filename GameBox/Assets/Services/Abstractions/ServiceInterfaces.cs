using System;
using System.Collections.Generic;

namespace Box.Services
{
    /// <summary>
    /// 广告服务接口:激励视频 + 去广告状态。
    /// 定义接口的目的是把「业务逻辑」和「具体 SDK」解耦:
    /// 上层只调用 IAdsService,不关心底层是 AdMob 还是桩实现。
    /// 11 文档:Services 接口程序集(Box.Services.Abstractions)是热更侧唯一可引用的服务程序集。
    /// </summary>
    public interface IAdsService
    {
        bool IsInitialized { get; }
        bool IsRewardedReady { get; }
        bool IsAdsRemoved { get; }

        void Initialize();

        /// <summary>设置去广告状态(内购完成后调用)。</summary>
        void SetRemoveAds(bool removed);

        /// <summary>展示激励视频;回调参数 true 表示玩家看完并应发放奖励。</summary>
        void ShowRewardedAd(Action<bool> onReward);

        /// <summary>
        /// 每完成一局(过关)通知(频控计数,与展示解耦)。全局共享(M3.2,WS-12):
        /// 数独/水排序任一玩法过关都累计,「新用户前 N 局保护」按此计数——连关路径只计数、
        /// 不在连关时展示插屏(展示只在 ShowInterstitial 的局间出口调用点)。
        /// </summary>
        void NotifyLevelCompleted();

        /// <summary>展示插屏广告(时机由玩法层在「过关 → 返回关卡选择/大厅」类局间出口调用;连关不调用)。</summary>
        /// <remarks>
        /// 频控规则(04 文档 §广告频控,真实现内部执行,参数默认表见 AdFrequencySettings):
        /// 1. 已购去广告 → 零广告;
        /// 2. 新用户前 N 局不弹插屏(N 默认 3);
        /// 3. 插屏局间至少间隔 4~6 分钟(取随机值,避免可预测节奏)。
        /// </remarks>
        void ShowInterstitial();
    }

    /// <summary>
    /// 内购服务接口:去广告(非消耗型商品)。
    /// </summary>
    public interface IIapService
    {
        bool IsInitialized { get; }
        bool IsRemoveAdsPurchased { get; }

        /// <summary>新购成功或启动恢复购买完成时触发。</summary>
        event Action PurchaseCompleted;

        void Initialize();
        void BuyRemoveAds();
        void RestorePurchases();
    }

    /// <summary>
    /// 分析服务接口:埋点事件 + 非致命错误上报。
    /// </summary>
    public interface IAnalyticsService
    {
        /// <summary>初始化分析服务(真实现为异步依赖修复,桩实现为日志)。</summary>
        void Initialize();

        void LogEvent(string eventName);
        void LogEvent(string eventName, string parameterName, object parameterValue);

        /// <summary>
        /// 多参数埋点(2026-09-13 补:单参数重载表达不了"难度+耗时+星级"这类多维事件)。
        /// 值为 null 的键会被跳过(GA4 不支持 null);参数名须符合 <see cref="AnalyticsEvents"/> 契约。
        /// </summary>
        void LogEvent(string eventName, IReadOnlyDictionary<string, object> parameters);

        void LogNonFatal(string message);
    }

    /// <summary>
    /// 音频服务接口(Phase 8 体验打磨:音频系统)。
    /// 解耦约定(11 文档):热更侧只调本接口,不关心 Addressables 异步加载与 AudioSource 池细节;
    /// 真实现为壳层 Box.Gameplay.AudioManager,AppBootstrap 创建注册。
    /// 地址约定:PlaySfx/PlayBgm 传短名(如 "click1"),内部拼 "Art/Audio/SFX/{name}" / "Art/Audio/BGM/{name}"。
    /// </summary>
    public interface IAudioService
    {
        /// <summary>初始化音频系统(创建常驻对象与音频源;随后按偏好播 BGM)。</summary>
        void Initialize();

        /// <summary>播放短音效(首次异步加载并缓存,此后零延迟;音效开关关闭时静默跳过)。</summary>
        void PlaySfx(string name);

        /// <summary>停止指定音效(池中正在播该音效的源才停;若异步加载尚未完成,加载完成后放弃本次播放)。
        /// 用途:「一响一动作」纪律 —— 音效时长不得超过所配音的动作,动作/动画结束时主动掐断,
        /// 防"动作已停、声音还在响"的拖尾(如倒水音须在倒完水那帧结束)。</summary>
        void StopSfx(string name);

        /// <summary>暂停 BGM(保留播放进度;音效不受影响)。玩法需要静默的场景
        /// (如对局内不放大厅音乐的水排序)进入时调用,退出后 ResumeBgm 原位续播。</summary>
        void PauseBgm();

        /// <summary>恢复被 PauseBgm 暂停的 BGM(与 PauseBgm 配对;音乐开关关闭时不强播,尊重偏好)。</summary>
        void ResumeBgm();

        /// <summary>切换 BGM 并循环播放(音乐开关关闭时只缓存不播,开启后续播)。</summary>
        void PlayBgm(string name);

        /// <summary>设置音效开关:写偏好(PlayerPrefs)并即时生效。</summary>
        void SetSoundEnabled(bool enabled);

        /// <summary>设置音乐开关:写偏好并即时生效(关闭即停,再开续播)。</summary>
        void SetMusicEnabled(bool enabled);
    }

    /// <summary>
    /// 音效短名常量(与 IAudioService 配套):统一收敛魔法字符串。
    /// 各层(UIKit 统一点击音/玩法层播放点)引用本类常量,改名只改一处。
    /// 地址约定:无前缀 = 公共 SFX(Art/Audio/SFX/{短名},Kenney UI Pack);
    /// "mod:" 前缀 = 模块独有音效,AudioManager 路由到对应模块目录(Sudoku/Audio/{短名})。
    /// 2026-08-30 资源归属分离:玩法独有音效(switch1/4/38)已随模块资源移入 Module_Sudoku 组。
    /// </summary>
    public static class AudioSfx
    {
        /// <summary>通用按钮/选格点击(公共)。</summary>
        public const string Click = "click1";

        /// <summary>填数(含清格/笔记切换,模块独有)。</summary>
        public const string Place = "mod:switch1";

        /// <summary>擦除(模块独有)。</summary>
        public const string Erase = "mod:switch4";

        /// <summary>提示落子(轻音,公共)。</summary>
        public const string Hint = "rollover1";

        /// <summary>胜利(临时占位,待正式 fanfare 替换,见 TODO;模块独有)。</summary>
        public const string Win = "mod:switch38";

        /// <summary>水排序:点试管抬起(选中;pick.ogg,2026-09-07 正式音效替换 switch34 系)。
        /// 注意 Addressables key 区分大小写,模块段须与注册地址 WaterSort/Audio/... 同大小写。</summary>
        public const string WaterPick = "mod:WaterSort/pick";

        /// <summary>水排序:点自己取消选中 / 点目标管放下(drop.ogg;替换 rollover6 系)。</summary>
        public const string WaterDrop = "mod:WaterSort/drop";

        /// <summary>水排序:倒水动画音(pour.wav;替换临时占位 pour1)。</summary>
        public const string WaterPour = "mod:WaterSort/pour";

        /// <summary>水排序:某管被倒满凑齐一管(tube_full.wav,原 finish.wav 语义化改名 2026-09-07;
        /// 与倒水动画「液面到顶」同帧响,见 WaterSortTubeRack.PlayPourAsync)。</summary>
        public const string WaterTubeFull = "mod:WaterSort/tube_full";

        /// <summary>水排序:过关结算胜利乐(win.wav,原「结算页面.wav」改名 2026-09-07;
        /// 与胜利弹层撒花彩带同刻起播,见 PlayWinThenAdvanceAsync)。</summary>
        public const string WaterWin = "mod:WaterSort/win";
    }

    /// <summary>
    /// D-7 存档 box.commerce 分区数据(Phase 7 7-1):去广告购买状态持久化。
    /// 15 号文档要求去广告状态写入存档分区(不再用 Stub 的 PlayerPrefs 键),
    /// 由 Ads/Iap 真实现通过 SaveService.GetModule/SetModule("box.commerce") 读写。
    /// </summary>
    [Serializable]
    public sealed class CommerceData
    {
        /// <summary>是否已购去广告(非消耗型商品,购买后永久有效)。</summary>
        public bool RemoveAdsPurchased;

        /// <summary>最后更新时间的 Unix 秒(排查/调试用)。</summary>
        public long UpdatedAtUnixSec;
    }
}
