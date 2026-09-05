using System;
using System.Collections.Generic;
using Box.Services;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Box.Gameplay
{
    /// <summary>
    /// IAssetService 的 Addressables 壳层实现(Phase 6,11 文档 §3.3:Addressables 只在壳层,热更侧经接口使用)。
    /// 维护 address → handle 映射以便 Release;同一 address 重复加载时后一次覆盖前一次句柄。
    /// 失败/缺失:回调 null + Debug.LogWarning,不抛到业务层。
    /// </summary>
    public sealed class AddressablesAssetService : IAssetService
    {
        private readonly Dictionary<string, AsyncOperationHandle> _handles =
            new Dictionary<string, AsyncOperationHandle>();

        public void LoadAsset<T>(string address, Action<T> onLoaded) where T : class
        {
            // 契约:失败/缺失必须回调 null,不抛到业务层(类头)。无效 key(未入组/产物目录缺失,
            // 如 PlayMode=Use Existing Build 而条目是后注册的)会令 LoadAssetAsync 在返回前
            // 同步抛 InvalidKeyException —— 必须在壳层边界兜住,否则会打断调用方流程(WaterSort 曾现)。
            AsyncOperationHandle handle;
            try
            {
                handle = Addressables.LoadAssetAsync<T>(address);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Assets] 加载失败(地址无效): {address} {e.Message}");
                onLoaded?.Invoke(null);
                return;
            }
            handle.Completed += op =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogWarning($"[Assets] 加载失败: {address}");
                    onLoaded?.Invoke(null);
                    return;
                }
                _handles[address] = op;
                onLoaded?.Invoke(op.Result as T);
            };
        }

        public void Instantiate(string address, Action<object> onLoaded)
        {
            // 同 LoadAsset:无效 key 同步抛异常须兜住(InstantiateAsync 无 location 时同样抛)
            AsyncOperationHandle handle;
            try
            {
                handle = Addressables.InstantiateAsync(address);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Assets] 实例化失败(地址无效): {address} {e.Message}");
                onLoaded?.Invoke(null);
                return;
            }
            handle.Completed += op =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogWarning($"[Assets] 实例化失败: {address}");
                    onLoaded?.Invoke(null);
                    return;
                }
                _handles[address] = op;
                onLoaded?.Invoke(op.Result);
            };
        }

        public void Release(string address)
        {
            if (!_handles.TryGetValue(address, out var handle)) return;
            Addressables.Release(handle);
            _handles.Remove(address);
        }
    }
}