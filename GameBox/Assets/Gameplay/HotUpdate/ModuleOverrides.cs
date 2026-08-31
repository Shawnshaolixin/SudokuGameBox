using System;
using Box.ModuleFramework;
using UnityEngine;

namespace Box.Gameplay.HotUpdate
{
    /// <summary>
    /// 远程模块清单(Phase 9 9-3,10 文档 §16.4)。
    /// JSON 字段与 ModuleEntry 一一对应,由 9-4 GenerateContent 从 ModuleCatalog.asset 序列化生成;
    /// 运行时加载后经 ModuleLoader.Refresh 全量替换包内清单(= 最简单回滚:清单随包走,远程可整体回退)。
    /// </summary>
    [Serializable]
    public sealed class ModuleOverrides
    {
        /// <summary>清单版本(与 HotUpdateVersion.CodeVersion 约定一致,供调试与后续灰度)。</summary>
        public string version;

        /// <summary>模块条目(全量替换语义:远程清单即为最终清单)。</summary>
        public ModuleEntry[] entries = Array.Empty<ModuleEntry>();
    }
}
