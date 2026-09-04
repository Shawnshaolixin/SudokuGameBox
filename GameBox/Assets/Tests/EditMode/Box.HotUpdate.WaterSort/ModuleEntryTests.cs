using System;
using Box.ModuleFramework;
using NUnit.Framework;
using UnityEngine;

namespace Box.HotUpdate.WaterSort.Tests
{
    /// <summary>
    /// 清单接入用例(M1.4,13 文档步骤 3/5):真实 ModuleCatalog.asset 含 watersort 入口,
    /// 入口类型可经 ModuleLoader.ResolveType 同语义(裸全名扫已加载程序集)解析并实例化。
    /// 防回归:AddEntry 幂等误删条目、link.xml 缺保留(编辑器 Mono 不裁剪,真机 IL2CPP 才暴露——
    /// 本用例至少守住"清单 → 类型"字符串一致性)。
    /// </summary>
    public class ModuleEntryTests
    {
        const string ModuleId = "watersort";
        const string EntryTypeName = "Box.HotUpdate.WaterSort.WaterSortModule";

        static ModuleEntry FindEntry()
        {
            // 运行时路径与 AppBootstrap 同源(Resources 兜底,资产在仓库随包走)
            var catalog = Resources.Load<ModuleCatalog>("Config/ModuleCatalog");
            Assert.IsNotNull(catalog, "ModuleCatalog.asset 缺失(需先跑 Phase45ModuleSetup.Build)");
            foreach (var e in catalog.entries)
                if (e != null && e.id == ModuleId) return e;
            return null;
        }

        [Test]
        public void Catalog_Contains_WaterSort_Entry()
        {
            var entry = FindEntry();
            Assert.IsNotNull(entry, $"ModuleCatalog.asset 缺 {ModuleId} 条目(需跑 WaterSortViewSetup.Build)");
            Assert.IsTrue(entry.enabled, "入口必须 enabled(大厅 More Games 列表可见性)");
            Assert.IsFalse(string.IsNullOrEmpty(entry.entryType));
            Assert.AreEqual(EntryTypeName, entry.entryType, "入口类型全名与 WaterSortModule 不符");
        }

        [Test]
        public void WaterSortModule_Resolves_And_Instantiates()
        {
            var entry = FindEntry();
            Assert.IsNotNull(entry);
            // 与 ModuleLoader.ResolveType 同语义:先精确命中,再裸全名扫已加载程序集(热更 dll 场景)
            var type = Type.GetType(entry.entryType);
            if (type == null)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    if ((type = asm.GetType(entry.entryType)) != null) break;
            Assert.IsNotNull(type, $"入口类型解析失败:{entry.entryType}(检查 link.xml 保留/程序集引用)");
            Assert.IsTrue(typeof(IGameModule).IsAssignableFrom(type), "入口类型必须实现 IGameModule");
            var module = (IGameModule)Activator.CreateInstance(type);
            Assert.IsNotNull(module);
            Assert.AreEqual(ModuleId, module.Id, "模块 Id 与清单键不一致(埋点前缀/EnterAsync 键)");
        }
    }
}
