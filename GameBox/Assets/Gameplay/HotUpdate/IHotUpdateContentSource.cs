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

        /// <summary>加载 AOT 程序集元数据字节(按 AOT 程序集名,如 Box.UI/mscorlib;Consistent 模式要求与包内剥离 dll 逐字节一致,无资源返回 null)。</summary>
        UniTask<byte[]> LoadMetadataAsync(string aotAssemblyName);

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

        /// <summary>装载单个 AOT 程序集元数据(LoadMetadataForAOTAssembly,Consistent 模式);失败返回 false。</summary>
        bool LoadMetadata(string aotAssemblyName, byte[] metadataBytes);

        /// <summary>装载热更程序集(Assembly.Load,元数据全部装载完成后调用);失败返回 false。</summary>
        bool LoadAssembly(string assemblyName, byte[] dllBytes);
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

        /// <summary>
        /// 远程加载路径的 profile 变量名(与编辑器 Remote.LoadPath="{RemoteHostURL}/[BuildTarget]" 对应)。
        /// 真机踩坑:Addressables 远程 bundle 的变量表在设备端求值,构建值不烘焙进 catalog;
        /// 不显式设置时占位符原样输出("RemoteHostURL/Android/...bundle" 加载失败)。
        /// </summary>
        public const string RemoteHostVariableName = "RemoteHostURL";

        /// <summary>
        /// 远程内容服务器地址。双通道 Firebase Hosting 布局(2026-09-02 拍板,见 tools/deploy_firebase.ps1):
        ///   https://sudokugamebox.web.app/staging    ← 开发/真机验证(dev APK 指这里,本常量)
        ///   https://sudokugamebox.web.app/production ← 上架内容(发布构建注入,仓库保持 staging/dev 值,红线 9)
        /// 每个通道下目录结构相同:Android/(Addressables 契约目录,catalog 内 id 烘焙为 /Android/) + manifest/(预留)。
        /// 内容先上 staging → 真机验收 → 验收通过后 deploy_firebase.ps1 -Env production 提升。
        /// 离网/局域网联调回退:改回 "http://192.168.1.100:8000"(deploy_remote.ps1 起本机服务)——真机坑:光猫按网段隔离
        /// (手机 192.168.1.x / 电脑 192.168.0.x 不通),需给电脑 WLAN 加同段别名 IP:netsh interface ipv4 add address
        /// "WLAN" 192.168.1.100 255.255.255.0 store=persistent;改回 http 后真机须卸载重装(UnityWebRequest 缓存)。
        /// 缓存头(firebase.json):.bin/.hash no-cache(每次启动拿最新),bundle 内容寻址 immutable。
        /// </summary>
#if BOX_REMOTE_PRODUCTION
        // 生产通道:符号由 BuildScript.PrepareV11 依环境变量 BOX_REMOTE_URL 注入(与 BOX_KEYSTORE_PASS 同款范式),
        // BuildV11 收尾自动移除 —— 仓库默认分支保持 staging(红线 9),玩家包经发布流程注入后指向生产
        public const string RemoteServerUrl = "https://sudokugamebox.web.app/production";
#else
        public const string RemoteServerUrl = "https://sudokugamebox.web.app/staging";
#endif

        /// <summary>
        /// Addressables 2.x 无 SetProfileVariable(1.x API 已移除),远程 URL 改写走
        /// InternalIdTransformFunc 钩子:每次解析资源 id 时把 {RemoteHostURL} 占位符替换为实际服务器地址。
        /// 必须在任何远程请求前设置(构造函数即装,覆盖 catalog 与 bundle 全部远程加载)。
        /// </summary>
        public AddressablesHotUpdateSource()
        {
            Addressables.InternalIdTransformFunc = location =>
                location.InternalId.Replace(RemoteHostVariableName, RemoteServerUrl);
        }

        /// <summary>catalog 更新失败/异常一律视为"无更新可用",调用方继续走包内版本。</summary>
        public async UniTask<bool> TryUpdateCatalogAsync()
        {
            try
            {
                // 确保远程地址改写已生效(构造函数已装,此处兜底防外部覆盖)
                Addressables.InternalIdTransformFunc = location =>
                    location.InternalId.Replace(RemoteHostVariableName, RemoteServerUrl);
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
    /// 9-4 起语义与真实 HybridCLR 对齐:LoadMetadata 按 AOT 程序集逐个装载(Consistent 模式),
    /// 全部元数据就绪后再 Assembly.Load 热更程序集。
    /// </summary>
    public sealed class HybridCLRAssemblyLoader : IHotUpdateAssemblyLoader
    {
        const string RuntimeApiTypeName = "HybridCLR.RuntimeApi, HybridCLR.Runtime";

        public bool IsRuntimeAvailable => Type.GetType(RuntimeApiTypeName) != null;

        public bool LoadMetadata(string aotAssemblyName, byte[] metadataBytes)
        {
            try
            {
                var runtimeApi = Type.GetType(RuntimeApiTypeName);
                if (runtimeApi == null) return false;
                if (metadataBytes == null || metadataBytes.Length == 0) return false;

                var loadMetadata = runtimeApi.GetMethod("LoadMetadataForAOTAssembly");
                if (loadMetadata == null) return false;
                var modeType = loadMetadata.GetParameters()[1].ParameterType;
                var consistent = Enum.Parse(modeType, "Consistent");
                loadMetadata.Invoke(null, new object[] { metadataBytes, consistent });
                Debug.Log($"[HotUpdate] AOT 元数据已装载: {aotAssemblyName}");
                return true;
            }
            catch (Exception e)
            {
                // 完整异常链(含反射 Invoke 内层,真机 2026-09-02 遇过 TargetInvocationException 吞详情)
                Debug.LogWarning($"[HotUpdate] 装载 AOT 元数据 {aotAssemblyName} 失败: {e}");
                return false;
            }
        }

        public bool LoadAssembly(string assemblyName, byte[] dllBytes)
        {
            try
            {
                // Assembly.Load 后热更类型进入 AppDomain,ModuleLoader.ResolveType 可命中
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
