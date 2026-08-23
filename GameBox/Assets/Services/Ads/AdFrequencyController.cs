using System;
using UnityEngine;

namespace Box.Services
{
    /// <summary>
    /// 插屏广告频控（04 号文档 §广告频控）。
    /// 规则：去广告用户零插屏（由 IAdsService.IsAdsRemoved 外层拦截）；
    /// 新用户前 3 局不弹插屏；两次插屏间隔 4~6 分钟（随机，避免可预测节奏）。
    /// 说明：频控是"广告投放节奏"运行时状态，不是玩家存档数据，因此用 PlayerPrefs 记录；
    /// 去广告状态才走 D-7 存档分区（box.commerce），见 IAdsService 相关实现。
    /// </summary>
    public sealed class AdFrequencyController
    {
        // 与 Stub 共用同一键，保证接入真实现后计数无缝延续（迁移友好）
        private const string KeyGamesPlayed = "sudoku.ads.gamesPlayed";
        // 下次允许展示插屏的时间戳（Unix 秒），展示成功后写入
        private const string KeyNextAllowedAt = "sudoku.ads.nextInterstitialAllowedAt";

        private const int NoInterstitialFirstLevels = 3;  // 新用户前 3 局不弹插屏
        private const int MinIntervalSec = 4 * 60;        // 局间隔下限：4 分钟
        private const int MaxIntervalSec = 6 * 60;        // 局间隔上限：6 分钟

        /// <summary>累计完成的对局数（用于"前 3 局"判定）。</summary>
        public int GamesPlayed => PlayerPrefs.GetInt(KeyGamesPlayed, 0);

        /// <summary>
        /// 当前是否允许展示插屏：已过前 3 局，且距上次展示达到随机间隔。
        /// </summary>
        public bool CanShowInterstitial()
        {
            bool passedFirstLevels = GamesPlayed >= NoInterstitialFirstLevels;
            bool intervalElapsed = NowUnix() >= PlayerPrefs.GetInt(KeyNextAllowedAt, 0);
            return passedFirstLevels && intervalElapsed;
        }

        /// <summary>一局结束（插屏候选点）时调用，累计对局数。</summary>
        public void OnLevelEnded()
        {
            PlayerPrefs.SetInt(KeyGamesPlayed, GamesPlayed + 1);
            PlayerPrefs.Save();
        }

        /// <summary>插屏成功展示后调用，记录下次允许时间 = 当前 + 4~6 分钟随机值。</summary>
        public void OnInterstitialShown()
        {
            int nextAllowed = (int)(NowUnix() + UnityEngine.Random.Range(MinIntervalSec, MaxIntervalSec + 1));
            PlayerPrefs.SetInt(KeyNextAllowedAt, nextAllowed);
            PlayerPrefs.Save();
        }

        private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}