using System;
using UnityEngine;

namespace Box.ModuleFramework
{
    /// <summary>模块清单条目(11 文档 §3.1 静态部分):包内 Catalog 随包走,离线兜底。</summary>
    [Serializable]
    public sealed class ModuleEntry
    {
        /// <summary>模块唯一 id(EnterAsync 键 + 埋点前缀 {id}.{action},§8.4)。</summary>
        public string id;

        /// <summary>入口类型全名(IGameModule 实现,ModuleLoader 反射实例化)。</summary>
        public string entryType;

        /// <summary>中间态:玩法场景名(v1.1 单场景化后废弃,改走入口 prefab 资源)。</summary>
        public string entryScene;

        /// <summary>大厅显示名(本地化 key 占位,文案查表在后续 Phase)。</summary>
        public string displayName;

        public bool enabled = true;

        /// <summary>大厅入口排序。</summary>
        public int sortOrder;
    }

    /// <summary>
    /// 模块目录资产(编辑器维护,随包走,离线兜底)。
    /// 运行时经 Resources 加载(AppBootstrap),缺失时告警 + 空目录(不阻断启动,§4.2 纪律)。
    /// 远程清单 module_overrides(上新玩法/下架/灰度)v1.1 接入,覆盖本资产字段。
    /// </summary>
    [CreateAssetMenu(menuName = "Box/ModuleCatalog")]
    public sealed class ModuleCatalog : ScriptableObject
    {
        public ModuleEntry[] entries = Array.Empty<ModuleEntry>();
    }
}
