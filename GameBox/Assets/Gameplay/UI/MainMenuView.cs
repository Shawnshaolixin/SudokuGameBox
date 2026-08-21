using Box.UI;
using Cysharp.Threading.Tasks;

namespace Box.Gameplay
{
    /// <summary>
    /// 主菜单视图(10 文档 §8 3-3 场景框架;功能在 Phase 4 4-1 填充)。
    /// prefab: Resources/UI/MainMenuView.prefab(Phase3SceneSetup 生成)。
    /// </summary>
    public sealed class MainMenuView : UIView
    {
        protected override UniTask OnCreate()
        {
            // Phase 4 4-1:难度选择按钮接线
            return UniTask.CompletedTask;
        }
    }
}
