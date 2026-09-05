using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace Box.Gameplay.HotUpdate
{
    /// <summary>
    /// 热更内容源抽象(Phase 9 9-3):下载层能力的最小接口。
    /// EditMode 测试注入 mock 验证编排逻辑;真机/编辑器走 AddressablesHotUpdateSource。
    /// 约定:任一方法"无内容"返回 null/false,不抛业务异常 —— 调用方静默降级包内版本。
    /// </summary>
    public interface IHotUpdateContentSource
    {
        /// <summary>
        /// 检查并应用远程 catalog 更新。true = 远程可用(继续远程装载);false = 远程不可用
        /// (实现方已自动切内置兜底源,调用方应继续走装载链,由内置 location 兜底或最终降级)。
        /// 2026-09-04 版本化部署:实现方需先解析版本指针 index.json(RemoteContentIndex)决定
        /// 本次 catalog 走哪个版本目录,解析失败沿用上次持久化版本,再走共享旧路径,详见实现。
        /// </summary>
        UniTask<bool> TryUpdateCatalogAsync();

        /// <summary>加载热更程序集 dll 字节(按程序集名,无资源返回 null)。</summary>
        UniTask<byte[]> LoadDllAsync(string assemblyName);

        /// <summary>加载 AOT 程序集元数据字节(按 AOT 程序集名,如 Box.UI/mscorlib;Consistent 模式要求与包内剥离 dll 逐字节一致,无资源返回 null)。</summary>
        UniTask<byte[]> LoadMetadataAsync(string aotAssemblyName);

        /// <summary>加载远程模块清单 JSON;无资源返回 null。</summary>
        UniTask<string> LoadOverridesJsonAsync();

        /// <summary>
        /// 通知源切换内置兜底(由编排层在总超时后触发;源内 catalog 失败自切为双保险)。
        /// 无内置副本语义的实现(如测试 mock)可为空实现。
        /// </summary>
        void UseBuiltinFallback();

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
    /// 2026-09-04 版本化部署(Phase 10-2 前置,布局见 tools/deploy_firebase.ps1 头注释与 20 文档 §11):
    /// 启动先拉 {RemoteServerUrl}/index.json 解析当前版本目录名,再经 InternalIdTransformFunc 把
    /// catalog 指向 {RemoteServerUrl}/{version}/Android/、bundle 指向共享 {RemoteServerUrl}/Android/。
    /// </summary>
    public sealed class AddressablesHotUpdateSource : IHotUpdateContentSource
    {
        public const string DllAddressPrefix = "HotUpdate/Dll/";
        public const string MetadataAddressPrefix = "HotUpdate/Metadata/";
        public const string OverridesAddress = "HotUpdate/module_overrides";

        // ===== 内置兜底地址(2026-09-03 断网空降级修复):BuiltinHotUpdate 本地组随主包内置,=====
        // 与远程内容同源同批(strip 产物副本);catalog 网络检查失败 → 自动切内置源继续装载,
        // 断网/远程不可达时玩法代码仍可装载(真"包内版本兜底",v1.1 剥离后不再裸奔)。
        public const string BuiltinDllAddressPrefix = "BuiltinHotUpdate/Dll/";
        public const string BuiltinMetadataAddressPrefix = "BuiltinHotUpdate/Metadata/";

        /// <summary>是否切到内置兜底源(进程内一次性:网络失败后本进程不再尝试远程)。</summary>
        public bool UseBuiltinContent { get; private set; }

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
        /// 2026-09-04 版本化部署后每通道内布局:
        ///   index.json                    ← 版本指针(每次发布/回滚改写,客户端启动解析)
        ///   Android/                      ← 共享层:bundle(内容寻址,全版本共享) + 旧客户端兼容 catalog(固定名双写)
        ///   {version}/Android/            ← 各版本 catalog 独立目录(回滚=指针指回,互不覆盖)
        ///   _history/{时间戳}/            ← 部署前自动归档的旧 catalog(防御性备份)
        /// 内容先上 staging → 真机验收 → 验收通过后 deploy_firebase.ps1 -Channel production 提升;
        /// 回滚 = -RollbackTo &lt;版本&gt;(只改指针,秒级生效,无需重新构建)。
        /// 离网/局域网联调回退:改回 "http://192.168.1.100:8000"(deploy_remote.ps1 起本机服务)——真机坑:光猫按网段隔离
        /// (手机 192.168.1.x / 电脑 192.168.0.x 不通),需给电脑 WLAN 加同段别名 IP:netsh interface ipv4 add address
        /// "WLAN" 192.168.1.100 255.255.255.0 store=persistent;改回 http 后真机须卸载重装(UnityWebRequest 缓存)。
        /// 缓存头(firebase.json):index.json 与 .bin/.hash no-cache(每次启动拿最新),bundle 内容寻址 immutable。
        /// </summary>
#if BOX_REMOTE_PRODUCTION
        // 生产通道:符号由 BuildScript.PrepareV11 依环境变量 BOX_REMOTE_URL 注入(与 BOX_KEYSTORE_PASS 同款范式),
        // BuildV11 收尾自动移除 —— 仓库默认分支保持 staging(红线 9),玩家包经发布流程注入后指向生产
        public const string RemoteServerUrl = "https://sudokugamebox.web.app/production";
#else
        public const string RemoteServerUrl = "https://sudokugamebox.web.app/staging";
#endif

        /// <summary>版本指针文件名(通道根下;deploy_firebase.ps1 发布/回滚时生成或改写)。</summary>
        public const string IndexFileName = "index.json";

        /// <summary>版本指针拉取超时(秒)。外层 RunAsync 对 catalog 阶段整体套 5s 总超时,这里留余量。</summary>
        public const int IndexTimeoutSeconds = 2;

        /// <summary>
        /// 上次成功解析的版本持久化键。指针拉取失败(断网/指针丢失)时沿用,保证已发布版本切换后
        /// 弱网设备不至于失联;从未成功解析过则无持久化值 → 走共享旧路径。
        /// </summary>
        public const string VersionPrefsKey = "Box.HotUpdate.LastRemoteContentVersion";

        /// <summary>
        /// 本次会话使用的远程内容版本(=服务器版本目录名)。null = 指针未解析成功(断网/首次启动即失败)
        /// → catalog 走共享旧路径(部署脚本对旧客户端的兼容目录)。会话内只写:解析成功时更新,
        /// 版本目录不可达时回退重试前清空(不消持久化值,下次启动重新解析指针)。
        /// </summary>
        public string CurrentRemoteVersion { get; private set; }

        /// <summary>
        /// Addressables 2.x 无 SetProfileVariable(1.x API 已移除),远程 URL 改写走
        /// InternalIdTransformFunc 钩子:每次解析资源 id 时把 RemoteHostURL 占位符替换为实际服务器地址。
        /// 必须在任何远程请求前设置(构造函数即装,覆盖 catalog 与 bundle 全部远程加载)。
        /// 2026-09-04 版本化:委托绑定实例方法,按文件类型与 CurrentRemoteVersion 动态拼路径
        /// (见 BuildRemoteBasePath)——指针解析完成后无需重装钩子,新版本即对后续请求生效。
        /// </summary>
        public AddressablesHotUpdateSource()
        {
            Addressables.InternalIdTransformFunc = TransformInternalId;
        }

        /// <summary>
        /// 远程 id 改写入口(InternalIdTransformFunc 委托目标)。internalId 形如
        /// "RemoteHostURL/Android/catalog_1.0.bin"(构建期烘焙,{RemoteHostURL} 占位符以裸名出现,
        /// 见 RemoteHostVariableName 注释),替换后成为完整 https URL。
        /// 参数须为 IResourceLocation(Addressables 2.8 委托签名;具体类 ResourceLocation 的
        /// InternalId 为显式接口实现,经具体类不可访问 —— 编译错误修复,勿改回)。
        /// </summary>
        string TransformInternalId(IResourceLocation location) =>
            location.InternalId.Replace(RemoteHostVariableName,
                BuildRemoteBasePath(RemoteServerUrl, CurrentRemoteVersion, location.InternalId));

        /// <summary>
        /// 按文件类型拼装远程基地址(纯函数,EditMode 可测)。
        /// 版本化规则(2026-09-04,与 tools/deploy_firebase.ps1 布局一一对应):
        ///   · catalog 文件(.bin/.hash 结尾)→ {服务器}/{版本}:每版本独立目录,回滚=指针指回旧版本;
        ///   · bundle(.bundle 结尾)         → {服务器}:落共享 Android/ 目录,URL 永不变化 → 设备
        ///     UnityWebRequest 缓存跨版本命中,版本切换只需重下几 KB 的 catalog;
        ///   · 版本未知(null/空)            → 一律 {服务器}(共享旧路径,旧客户端兼容目录)。
        /// </summary>
        public static string BuildRemoteBasePath(string serverUrl, string version, string internalId)
        {
            bool isCatalogFile = internalId.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                              || internalId.EndsWith(".hash", StringComparison.OrdinalIgnoreCase);
            return isCatalogFile && !string.IsNullOrEmpty(version)
                ? serverUrl + "/" + version
                : serverUrl;
        }

        /// <summary>
        /// catalog 更新(网络等待 5s 上限,内聚于此 —— 超时与失败同语义)。
        /// 流程(2026-09-04 版本化部署):①解析版本指针 index.json(失败沿用上次持久化版本,不中断);
        /// ②按当前版本目录检查/更新 catalog;③版本目录不可达时(指针落后/目录被清/网络抖动)会话内
        /// 清空版本回退共享旧路径重试一次——双路径都失败才切内置兜底源(UseBuiltinContent=true)。
        /// 返回 true = 远程就绪(继续远程装载);false = 远程不可用,已自动切内置兜底源
        /// (UseBuiltinContent=true),调用方继续尝试装载(内置 location 零网络)。
        /// 2026-09-03 前语义为"false = 无更新,调用方降级包内版本" —— 修复断网空降级缺陷后,
        /// 调用方(HotUpdateService)不再因 false 放弃,而是走内置源兜底(见 RunAsync 注释)。
        /// </summary>
        public async UniTask<bool> TryUpdateCatalogAsync()
        {
            try
            {
                await ResolveRemoteVersionAsync();
                // 确保远程地址改写已生效(构造函数已装,此处兜底防外部覆盖;委托读实例当前版本状态)
                Addressables.InternalIdTransformFunc = TransformInternalId;
                return await CheckAndUpdateCatalogAsync();
            }
            catch (Exception e)
            {
                // 版本化目录不可达 → 会话内清除版本,回退共享旧路径再试一次(双保险;
                // 不清持久化值——下次启动会重新解析指针,拿到服务器真正的当前版本)
                if (!string.IsNullOrEmpty(CurrentRemoteVersion))
                {
                    Debug.LogWarning($"[HotUpdate] 版本 {CurrentRemoteVersion} 的 catalog 检查失败({e.Message}),回退共享旧路径重试");
                    CurrentRemoteVersion = null;
                    try { return await CheckAndUpdateCatalogAsync(); }
                    catch (Exception legacyError) { e = legacyError; }
                }
                // 双路径都不可达(断网/服务器停/超时)→ 切内置兜底源,本进程不再尝试远程
                UseBuiltinContent = true;
                Debug.LogWarning($"[HotUpdate] catalog 检查失败,切内置兜底源继续装载: {e.Message}");
                return false;
            }
        }

        /// <summary>catalog 检查+更新单次尝试(各 5s 上限);true=远程 catalog 就绪(含"无更新")。</summary>
        static async UniTask<bool> CheckAndUpdateCatalogAsync()
        {
            var catalogs = await AwaitHandleAsync(Addressables.CheckForCatalogUpdates())
                .Timeout(TimeSpan.FromSeconds(5));
            if (catalogs == null || catalogs.Count == 0) return true;
            await AwaitHandleAsync(Addressables.UpdateCatalogs(catalogs))
                .Timeout(TimeSpan.FromSeconds(5));
            return true;
        }

        /// <summary>
        /// 解析远程内容版本指针:拉取 {RemoteServerUrl}/index.json 取当前版本目录名并持久化。
        /// 失败(断网/404/格式无效)不抛出——沿用上次持久化版本(PlayerPrefs,VersionPrefsKey);
        /// 从未成功过则为 null → 后续 catalog 走共享旧路径(旧客户端兼容目录仍在部署)。
        /// </summary>
        async UniTask ResolveRemoteVersionAsync()
        {
            var json = await FetchTextAsync(RemoteServerUrl + "/" + IndexFileName, IndexTimeoutSeconds);
            if (!string.IsNullOrEmpty(json) && TryParseIndexJson(json, out var index))
            {
                CurrentRemoteVersion = index.version;
                PlayerPrefs.SetString(VersionPrefsKey, index.version); // 断网启动时下次仍可用上次已知版本
                Debug.Log($"[HotUpdate] 远程内容版本指针: {index.version}(catalogHash={index.catalogHash})");
                return;
            }
            CurrentRemoteVersion = PlayerPrefs.GetString(VersionPrefsKey, null);
            if (CurrentRemoteVersion != null)
                Debug.LogWarning($"[HotUpdate] 版本指针不可用,沿用上次已知版本 {CurrentRemoteVersion}");
            else
                Debug.LogWarning("[HotUpdate] 版本指针不可用且无历史版本,catalog 走共享旧路径");
        }

        /// <summary>
        /// 解析 index.json(JsonUtility);无效(空输入/非法 JSON/version 缺失或为空)返回 false 且 index=null
        /// ——调用方拿到 true 才有可用指针对象,半成品对象(JsonUtility 对缺字段 JSON 返回非 null 空对象)不外泄。
        /// </summary>
        public static bool TryParseIndexJson(string json, out RemoteContentIndex index)
        {
            index = null;
            if (string.IsNullOrEmpty(json)) return false;
            RemoteContentIndex parsed;
            try
            {
                parsed = JsonUtility.FromJson<RemoteContentIndex>(json);
            }
            catch (Exception)
            {
                return false; // 非法 JSON(JsonUtility 多数情况下返回空对象而非抛异常,此处防御性兜底)
            }
            if (parsed == null || string.IsNullOrEmpty(parsed.version))
            {
                return false; // 无版本目录名 = 无效指针(版本化路径无从拼起)
            }
            index = parsed;
            return true;
        }

        /// <summary>简单 GET 文本(启动期一次性指针拉取,不走 Addressables);任何失败返回 null(已打日志)。</summary>
        static async UniTask<string> FetchTextAsync(string url, int timeoutSeconds)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = timeoutSeconds;
                var op = req.SendWebRequest();
                while (!op.isDone) await UniTask.Yield(); // 轮询等待(一次性拉取,不值得引入 UniTask.WebRequest 扩展依赖)
                // 结果读 req 本体(异步操作对象上无 result/error/downloadHandler —— 编译修复,勿改回)
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[HotUpdate] GET {url} 失败: {req.error}");
                    return null;
                }
                return req.downloadHandler.text;
            }
        }

        /// <summary>
        /// 加载热更 dll:内置兜底态直接走本地组;否则先试远程,**远程失败(任何原因)自动固化切内置再试一次**。
        /// </summary>
        public async UniTask<byte[]> LoadDllAsync(string assemblyName) =>
            await LoadWithFallbackAsync(DllAddressPrefix, BuiltinDllAddressPrefix, assemblyName);

        /// <summary>
        /// 加载 AOT 元数据:同上。Consistent 模式要求与包内剥离 AOT 一致 → 切换一旦发生即整链固化内置,
        /// 杜绝远程/内置混载(两者理论上同批,但混载无必要且引入一致性风险)。
        /// </summary>
        public async UniTask<byte[]> LoadMetadataAsync(string assemblyName) =>
            await LoadWithFallbackAsync(MetadataAddressPrefix, BuiltinMetadataAddressPrefix, assemblyName);

        /// <summary>
        /// 双地址装载(2026-09-03 二次修复核心):切换点**后置到装载层**。
        /// catalog 阶段的成败判定不可靠 —— ①Addressables 把远程 hash 下载失败静默吞成"无更新"(CheckCatalogsOperation
        /// 仅当全部失败才报错,部分失败/吞失败均静默放行);②catalog 检查/更新成功但 bundle 实际下载失败(SSL/断网/服务器坏)
        /// 时错误发生在 LoadAsset 阶段,catalog 阶段无从感知。因此:任何一次首选源装载失败 → 切内置再试,
        /// 失败即固化 UseBuiltinContent(本进程后续装载全走本地,零网络),内置也没有才返回 null 由编排层降级。
        /// </summary>
        async UniTask<byte[]> LoadWithFallbackAsync(string primaryPrefix, string builtinPrefix, string assetName)
        {
            var bytes = await LoadTextAssetBytesAsync((UseBuiltinContent ? builtinPrefix : primaryPrefix) + assetName);
            if (bytes != null || UseBuiltinContent) return bytes;
            SwitchToBuiltin(assetName); // 首选源失败(远程不可达/内容缺失/吞失败) → 固化切内置
            return await LoadTextAssetBytesAsync(builtinPrefix + assetName);
        }

        /// <summary>固化切内置兜底源(幂等;切换点:catalog 阶段预切换 + 装载层失败兜切换,双保险)。</summary>
        void SwitchToBuiltin(string assetName)
        {
            if (UseBuiltinContent) return;
            UseBuiltinContent = true;
            Debug.LogWarning($"[HotUpdate] 装载 {assetName} 失败,固化切内置兜底源(后续零网络)");
        }

        /// <summary>显式切内置兜底源(编排层总超时兜底;置位后本进程后续装载全走本地,零网络)。</summary>
        public void UseBuiltinFallback()
        {
            if (UseBuiltinContent) return;
            UseBuiltinContent = true;
            Debug.LogWarning("[HotUpdate] catalog 更新总超时,切内置兜底源继续装载");
        }

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
    /// (v1.0 包虽带类型头,但运行时方法被 IL2CPP 裁剪,调用即 MissingMethod —— 见
    /// IsRuntimeAvailable 的 #if 注释;直接编译期引用同样会裁剪崩溃)。
    /// 反射探测成功后才执行;HomologousImageMode 枚举经方法参数类型解析,不硬编码命名空间/数值。
    /// 9-4 起语义与真实 HybridCLR 对齐:LoadMetadata 按 AOT 程序集逐个装载(Consistent 模式),
    /// 全部元数据就绪后再 Assembly.Load 热更程序集。
    /// </summary>
    public sealed class HybridCLRAssemblyLoader : IHotUpdateAssemblyLoader
    {
        const string RuntimeApiTypeName = "HybridCLR.RuntimeApi, HybridCLR.Runtime";

        // 2026-09-05 真机重启后 More Games 无反应(第二层根因,19 文档 §10 第 5 项):
        // v1.0 包实际带 HybridCLR.Runtime **类型头**(HybridCLR 为 git Package v8.14.1,
        // 其 Runtime asmdef 无条件编译进所有 Player),Type.GetType 恒非 null → 旧写法恒 true,
        // 热更链每启执行 TryUpdateCatalogAsync → 下载服务器(staging)远程 catalog 写缓存
        // (服务器内容 = 旧远程组部署产物,实测含 c86f31c3/7d8d5143 bundle 名)→ Addressables
        // 初始化读该缓存 → 劫持包内内容(同一次启动内覆盖 AppBootstrap 的缓存清除)。
        // 修复:v1.0(无宏)恒 false 整链跳过 —— v1.0 无远程内容语义,热更 dll 已 AOT 随包,
        // 运行时方法还被 IL2CPP 裁剪(LoadMetadataForAOTAssembly MissingMethodException 实测),
        // 热更链对本包既无用又有害(网络请求 + 缓存污染种子);
        // v1.1(HYBRIDCLR_UNITY)保持反射真探测,宏语义即构建语义,两模式各自自洽。
#if HYBRIDCLR_UNITY
        public bool IsRuntimeAvailable => Type.GetType(RuntimeApiTypeName) != null;
#else
        public bool IsRuntimeAvailable => false;
#endif

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
