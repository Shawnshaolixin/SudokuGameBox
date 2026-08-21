using System.Collections.Generic;
using System.Threading;
using Box.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.UI.Tests
{
    /// <summary>
    /// 返回预置克隆;按 key 优先取 Prefabs,未注册回退 Prefab。
    /// Prefab 为 null 时返回 null(模拟资源缺失),ThrowOnLoad 模拟加载异常。
    /// </summary>
    public sealed class FakeLoader : IViewLoader
    {
        public readonly Dictionary<string, GameObject> Prefabs = new();
        public GameObject Prefab;
        public int LoadCount;
        public bool ThrowOnLoad;

        /// <summary>实例化后、返回前的回调(模拟真实 prefab 的序列化字段——克隆体不拷贝 [CompilerGenerated] backing field)。</summary>
        public System.Action<UIView> OnInstantiated;

        public UniTask<GameObject> LoadAsync(string key, CancellationToken ct = default)
        {
            LoadCount++;
            if (ThrowOnLoad) throw new System.InvalidOperationException("模拟加载异常");
            var source = Prefabs.TryGetValue(key, out var p) ? p : Prefab;
            if (source == null) return UniTask.FromResult<GameObject>(null);
            var go = Object.Instantiate(source);
            OnInstantiated?.Invoke(go.GetComponent<UIView>());
            return UniTask.FromResult(go);
        }
    }
}
