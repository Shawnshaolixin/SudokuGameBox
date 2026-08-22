using System;

namespace Box.Services
{
    /// <summary>
    /// 存档服务接口(11 文档 §8.1 D-7 统一 SaveService)。
    /// AOT 程序集(Box.Services.Abstractions):热更侧唯一可引用的服务程序集(11 文档 §4.4)。
    /// 职责边界:模块只读写自己的 modules.&lt;id&gt; 分区;box.* 仅 Shell/Economy 可写(D-5)。
    /// 实现(壳层 Box.Gameplay.SaveService)负责:JSON + AES-CBC/HMAC 加密 + 原子写 + 备份 + 异常恢复。
    /// </summary>
    public interface ISaveService
    {
        /// <summary>当前写入的 schema 版本(§8.2 单调递增,客户端只升不降;当前固定 1)。</summary>
        int SchemaVersion { get; }

        /// <summary>存档文件是否存在(首次创建/迁移完成后为 true)。</summary>
        bool Exists { get; }

        /// <summary>存档文件路径(诊断/测试用)。</summary>
        string FilePath { get; }

        // ---- box.*(仅 Shell/Economy 可写;D-5 单货币) ----

        /// <summary>全盒子唯一货币余额。</summary>
        long Coins { get; set; }

        /// <summary>首次安装时间(UTC ISO8601,首次创建 v1 档时写入,此后不再变)。</summary>
        string InstalledAt { get; }

        /// <summary>上次游玩模块 id(大厅入口写,交叉导量恢复用)。</summary>
        string LastModuleId { get; set; }

        /// <summary>读每日签到记录;无记录返回 false。</summary>
        bool TryGetSignin(out string lastDate, out int streak);

        /// <summary>写每日签到记录(仅 Shell 可写)。</summary>
        void SetSignin(string lastDate, int streak);

        // ---- modules.&lt;id&gt; 分区(模块只读写自己的分区) ----

        /// <summary>读模块分区数据;无记录时返回默认实例 T。</summary>
        T GetModule<T>(string moduleId) where T : class, new();

        /// <summary>写模块分区数据(立即加密落盘)。</summary>
        void SetModule<T>(string moduleId, T data) where T : class;

        /// <summary>立即把内存数据加密落盘(box.* 变更后调用;SetModule 内部已自动落盘,显式再调仅强化)。</summary>
        void Save();
    }

    /// <summary>
    /// 偏好设置服务接口(11 文档 §8.1:「PlayerPrefs 只留音量/语言等偏好」)。
    /// 音量/语言/主题属于全局偏好,不进加密存档;实现用 PlayerPrefs(壳层 Box.Gameplay.SettingsService)。
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>音效开关。</summary>
        bool SoundEnabled { get; set; }

        /// <summary>音乐开关(当前无音乐实现,先存开关,Phase 后置接音频系统)。</summary>
        bool MusicEnabled { get; set; }

        /// <summary>主题索引:0=浅色(默认) / 1=深色。</summary>
        int ThemeIndex { get; set; }

        /// <summary>语言代码:zh=中文(当前) / en=English(本地化管线后置,先存偏好)。</summary>
        string Language { get; set; }

        /// <summary>立即落盘(PlayerPrefs.Save)。</summary>
        void Save();
    }

    /// <summary>
    /// 服务定位器(静态注册,AppBootstrap 在启动引导创建并注册;测试可注入临时实例)。
    /// 放在 AOT 程序集 Abstractions,使热更侧玩法只依赖接口、不依赖壳层实现类型。
    /// ⚠️ 类名不叫 Services:会和命名空间 Box.Services 同名,编译器优先解析为命名空间
    /// (在 Box.Gameplay 等子命名空间里 Services.Save 会查 Box.Services.Save → CS0234)。
    /// 用 ServiceLocator 避开这个经典陷阱。
    /// </summary>
    public static class ServiceLocator
    {
        public static ISaveService Save { get; private set; }
        public static ISettingsService Settings { get; private set; }

        /// <summary>服务是否已注册(运行时 AppBootstrap 注册后恒为 true)。</summary>
        public static bool IsReady => Save != null && Settings != null;

        public static void Register(ISaveService save, ISettingsService settings)
        {
            Save = save;
            Settings = settings;
        }

        /// <summary>测试清理用(置空,防跨测试污染)。</summary>
        public static void Reset()
        {
            Save = null;
            Settings = null;
        }
    }
}