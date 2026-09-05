using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Box.Gameplay.HotUpdate;
using Box.ModuleFramework;
using Box.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// HotUpdateService 编排测试(Phase 9 9-3,10 文档 §16.4 验证①):
    /// 下载层与装载器注入 mock(Editor 是 Mono 且同名程序集已加载,**不能真 Assembly.Load**)。
    /// 覆盖:无运行时跳过 / catalog 失败继续走兜底装载(2026-09-03 断网空降级修复)/
    /// 总超时显式切内置 / 成功装载+清单刷新 / 装载失败清缓存 / metadata 缺失容错。
    /// </summary>
    public class HotUpdateServiceTests
    {
        static ModuleLoader NewLoader(params ModuleEntry[] entries) =>
            new ModuleLoader(new UIService(new ResourceViewLoader()), entries);

        static readonly ModuleEntry[] ValidEntries =
        {
            new ModuleEntry { id = "sudoku", entryType = "Box.HotUpdate.Sudoku.SudokuModule", enabled = true, sortOrder = 0 }
        };

        /// <summary>下载层 fake:调用可配置返回值并记录调用;所有方法同步完成,不触网。</summary>
        sealed class FakeSource : IHotUpdateContentSource
        {
            public bool CatalogResult = true;
            public bool ThrowTimeout; // true = 模拟 catalog 更新总超时(验证编排层显式切内置)
            public bool FallbackCalled; // UseBuiltinFallback 调用记录
            public byte[] DllBytes = { 1, 2, 3 };
            public byte[] MetadataBytes = { 4, 5 };
            public string OverridesJson;
            public readonly List<string> LoadedDlls = new();
            public readonly List<string> LoadedMetadatas = new();
            public int DllLoadCount;
            public int MetadataLoadCount;
            public bool ClearedCache;

            public UniTask<bool> TryUpdateCatalogAsync()
            {
                if (ThrowTimeout) throw new TimeoutException(); // 同步抛,由 RunAsync 步骤 2 的 try 捕获
                return UniTask.FromResult(CatalogResult);
            }

            public void UseBuiltinFallback() => FallbackCalled = true;

            public async UniTask<byte[]> LoadDllAsync(string assemblyName)
            {
                LoadedDlls.Add(assemblyName);
                DllLoadCount++;
                return DllBytes;
            }

            public async UniTask<byte[]> LoadMetadataAsync(string aotAssemblyName)
            {
                LoadedMetadatas.Add(aotAssemblyName);
                MetadataLoadCount++;
                return MetadataBytes;
            }

            public UniTask<string> LoadOverridesJsonAsync() => UniTask.FromResult(OverridesJson);

            public UniTask ClearCacheAsync()
            {
                ClearedCache = true;
                return UniTask.CompletedTask;
            }
        }

        /// <summary>装载器 fake:Editor 下不真 Assembly.Load,仅记录调用。</summary>
        sealed class FakeLoader : IHotUpdateAssemblyLoader
        {
            public bool Available = true;
            public bool LoadResult = true;
            public readonly List<string> LoadedMetadatas = new();
            public readonly List<string> LoadedAssemblies = new();

            public bool IsRuntimeAvailable => Available;

            public bool LoadMetadata(string aotAssemblyName, byte[] metadataBytes)
            {
                LoadedMetadatas.Add(aotAssemblyName);
                return LoadResult;
            }

            public bool LoadAssembly(string assemblyName, byte[] dllBytes)
            {
                LoadedAssemblies.Add(assemblyName);
                return LoadResult;
            }
        }

        /// <summary>v1.0 语义:无 HybridCLR 运行时 → 整链跳过,下载层零调用。</summary>
        [Test]
        public async Task NoRuntime_SkipsWholeChain()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource();
            var al = new FakeLoader { Available = false };

            await new HotUpdateService(source, al).RunAsync(loader);

            Assert.AreEqual(0, source.DllLoadCount, "无运行时不应加载任何 dll");
            Assert.AreEqual(0, source.MetadataLoadCount, "无运行时不应加载元数据");
            Assert.IsFalse(source.ClearedCache, "跳过链路不应清缓存");
            Assert.AreEqual(1, loader.Entries.Count, "包内清单保持不变");
        }

        /// <summary>
        /// catalog 更新失败(断网) → 2026-09-03 断网空降级修复后语义:不再放弃,
        /// 继续走装载链(源已切内置兜底,mock 返回字节即代表内置 location 有内容)。
        /// </summary>
        [Test]
        public async Task CatalogFailure_ContinuesWithFallbackLoading()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource { CatalogResult = false };
            var al = new FakeLoader();

            await new HotUpdateService(source, al).RunAsync(loader);

            CollectionAssert.AreEqual(
                HotUpdateService.AotMetadataAssemblies, al.LoadedMetadatas,
                "catalog 失败后 AOT 元数据仍逐个装载(内置兜底)");
            CollectionAssert.AreEqual(
                HotUpdateService.HotUpdateAssemblies, al.LoadedAssemblies,
                "catalog 失败后热更程序集仍装载(断网可玩的关键)");
            Assert.AreEqual(1, loader.Entries.Count, "无远程 overrides → 清单保持包内");
        }

        /// <summary>catalog 更新总超时 → 编排层显式通知源切内置兜底(源内自切为双保险),随后继续装载。</summary>
        [Test]
        public async Task CatalogTimeout_ExplicitFallbackThenContinues()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource { ThrowTimeout = true };
            var al = new FakeLoader();

            await new HotUpdateService(source, al).RunAsync(loader);

            Assert.IsTrue(source.FallbackCalled, "总超时后编排层应显式调用 UseBuiltinFallback");
            Assert.AreEqual(HotUpdateService.AotMetadataAssemblies.Count, source.MetadataLoadCount,
                "切内置后元数据装载继续");
            Assert.AreEqual(HotUpdateService.HotUpdateAssemblies.Count, source.DllLoadCount,
                "切内置后 dll 装载继续");
        }

        /// <summary>成功路径:名单内热更程序集(现 3 个,M3.4 起含 WaterSort)依次装载 → overrides 全量刷新清单。
        /// 断言引用 HotUpdateService.HotUpdateAssemblies 常量,名单变更不产生硬编码失步(2026-09-05 M3.4 欠账教训)。</summary>
        [Test]
        public async Task Success_LoadsBothAssembliesAndRefreshesCatalog()
        {
            var loader = NewLoader(ValidEntries);
            var overrides = new ModuleOverrides
            {
                version = "1.1.0",
                entries = new[]
                {
                    new ModuleEntry { id = "sudoku", entryType = "Box.HotUpdate.Sudoku.SudokuModule", enabled = true, sortOrder = 0 },
                    new ModuleEntry { id = "sudoku2", entryType = "Box.HotUpdate.Sudoku.SudokuModule", enabled = true, sortOrder = 1 }
                }
            };
            var source = new FakeSource { OverridesJson = JsonUtility.ToJson(overrides) };
            var al = new FakeLoader();

            await new HotUpdateService(source, al).RunAsync(loader);

            CollectionAssert.AreEqual(
                HotUpdateService.HotUpdateAssemblies,
                source.LoadedDlls, "按名单顺序装载全部热更程序集");
            CollectionAssert.AreEqual(
                HotUpdateService.AotMetadataAssemblies,
                source.LoadedMetadatas, "按 AOT 元数据清单逐个加载");
            CollectionAssert.AreEqual(
                HotUpdateService.AotMetadataAssemblies,
                al.LoadedMetadatas, "装载器逐个收到 AOT 元数据");
            CollectionAssert.AreEqual(
                HotUpdateService.HotUpdateAssemblies,
                al.LoadedAssemblies, "元数据就绪后装载热更程序集");
            Assert.AreEqual(2, loader.Entries.Count, "远程 overrides 全量替换包内清单");
            Assert.AreEqual("sudoku2", loader.Entries[1].id);
        }

        /// <summary>程序集装载失败(缓存损坏) → 清缓存,不刷新清单,下轮重试。</summary>
        [Test]
        public async Task AssemblyLoadFailure_ClearsCache()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource();
            var al = new FakeLoader { LoadResult = false };

            await new HotUpdateService(source, al).RunAsync(loader);

            Assert.IsTrue(source.ClearedCache, "装载失败应清依赖缓存");
            Assert.AreEqual(1, loader.Entries.Count, "清单保持包内版本");
        }

        /// <summary>AOT 元数据缺失 → 立即降级(Consistent 模式缺元数据不能安全装载),不碰 dll。</summary>
        [Test]
        public async Task MetadataMissing_DegradesBeforeDll()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource { MetadataBytes = null };
            var al = new FakeLoader();

            await new HotUpdateService(source, al).RunAsync(loader);

            Assert.AreEqual(0, source.DllLoadCount, "元数据缺失时不应加载 dll");
            Assert.AreEqual(0, al.LoadedAssemblies.Count, "不应装载任何程序集");
            Assert.AreEqual(1, loader.Entries.Count, "包内清单保持不变");
        }

        /// <summary>overrides 无效/为空 → 保持包内清单(dll 已装载仍生效)。</summary>
        [Test]
        public async Task OverridesInvalid_KeepsPackagedCatalog()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource { OverridesJson = "" };

            await new HotUpdateService(source, new FakeLoader()).RunAsync(loader);

            Assert.AreEqual(5, source.MetadataLoadCount, "AOT 元数据仍正常加载");
            Assert.AreEqual(HotUpdateService.HotUpdateAssemblies.Count, source.DllLoadCount,
                "dll 仍正常装载");
            Assert.AreEqual(1, loader.Entries.Count, "远程清单无效时保持包内清单");
        }
    }
}
