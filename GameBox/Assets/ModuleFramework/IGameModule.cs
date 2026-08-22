using Cysharp.Threading.Tasks;

namespace Box.ModuleFramework
{
    /// <summary>
    /// 玩法模块接口(11 文档 §3.2):每个玩法一个实现,由 ModuleLoader 按清单实例化。
    /// 框架全 AOT;玩法实现 v1.0 随包编译(D-2 纯 AOT 模式),v1.1 起进热更 dll —— 接口形状不变。
    /// </summary>
    public interface IGameModule
    {
        /// <summary>模块唯一 id(与 ModuleCatalog 条目一致,同时是埋点前缀)。</summary>
        string Id { get; }

        /// <summary>
        /// 进入模块。中间态(v1.0):模块内部负责加载玩法场景;
        /// v1.1 单场景化后:加载模块入口资源(prefab)进 UI 栈,接口不变。
        /// </summary>
        UniTask OnEnter(ModuleContext ctx);

        /// <summary>退出模块:清理资源;中间态下由模块内部切回大厅场景。</summary>
        UniTask OnExit();
    }
}
