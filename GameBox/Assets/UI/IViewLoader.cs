using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// 异步 prefab 加载抽象(11 文档 §3.5 第 4 条)。
    /// Phase 3 由 Resources 实现兜底;Phase 6 接入 Addressables 后提供 AddressablesViewLoader,
    /// UIKit 与调用方零改动。
    /// 约定:key 即 prefab 资源路径(如 "UI/Popups/SettingsPopup")。
    /// </summary>
    public interface IViewLoader
    {
        UniTask<GameObject> LoadAsync(string key, CancellationToken ct = default);
    }

    /// <summary>
    /// Resources 实现:加载 + 实例化;超时兜底返回 null,不抛到业务层。
    /// </summary>
    public sealed class ResourceViewLoader : IViewLoader
    {
        const float TimeoutSec = 10f;

        public async UniTask<GameObject> LoadAsync(string key, CancellationToken ct = default)
        {
            try
            {
                var op = Resources.LoadAsync<GameObject>(key);
                var (loadDone, result) = await UniTask.WhenAny(
                    op.ToUniTask().AttachExternalCancellation(ct),
                    UniTask.Delay(TimeSpan.FromSeconds(TimeoutSec), cancellationToken: ct));
                if (!loadDone || result == null)
                {
                    Debug.LogWarning($"[UIKit] 加载超时或资源缺失: {key}");
                    return null;
                }
                return UnityEngine.Object.Instantiate((GameObject)result);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UIKit] 加载失败 {key}: {e.Message}");
                return null;
            }
        }
    }
}
