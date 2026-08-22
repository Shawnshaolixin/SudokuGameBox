using System;
using System.Collections.Generic;
using Box.ModuleFramework;
using Box.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Box.ModuleFramework.Tests
{
    /// <summary>
    /// ModuleLoader 生命周期与容错(11 文档 §15「模块清单容错」用例):
    /// 缺字段/未知模块/入口反射失败 → 不崩、返回 false、状态回 Idle。
    /// </summary>
    public class ModuleLoaderTests
    {
        static readonly IReadOnlyList<ModuleEntry> ValidEntries = new[]
        {
            new ModuleEntry { id = "fake", entryType = typeof(FakeModule).FullName, enabled = true },
        };

        ModuleLoader _loader;
        GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _loader = NewLoader();
            FakeModule.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_prefab != null) UnityEngine.Object.DestroyImmediate(_prefab);
            var es = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);
            var runner = GameObject.Find("BoxUI_BackKey");
            if (runner != null) UnityEngine.Object.DestroyImmediate(runner);
        }

        static ModuleLoader NewLoader(IReadOnlyList<ModuleEntry> entries = null)
        {
            var ui = new UIService(new ResourceViewLoader()); // 测试不加载视图,仅提供 ctx.UI
            return new ModuleLoader(ui, entries ?? ValidEntries);
        }

        [Test]
        public void Enter_UnknownModule_ReturnsFalse()
        {
            Assert.False(_loader.EnterAsync("nope").GetAwaiter().GetResult());
            Assert.AreEqual(ModuleLoadState.Idle, _loader.GetState("nope"));
        }

        [Test]
        public void Enter_DisabledModule_ReturnsFalse()
        {
            var loader = NewLoader(new[]
            {
                new ModuleEntry { id = "off", entryType = typeof(FakeModule).FullName, enabled = false },
            });
            Assert.False(loader.EnterAsync("off").GetAwaiter().GetResult());
        }

        [Test]
        public void Enter_MissingEntryType_ReturnsFalse_AndIdle()
        {
            var loader = NewLoader(new[]
            {
                new ModuleEntry { id = "broken", entryType = "No.Such.Type", enabled = true },
            });
            Assert.False(loader.EnterAsync("broken").GetAwaiter().GetResult());
            Assert.AreEqual(ModuleLoadState.Idle, loader.GetState("broken"));
        }

        [Test]
        public void Enter_NonModuleType_ReturnsFalse()
        {
            var loader = NewLoader(new[]
            {
                new ModuleEntry { id = "wrong", entryType = typeof(NotAModule).FullName, enabled = true },
            });
            Assert.False(loader.EnterAsync("wrong").GetAwaiter().GetResult());
        }

        [Test]
        public void Enter_RunsOnEnter_WithContext_AndActive()
        {
            Assert.True(_loader.EnterAsync("fake", "hello").GetAwaiter().GetResult());
            Assert.AreEqual(ModuleLoadState.Active, _loader.GetState("fake"));
            Assert.AreEqual(1, FakeModule.EnterCount);
            Assert.AreEqual("hello", FakeModule.LastCtx.Args, "入口参数经 ctx 透传");
            Assert.AreSame(_loader, FakeModule.LastCtx.Loader, "ctx 携带加载器(交叉导量入口)");
            Assert.NotNull(FakeModule.LastCtx.UI, "ctx 携带 UIKit 句柄");
        }

        [Test]
        public void Enter_WhileActive_Rejected()
        {
            Assert.True(_loader.EnterAsync("fake").GetAwaiter().GetResult());
            Assert.False(_loader.EnterAsync("fake").GetAwaiter().GetResult());
            Assert.AreEqual(1, FakeModule.EnterCount, "重入不触发第二次 OnEnter");
        }

        [Test]
        public void Exit_NotActive_ReturnsFalse()
        {
            Assert.False(_loader.ExitAsync("fake").GetAwaiter().GetResult());
        }

        [Test]
        public void Exit_CallsOnExit_ResetsToIdle_AllowsReenter()
        {
            Assert.True(_loader.EnterAsync("fake").GetAwaiter().GetResult());
            Assert.True(_loader.ExitAsync("fake").GetAwaiter().GetResult());
            Assert.AreEqual(1, FakeModule.ExitCount);
            Assert.AreEqual(ModuleLoadState.Idle, _loader.GetState("fake"));
            Assert.True(_loader.EnterAsync("fake").GetAwaiter().GetResult(), "退出后可再次进入");
            Assert.AreEqual(2, FakeModule.EnterCount);
        }

        [Test]
        public void OnEnter_Throws_ReturnsFalse_AndIdle()
        {
            FakeModule.ThrowOnEnter = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("OnEnter 异常"));
            Assert.False(_loader.EnterAsync("fake").GetAwaiter().GetResult());
            Assert.AreEqual(ModuleLoadState.Idle, _loader.GetState("fake"));
            FakeModule.ThrowOnEnter = false; // 复位后重试应成功
            Assert.True(_loader.EnterAsync("fake").GetAwaiter().GetResult(), "失败后状态复位,可重试");
        }

        [Test]
        public void Entries_ExposesCatalog_And_SkipsNullOrEmptyIds()
        {
            var loader = NewLoader(new[]
            {
                null,
                new ModuleEntry { id = "", entryType = typeof(FakeModule).FullName },
                new ModuleEntry { id = "fake", entryType = typeof(FakeModule).FullName },
            });
            Assert.AreEqual(1, loader.Entries.Count, "null/空 id 条目不入册");
            Assert.AreEqual("fake", loader.Entries[0].id);
        }

        [Test]
        public void Register_Overwrites_Instance()
        {
            var a = NewLoader();
            var b = NewLoader();
            ModuleLoader.Register(a);
            Assert.AreSame(a, ModuleLoader.Instance);
            ModuleLoader.Register(b);
            Assert.AreSame(b, ModuleLoader.Instance, "重复注册覆盖旧实例");
        }

        /// <summary>测试模块:普通类实现 IGameModule(不依赖场景,验证反射实例化链路)。</summary>
        public sealed class FakeModule : IGameModule
        {
            public static int EnterCount;
            public static int ExitCount;
            public static bool ThrowOnEnter;
            public static ModuleContext LastCtx;

            public static void Reset()
            {
                EnterCount = 0;
                ExitCount = 0;
                ThrowOnEnter = false;
                LastCtx = null;
            }

            public string Id => "fake";

            public UniTask OnEnter(ModuleContext ctx)
            {
                EnterCount++;
                LastCtx = ctx;
                if (ThrowOnEnter) throw new InvalidOperationException("模拟 OnEnter 异常");
                return UniTask.CompletedTask;
            }

            public UniTask OnExit()
            {
                ExitCount++;
                return UniTask.CompletedTask;
            }
        }

        /// <summary>非 IGameModule 类型(验证入口类型校验)。</summary>
        public sealed class NotAModule
        {
        }
    }
}
