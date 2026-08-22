using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Box.ModuleFramework
{
    /// <summary>模块加载状态(供大厅入口灰化/调试)。</summary>
    public enum ModuleLoadState { Idle, Entering, Active, Exiting }

    /// <summary>
    /// 模块加载器接口(11 文档 §3.2,可注入非 static):
    /// 进入 = 校验清单 → 实例化入口类型 → OnEnter;退出 = OnExit → 回收。
    /// EnterAsync 失败返回 false,不抛到 UI 层(§3.2 失败即降级:入口灰化 + 提示 + 上报)。
    /// </summary>
    public interface IModuleLoader
    {
        /// <summary>清单条目(大厅渲染入口网格用,§3.1)。</summary>
        IReadOnlyList<ModuleEntry> Entries { get; }

        /// <summary>进入模块;args 为模块入口参数;返回是否成功。</summary>
        UniTask<bool> EnterAsync(string moduleId, object args = null);

        /// <summary>退出模块;返回是否成功(未进入/正在退出返回 false)。</summary>
        UniTask<bool> ExitAsync(string moduleId);

        ModuleLoadState GetState(string moduleId);
    }
}
