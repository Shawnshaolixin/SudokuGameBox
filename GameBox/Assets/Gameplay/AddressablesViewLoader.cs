using System;
using System.Threading;
using Box.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Box.Gameplay
{
    /// <summary>
    /// IViewLoader 的 Addressables 实现(Phase 6,IViewLoader 注释约定:UIKit 与调用方零改动)。
    /// key 即 Addressables 地址(迁移后的 UI prefab 均以 "UI/xxx" 为地址)。
    /// 失败/缺失返回 null,不抛到业务层;成功时实例化副本返回。
    /// </summary>
    public sealed class AddressablesViewLoader : IViewLoader
    {
        const float TimeoutSec = 10f;

        public async UniTask<GameObject> LoadAsync(string key, CancellationToken ct = default)
        {
            try
            {
                var handle = Addressables.LoadAssetAsync<GameObject>(key);
                var (loadDone, asset) = await UniTask.WhenAny(
                    handle.Task.AsUniTask().AttachExternalCancellation(ct),
                    UniTask.Delay(TimeSpan.FromSeconds(TimeoutSec), cancellationToken: ct));
                if (!loadDone || asset == null)
                {
                    Debug.LogWarning($"[UIKit] 加载超时或资源缺失: {key}");
                    return null;
                }
                return UnityEngine.Object.Instantiate(asset);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UIKit] 加载失败 {key}: {e.Message}");
                return null;
            }
        }
    }
}