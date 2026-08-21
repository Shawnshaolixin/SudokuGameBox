using Box.UI;

namespace Box.Gameplay
{
    /// <summary>
    /// 结算弹窗(10 文档 §8 3-3 场景框架;内容在 Phase 4 4-3 填充)。
    /// 复用通用弹窗布局:Title/Message/Confirm/Cancel。
    /// prefab: Resources/UI/Popups/SettlementPopup.prefab(Phase3SceneSetup 生成)。
    /// </summary>
    public sealed class SettlementPopupView : BoxDialogView
    {
        // Phase 4 4-3:展示用时/错误数,确认返回主菜单
    }
}
