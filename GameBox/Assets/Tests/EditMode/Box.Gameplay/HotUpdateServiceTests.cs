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
    /// 覆盖:无运行时跳过 / catalog 失败降级 / 成功装载+清单刷新 / 装载失败清缓存 / metadata 缺失容错。
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
            public byte[] DllBytes = { 1, 2, 3 };
            public byte[] MetadataBytes = { 4, 5 };
            public string OverridesJson;
            public readonly List<string> LoadedDlls = new();
            public int DllLoadCount;
            public int MetadataLoadCount;
            public bool ClearedCache;

            public UniTask<bool> TryUpdateCatalogAsync() => UniTask.FromResult(CatalogResult);

            public async UniTask<byte[]> LoadDllAsync(string assemblyName)
            {
                LoadedDlls.Add(assemblyName);
                DllLoadCount++;
                return DllBytes;
            }

            public async UniTask<byte[]> LoadMetadataAsync(string assemblyName)
            {
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
            public readonly List<string> Loaded = new();

            public bool IsRuntimeAvailable => Available;

            public bool Load(string assemblyName, byte[] dllBytes, byte[] metadataBytes)
            {
                Loaded.Add(assemblyName);
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

        /// <summary>catalog 更新失败(断网/超时) → 静默降级,保持包内版本。</summary>
        [Test]
        public async Task CatalogFailure_DegradesToPackaged()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource { CatalogResult = false };
            var al = new FakeLoader();

            await new HotUpdateService(source, al).RunAsync(loader);

            Assert.AreEqual(0, source.DllLoadCount, "catalog 失败不应尝试装载");
            Assert.AreEqual(1, loader.Entries.Count, "包内清单保持不变");
        }

        /// <summary>成功路径:两个热更程序集依次装载 → overrides 全量刷新清单。</summary>
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

            await new HotUpdateService(source, new FakeLoader()).RunAsync(loader);

            CollectionAssert.AreEqual(
                new[] { "Box.HotUpdate.Core", "Box.HotUpdate.Sudoku" },
                source.LoadedDlls, "按名单顺序装载两个热更程序集");
            Assert.AreEqual(2, source.MetadataLoadCount, "两个程序集各加载一次元数据");
            Assert.AreEqual(2, loader.Entries.Count, "远程 overrides 全量替换包内清单");
            Assert.AreEqual("sudoku2", loader.Entries[1].id);
        }

        /// <summary>Assembly.Load 失败(缓存损坏) → 清缓存,不刷新清单,下轮重试。</summary>
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

        /// <summary>metadata 缺失(9-3 阶段地址未配置) → 跳过元数据装载,仍能完成 dll 装载。</summary>
        [Test]
        public async Task MetadataMissing_StillLoadsAssembly()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource { MetadataBytes = null };
            var al = new FakeLoader();

            await new HotUpdateService(source, al).RunAsync(loader);

            Assert.AreEqual(2, source.LoadedDlls.Count, "dll 装载不受元数据缺失影响");
            Assert.AreEqual(2, al.Loaded.Count, "装载器收到两个程序集");
        }

        /// <summary>overrides 无效/为空 → 保持包内清单(dll 已装载仍生效)。</summary>
        [Test]
        public async Task OverridesInvalid_KeepsPackagedCatalog()
        {
            var loader = NewLoader(ValidEntries);
            var source = new FakeSource { OverridesJson = "" };

            await new HotUpdateService(source, new FakeLoader()).RunAsync(loader);

            Assert.AreEqual(2, source.DllLoadCount, "dll 仍正常装载");
            Assert.AreEqual(1, loader.Entries.Count, "远程清单无效时保持包内清单");
        }
    }
}
