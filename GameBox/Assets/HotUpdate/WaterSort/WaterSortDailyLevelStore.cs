using System;
using Box.Services;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WaterSort.Core;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 每日题库加载与按日期取关(WS-09):TextAsset 进 Game_WaterSort 组,地址
    /// WaterSort/Levels/daily_levels.json(与常规题库同一 IAssetService 回调式加载 + UniTask 桥接)。
    /// 包体 ~数百 KB 一次加载常驻缓存;失败/损坏返回 null,界面降级提示(每日入口回常规选关)。
    /// 取关语义:levels 内按 id=日期种子精确命中;当日缺失/损坏 → 兜底取备用池
    /// (spares[seed % n],确定性——同日期恒取同条目,杜绝"全球同日死局"),返回 usedFallback 标记
    /// 供埋点/调试;备用池为空则返回 null(资产异常,由调用方提示)。
    /// </summary>
    public static class WaterSortDailyLevelStore
    {
        public const string DailyLevelsAddress = "WaterSort/Levels/daily_levels.json";

        static WaterSortDailyPack _pack;                       // 加载成功后的缓存
        static UniTaskCompletionSource<WaterSortDailyPack> _loading; // 在途请求(并发去重)

        /// <summary>异步取每日题库(首次经 Addressables 加载并缓存;失败后允许重试)。</summary>
        public static async UniTask<WaterSortDailyPack> LoadPackAsync()
        {
            if (_pack != null) return _pack;
            if (_loading != null) return await _loading.Task;

            var tcs = new UniTaskCompletionSource<WaterSortDailyPack>();
            _loading = tcs;
            var svc = ServiceLocator.Assets;
            if (svc == null)
            {
                tcs.TrySetResult(null); // 服务未注册(异常上下文)
            }
            else
            {
                svc.LoadAsset<TextAsset>(DailyLevelsAddress, ta =>
                {
                    WaterSortDailyPack pack = null;
                    if (ta != null)
                    {
                        try
                        {
                            pack = JsonUtility.FromJson<WaterSortDailyPack>(ta.text);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[WaterSort] 每日题库 JSON 解析失败: {DailyLevelsAddress} {e.Message}");
                        }
                    }
                    if (pack == null || pack.levels == null || pack.levels.Count == 0)
                        Debug.LogWarning($"[WaterSort] 每日题库缺失或为空: {DailyLevelsAddress}");
                    else
                        _pack = pack; // 只缓存有效包;失败留空,下次调用重试
                    tcs.TrySetResult(pack);
                });
            }
            return await tcs.Task;
        }

        /// <summary>按日期种子取关(精确命中优先,缺失/损坏兜底备用池;见类头)。</summary>
        public static WaterSortLevelData GetForSeed(WaterSortDailyPack pack, int seed, out bool usedFallback)
        {
            usedFallback = false;
            if (pack?.levels == null) return null;
            for (int i = 0; i < pack.levels.Count; i++)
                if (pack.levels[i].id == seed) return pack.levels[i];

            var spares = pack.spares;
            if (spares == null || spares.Count == 0) return null; // 无备用可兜:资产异常
            usedFallback = true;
            return spares[Math.Abs(seed % spares.Count)]; // 确定性取池(同日期恒同条目)
        }
    }
}
