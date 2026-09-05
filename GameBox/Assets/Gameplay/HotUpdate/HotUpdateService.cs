using System;
using System.Collections.Generic;
using Box.ModuleFramework;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.Gameplay.HotUpdate
{
    /// <summary>
    /// 热更引导服务(Phase 9 9-3,10 文档 §16.4)—— AOT 侧编排核心。
    ///
    /// 纪律:全方法体无条件编译,HybridCLR 一切交互走反射(IHotUpdateAssemblyLoader);
    /// v1.0 主包无 HybridCLR.RuntimeApi → IsRuntimeAvailable=false → 整链静默跳过,零额外开销。
    /// 任一步失败静默降级用包内版本(日志 Warning,不弹窗不阻塞);Assembly.Load 失败清缓存下轮重试。
    /// </summary>
    public sealed class HotUpdateService
    {
        /// <summary>热更程序集名单(与 Phase9HybridCLRSetup.HotUpdateAssemblies 保持一致;Box.Gameplay 不引用 Box.Editor,故本地维护)。
        /// M3.4 加 Box.HotUpdate.WaterSort —— 四方同步见 Phase9HybridCLRSetup 名单注释。</summary>
        public static readonly IReadOnlyList<string> HotUpdateAssemblies = new[]
        {
            "Box.HotUpdate.Core",
            "Box.HotUpdate.Sudoku",
            "Box.HotUpdate.WaterSort"
        };

        /// <summary>
        /// AOT 元数据清单(Consistent 模式装载对象 = 热更代码引用的 AOT 程序集剥离 dll)。
        /// 与 GenerateAll 产出 AOTGenericReferences.PatchedAOTAssemblyList 一致(9-1 审查);
        /// 更新需与 Phase9HybridCLRSetup.GenerateContent 的拷贝名单保持同步。
        /// </summary>
        public static readonly IReadOnlyList<string> AotMetadataAssemblies = new[]
        {
            "Box.UI",
            "System.Core",
            "UniTask",
            "UnityEngine.CoreModule",
            "mscorlib"
        };

        public static HotUpdateService Instance { get; private set; }

        readonly IHotUpdateContentSource _source;
        readonly IHotUpdateAssemblyLoader _loader;

        /// <summary>依赖注入构造(source/loader 可替换,EditMode 测试注入 mock)。</summary>
        public HotUpdateService(IHotUpdateContentSource source, IHotUpdateAssemblyLoader loader)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        /// <summary>
        /// 启动引导入口(AppBootstrap.Boot 调用):fire-and-forget,不 await 不阻塞。
        /// 后台链路在启动后异步执行:反射探测 → catalog 更新(5s 超时)→ 装载热更 dll → 刷新模块清单。
        /// </summary>
        public static void Begin(ModuleLoader loader, IHotUpdateContentSource source = null, IHotUpdateAssemblyLoader assemblyLoader = null)
        {
            if (Instance != null) return; // 防重复引导(单例生命周期=进程)
            var svc = new HotUpdateService(
                source ?? new AddressablesHotUpdateSource(),
                assemblyLoader ?? new HybridCLRAssemblyLoader());
            Instance = svc;
            svc.RunAsync(loader).Forget(); // 异常由 RunAsync 内部兜底,不外泄
        }

        /// <summary>热更编排主流程(公开以便 EditMode 测试直接驱动;内部异常全部兜底不抛出)。</summary>
        public async UniTask RunAsync(ModuleLoader loader)
        {
            try
            {
                // 1) 反射探测:v1.0 主包无 HybridCLR 运行时 → 整链跳过(静默,仅日志)
                if (!_loader.IsRuntimeAvailable)
                {
                    Debug.Log("[HotUpdate] 主包无 HybridCLR 运行时(v1.0 语义),热更链路跳过");
                    return;
                }

                // 2) 远程 catalog 更新(网络等待总上限 5s)。失败/超时 **不再放弃** ——
                //    2026-09-03 断网空降级缺陷修复:source 已切内置兜底源(BuiltinHotUpdate 本地组),
                //    步骤 3 起的装载走本地 location(零网络)。真到"内置也没有"才降级(裸机/数据异常)。
                bool remoteOk;
                try
                {
                    remoteOk = await _source.TryUpdateCatalogAsync()
                        .Timeout(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _source.UseBuiltinFallback(); // 显式切内置(内层自切双保险)
                    remoteOk = false;
                }
                if (!remoteOk)
                {
                    Debug.Log("[HotUpdate] catalog 更新失败/超时 → 内置兜底源继续尝试装载");
                }

                // 3) AOT 元数据先行(Consistent 模式:热更 dll 引用 AOT 类型/泛型时依赖元数据,缺一不可安全装载);
                //    任一失败清缓存,下轮启动重试(不阻断本局;内置源下清缓存无用武之地但语义一致)
                foreach (var aot in AotMetadataAssemblies)
                {
                    var metadata = await _source.LoadMetadataAsync(aot);
                    if (metadata == null || metadata.Length == 0)
                    {
                        Debug.LogWarning($"[HotUpdate] 缺少 AOT 元数据: {aot},降级包内版本");
                        return;
                    }
                    if (!_loader.LoadMetadata(aot, metadata))
                    {
                        await _source.ClearCacheAsync();
                        return;
                    }
                }

                // 4) 元数据就绪后装载热更程序集(Assembly.Load)
                foreach (var asm in HotUpdateAssemblies)
                {
                    var dll = await _source.LoadDllAsync(asm);
                    if (dll == null || dll.Length == 0)
                    {
                        Debug.LogWarning($"[HotUpdate] 缺少 dll 资源: {asm},降级包内版本");
                        return;
                    }
                    if (!_loader.LoadAssembly(asm, dll))
                    {
                        await _source.ClearCacheAsync(); // 缓存损坏 → 清空重试
                        return;
                    }
                }

                // 5) 远程清单(module_overrides)全量替换包内清单;无远程清单保持现状
                var overridesJson = await _source.LoadOverridesJsonAsync();
                if (!string.IsNullOrEmpty(overridesJson))
                {
                    var overrides = JsonUtility.FromJson<ModuleOverrides>(overridesJson);
                    if (overrides != null && overrides.entries != null && overrides.entries.Length > 0)
                    {
                        loader.Refresh(overrides.entries);
                        Debug.Log($"[HotUpdate] 模块清单已按远程 overrides 刷新,共 {overrides.entries.Length} 项 (v{overrides.version})");
                        return;
                    }
                    Debug.LogWarning("[HotUpdate] overrides 内容为空或无效,保持包内清单");
                }
                Debug.Log("[HotUpdate] 热更程序集装载完成,清单保持包内版本");
            }
            catch (Exception e)
            {
                // 兜底:任何未预期异常不外泄,保证启动流程不受热更影响
                Debug.LogWarning($"[HotUpdate] 热更链路异常,降级包内版本: {e.Message}");
            }
        }
    }
}
