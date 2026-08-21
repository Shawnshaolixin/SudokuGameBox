using Box.UI;
using Cysharp.Threading.Tasks;

namespace Box.Gameplay
{
    /// <summary>
    /// 对局视图(10 文档 §8 3-3 场景框架;功能在 Phase 4 4-2 填充)。
    /// prefab: Resources/UI/GameplayView.prefab(Phase3SceneSetup 生成)。
    /// </summary>
    public sealed class GameplayView : UIView
    {
        protected override UniTask OnCreate()
        {
            // Phase 4 4-2:棋盘/数字键盘/计时器接线
            return UniTask.CompletedTask;
        }
    }
}
