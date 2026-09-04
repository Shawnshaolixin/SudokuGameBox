using System;
using System.Collections.Generic;
using Box.Services;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WaterSort.Core;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 常规题库包(JSON 文件顶层包装:JsonUtility 不支持裸数组,故外包一层)。
    /// 生成器(M1.5/数据任务)与运行时共用本结构,id 即关号(1 起,升序连续,见 WaterSortProgressStore 约定)。
    /// </summary>
    [Serializable]
    public sealed class WaterSortLevelPack
    {
        /// <summary>关卡列表(id 升序;运行时按 id 线性查找,量级 ≤ 数百)。</summary>
        public List<WaterSortLevelData> levels = new List<WaterSortLevelData>();
    }

    /// <summary>
    /// 常规题库加载(TextAsset 进 Game_WaterSort 组,地址 WS-20 约定 WaterSort/Levels/regular_levels.json)。
    /// IAssetService 为回调式(热更侧禁止直连 Addressables,11 文档 §3.3),用 UniTaskCompletionSource 桥接;
    /// 包体积小(≤ 数百 KB)一次加载常驻缓存,重复进入零加载。
    /// 失败/损坏返回 null,界面 toast 提示(常规关缺失属构建期错误,理论上不出现)。
    /// </summary>
    public static class WaterSortLevelStore
    {
        public const string LevelsAddress = "WaterSort/Levels/regular_levels.json";

        static WaterSortLevelPack _pack;                       // 加载成功后的缓存
        static UniTaskCompletionSource<WaterSortLevelPack> _loading; // 在途请求(并发去重,同帧多处请求共享一次)

        /// <summary>异步取题库(首次经 Addressables 加载并缓存;失败后允许重试)。</summary>
        public static async UniTask<WaterSortLevelPack> LoadPackAsync()
        {
            if (_pack != null) return _pack; // 已缓存:直出,零开销
            if (_loading != null) return await _loading.Task; // 并发去重

            var tcs = new UniTaskCompletionSource<WaterSortLevelPack>();
            _loading = tcs;
            var svc = ServiceLocator.Assets;
            if (svc == null)
            {
                tcs.TrySetResult(null); // 服务未注册(异常上下文)
            }
            else
            {
                svc.LoadAsset<TextAsset>(LevelsAddress, ta =>
                {
                    WaterSortLevelPack pack = null;
                    if (ta != null)
                    {
                        try
                        {
                            pack = JsonUtility.FromJson<WaterSortLevelPack>(ta.text);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[WaterSort] 题库 JSON 解析失败: {LevelsAddress} {e.Message}");
                        }
                    }
                    if (pack == null || pack.levels == null || pack.levels.Count == 0)
                        Debug.LogWarning($"[WaterSort] 题库缺失或为空: {LevelsAddress}");
                    else
                        _pack = pack; // 只缓存有效包;失败留空,下次调用重试
                    tcs.TrySetResult(pack);
                });
            }
            return await tcs.Task;
        }

        /// <summary>按关号取关;未找到返回 null(调用方判定越界)。</summary>
        public static WaterSortLevelData FindById(WaterSortLevelPack pack, int id)
        {
            if (pack?.levels == null) return null;
            for (int i = 0; i < pack.levels.Count; i++)
                if (pack.levels[i].id == id) return pack.levels[i];
            return null;
        }
    }
}
