using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Box.Gameplay.HotUpdate
{
    /// <summary>
    /// 热更内容源抽象(Phase 9 9-3):下载层能力的最小接口。
    /// EditMode 测试注入 mock 验证编排逻辑;真机/编辑器走 AddressablesHotUpdateSource。
    /// 约定:任一方法"无内容"返回 null/false,不抛业务异常 —— 调用方静默降级包内版本。
    /// </summary>
    public interface IHotUpdateContentSource
    {
        /// <summary>检查并应用远程 catalog 更新;无更新或成功返回 true,网络失败返回 false。</summary>
        UniTask<bool> TryUpdateCatalogAsync();

        /// <summary>加载热更程序集 dll 字节(按程序集名,无资源返回 null)。</summary>
        UniTask<byte[]> LoadDllAsync(string assemblyName);

        /// <summary>加载 AOT 元数据字节(Consistent 模式要求与包内剥离 dll 逐字节一致;无资源返回 null)。</summary>
        UniTask<byte[]> LoadMetadataAsync(string assemblyName);

        /// <summary>加载远程模块清单 JSON;无资源返回 null。</summary>
        UniTask<string> LoadOverridesJsonAsync();

        /// <summary>清理内容缓存(Assembly.Load 失败时调用,下轮启动重试)。</summary>
        UniTask ClearCacheAsync();
    }

    /// <summary>
    /// 热更程序集装载器抽象(9-3):反射探测 HybridCLR 运行时 + 装载 dll。
    /// 测试注入 fake 避免 Editor 下真 Assembly.Load(Editor 为 Mono 且同名程序集已加载)。
    /// </summary>
    public interface IHotUpdateAssemblyLoader
    {
        /// <summary>HybridCLR 运行时是否可用(v1.0 主包无 RuntimeApi → false,整链静默跳过)。</summary>
        bool IsRuntimeAvailable { get; }

        /// <summary>装载元数据 + 程序集;任一步失败返回 false(不抛,调用方清缓存)。</summary>
        bool Load(string assemblyName, byte[] dllBytes, byte[] metadataBytes);
    }

    /// <summary>
    /// Addressables 内容源(9-3 默认实现)。
    /// 地址约定(v1.1 资源组 HotUpdate_Local,9-4 配置):HotUpdate/Dll/{程序集名}、HotUpdate/Metadata/{程序集名}、HotUpdate/module_overrides。
    /// 资源缺失时 Addressables 句柄失败 → 捕获 → null(触发调用方降级),不阻塞启动。
    /// </summary>
    public sealed class AddressablesHotUpdateSource : IHotUpdateContentSource
    {
        public const string DllAddressPrefix = "HotUpdate/Dll/";
        public const string MetadataAddressPrefix = "HotUpdate/Metadata/";
        public const string OverridesAddress = "HotUpdate/module_overrides";

        /// <summary>catalog 更新失败/异常一律视为"无更新可用",调用方继续走包内版本。</summary>
        public async UniTask<bool> TryUpdateCatalogAsync()
        {
            try
            {
                var catalogs = await AwaitHandleAsync(Addressables.CheckForCatalogUpdates());
                if (catalogs == null || catalogs.Count == 0) return true;
                await AwaitHandleAsync(Addressables.UpdateCatalogs(catalogs));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HotUpdate] catalog 检查失败(降级包内版本): {e.Message}");
                return false;
            }
        }

        public async UniTask<byte[]> LoadDllAsync(string assemblyName) =>
            await LoadTextAssetBytesAsync(DllAddressPrefix + assemblyName);

        public async UniTask<byte[]> LoadMetadataAsync(string assemblyName) =>
            await LoadTextAssetBytesAsync(MetadataAddressPrefix + assemblyName);

        public async UniTask<string> LoadOverridesJsonAsync()
        {
            var handle = Addressables.LoadAssetAsync<TextAsset>(OverridesAddress);
            try
            {
                var asset = await AwaitHandleAsync(handle);
                return asset != null ? asset.text : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HotUpdate] 加载 {OverridesAddress} 失败(保持包内清单): {e.Message}");
                return null;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        /// <summary>统一加载 TextAsset 字节;读取后立即释放句柄(一次性数据,不驻留)。</summary>
        static async UniTask<byte[]> LoadTextAssetBytesAsync(string address)
        {
            var handle = Addressables.LoadAssetAsync<TextAsset>(address);
            try
            {
                var asset = await AwaitHandleAsync(handle);
                return asset != null ? asset.bytes : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HotUpdate] 加载 {address} 失败(降级包内版本): {e.Message}");
                return null;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        public async UniTask ClearCacheAsync()
        {
            try
            {
                await AwaitHandleAsync(Addressables.ClearDependencyCacheAsync("", false));
                Debug.Log("[HotUpdate] 依赖缓存已清除,下轮启动重试");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HotUpdate] 清缓存失败(忽略): {e.Message}");
            }
        }

        /// <summary>
        /// AsyncOperationHandle → UniTask 转换(回调式,不依赖 UniTask 包的 Addressables 扩展版本)。
        /// 失败抛 OperationException,由调用方 catch 降级。
        /// </summary>
        static UniTask<T> AwaitHandleAsync<T>(AsyncOperationHandle<T> handle)
        {
            var tcs = new UniTaskCompletionSource<T>();
            handle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded) tcs.TrySetResult(op.Result);
                else tcs.TrySetException(op.OperationException ?? new Exception($"Addressables 操作失败: {op.Status}"));
            };
            return tcs.Task;
        }

        /// <summary>非泛型句柄版本(UpdateCatalogs/ClearDependencyCacheAsync 等无返回值操作)。</summary>
        static UniTask AwaitHandleAsync(AsyncOperationHandle handle)
        {
            var tcs = new UniTaskCompletionSource();
            handle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded) tcs.TrySetResult();
                else tcs.TrySetException(op.OperationException ?? new Exception($"Addressables 操作失败: {op.Status}"));
            };
            return tcs.Task;
        }
    }

    /// <summary>
    /// HybridCLR 运行时装载器(9-3 默认实现):全反射,禁止编译期引用 HybridCLR.Runtime
    /// (v1.0 主包无该程序集,直接引用会被 IL2CPP 裁剪 → MissingMethod 崩溃)。
    /// 反射探测成功后才执行;HomologousImageMode 枚举经方法参数类型解析,不硬编码命名空间/数值。
    /// </summary>
    public sealed class HybridCLRAssemblyLoader : IHotUpdateAssemblyLoader
    {
        const string RuntimeApiTypeName = "HybridCLR.RuntimeApi, HybridCLR.Runtime";

        public bool IsRuntimeAvailable => Type.GetType(RuntimeApiTypeName) != null;

        public bool Load(string assemblyName, byte[] dllBytes, byte[] metadataBytes)
        {
            try
            {
                var runtimeApi = Type.GetType(RuntimeApiTypeName);
                if (runtimeApi == null) return false;

                // 1) AOT 元数据装载(Consistent 严格一致模式)—— 先元数据后 dll,顺序不可反
                if (metadataBytes != null && metadataBytes.Length > 0)
                {
                    var loadMetadata = runtimeApi.GetMethod("LoadMetadataForAOTAssembly");
                    if (loadMetadata == null) return false;
                    var modeType = loadMetadata.GetParameters()[1].ParameterType;
                    var consistent = Enum.Parse(modeType, "Consistent");
                    loadMetadata.Invoke(null, new object[] { metadataBytes, consistent });
                }

                // 2) 程序集装载(Assembly.Load 后热更类型进入 AppDomain,ModuleLoader.ResolveType 可命中)
                Assembly.Load(dllBytes);
                Debug.Log($"[HotUpdate] 程序集已装载: {assemblyName}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HotUpdate] 装载程序集 {assemblyName} 失败: {e.Message}");
                return false;
            }
        }
    }
}
