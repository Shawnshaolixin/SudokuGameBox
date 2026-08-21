using System.Collections.Generic;
using Box.UI;
using Cysharp.Threading.Tasks;

namespace Box.UI.Tests
{
    /// <summary>
    /// 测试用视图:记录生命周期调用顺序。
    /// 独立运行时程序集(Box.UI.Tests.Helpers)——Editor-only 测试程序集里定义的
    /// MonoBehaviour 无法挂到场景对象,故拆出。
    /// </summary>
    public class RecordingView : UIView
    {
        public readonly List<string> Log = new();

        protected override void Awake() { Log.Add("Awake"); base.Awake(); }
        protected override UniTask OnCreate() { Log.Add("Create"); return UniTask.CompletedTask; }
        protected override UniTask OnShow(object args) { Log.Add("Show:" + args); return UniTask.CompletedTask; }
        protected override UniTask OnHide() { Log.Add("Hide"); return UniTask.CompletedTask; }
        protected override UniTask OnDestroy() { Log.Add("Destroy"); return UniTask.CompletedTask; }

        /// <summary>
        /// 测试专用:EditMode 下 Instantiate 不执行克隆体 Awake(实例化仅做字段拷贝),
        /// 故层级/缓存等需在实例化前对源 prefab 显式设置。
        /// </summary>
        public void ForceLayer(UILayer layer) => Layer = layer;
    }

    /// <summary>HUD 层级视图(测非栈层行为);层由测试经 ForceLayer 在实例化前设置。</summary>
    public sealed class HUDView : RecordingView { }
}
