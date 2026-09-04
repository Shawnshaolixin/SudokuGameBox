using System;
using UnityEngine;

namespace Box.Services
{
    /// <summary>
    /// 插屏广告频控(M3.2 全局化,WS-12「频控全局共享、连关路径不插屏」)。
    /// 规则:去广告用户零插屏(由 IAdsService.IsAdsRemoved 外层拦截);
    /// 新用户前 N 局不弹插屏;两次插屏间隔 4~6 分钟(随机,避免可预测节奏)。
    /// 计数与展示解耦两阶段:
    ///   NotifyLevelCompleted —— 每完成一局(过关)调用。数独/水排序任一玩法过关都累计(全局共享),
    ///                           连关路径也照常计数——「前 N 局保护」按真实完成局数计;
    ///   CanShowInterstitial / OnInterstitialShown —— 只在「过关 → 返回选关/大厅」类局间出口判定与落间隔。
    /// 参数(前 N 局 / 间隔区间)默认表在 AdFrequencySettings(覆写层整体替换即生效)。
    /// 说明:频控是"广告投放节奏"运行时状态,不是玩家存档数据,因此用 PlayerPrefs 记录;
    /// 去广告状态才走 D-7 存档分区(box.commerce),见 IAdsService 相关实现。
    /// 键名 v2:sudoku.ads.* → box.ads.*(全局语义,不再绑数独玩法);首次构造自动迁移旧值,
    /// 避免老用户「前 3 局保护 / 局间隔」判定随升级重置。
    /// </summary>
    public sealed class AdFrequencyController
    {
        private const string KeyGamesPlayed = "box.ads.gamesPlayed";
        private const string KeyNextAllowedAt = "box.ads.nextInterstitialAllowedAt";
        // 旧键(v1,数独专属前缀):仅迁移频控两键;去广告键不在此列(去广告走 D-7 box.commerce 分区)
        private const string LegacyGamesPlayed = "sudoku.ads.gamesPlayed";
        private const string LegacyNextAllowedAt = "sudoku.ads.nextInterstitialAllowedAt";

        public AdFrequencyController() => MigrateLegacyKeys();

        /// <summary>累计完成的对局数(前 N 局保护判定;box.ads.gamesPlayed)。</summary>
        public int GamesPlayed => PlayerPrefs.GetInt(KeyGamesPlayed, 0);

        /// <summary>过关计数入口(玩法层每完成一局调用;只计数不做展示判定,见类注释)。</summary>
        public void NotifyLevelCompleted()
        {
            PlayerPrefs.SetInt(KeyGamesPlayed, GamesPlayed + 1);
            PlayerPrefs.Save();
        }

        /// <summary>当前是否允许展示插屏:已过前 N 局,且距上次展示达到随机间隔(参数读 AdFrequencySettings)。</summary>
        public bool CanShowInterstitial()
        {
            bool passedFirstLevels = GamesPlayed >= AdFrequencySettings.NoInterstitialFirstLevels;
            bool intervalElapsed = NowUnix() >= PlayerPrefs.GetInt(KeyNextAllowedAt, 0);
            return passedFirstLevels && intervalElapsed;
        }

        /// <summary>插屏成功展示后调用,记录下次允许时间 = 当前 + [Min, Max] 随机间隔。</summary>
        public void OnInterstitialShown()
        {
            int nextAllowed = (int)(NowUnix() + UnityEngine.Random.Range(
                AdFrequencySettings.MinIntervalSec, AdFrequencySettings.MaxIntervalSec + 1));
            PlayerPrefs.SetInt(KeyNextAllowedAt, nextAllowed);
            PlayerPrefs.Save();
        }

        /// <summary>旧键(sudoku.ads.*)→ 新键(box.ads.*)一次性迁移:新键存在则旧键直接丢弃,迁移幂等。</summary>
        private static void MigrateLegacyKeys()
        {
            MigrateOne(KeyGamesPlayed, LegacyGamesPlayed);
            MigrateOne(KeyNextAllowedAt, LegacyNextAllowedAt);
            PlayerPrefs.Save();
        }

        private static void MigrateOne(string current, string legacy)
        {
            if (!PlayerPrefs.HasKey(current) && PlayerPrefs.HasKey(legacy))
                PlayerPrefs.SetInt(current, PlayerPrefs.GetInt(legacy, 0)); // 仅新键缺席时平迁旧值
            if (PlayerPrefs.HasKey(legacy)) PlayerPrefs.DeleteKey(legacy);   // 迁完即清,防二次污染
        }

        private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
