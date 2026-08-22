using Box.UI;

namespace Box.ModuleFramework
{
    /// <summary>
    /// 模块上下文(11 文档 §3.2):共享服务句柄 + 入口参数。
    /// 玩法只通过本对象与壳/服务交互,禁止直接引用其他玩法类型(模块间零耦合)。
    /// </summary>
    public sealed class ModuleContext
    {
        /// <summary>UIKit 组合入口(路由/弹窗仲裁/层级)。</summary>
        public UIService UI { get; }

        /// <summary>模块加载器(交叉导量入口:玩法内调 EnterAsync("other") 跳其他玩法)。</summary>
        public IModuleLoader Loader { get; }

        /// <summary>入口参数(玩法自定义结构,如难度/每日挑战标记)。</summary>
        public object Args { get; }

        public ModuleContext(UIService ui, IModuleLoader loader, object args)
        {
            UI = ui;
            Loader = loader;
            Args = args;
        }
    }
}
