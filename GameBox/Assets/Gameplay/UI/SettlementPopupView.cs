using Box.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.Gameplay
{
    /// <summary>
    /// 结算弹窗(Phase 4 4-3):展示星级/用时/错误/提示。
    /// Confirm=Next(同难度重开),Cancel=Home(回主菜单);返回键关闭不导航(Action=None)。
    /// 由 PopupArbiter 展示(被动弹窗),args 为 SettlementResult,关闭后回写 Action。
    /// </summary>
    public sealed class SettlementPopupView : BoxDialogView
    {
        SettlementResult _result;
        UIService _svc;

        protected override UniTask OnCreate()
        {
            _svc = UIService.Instance;
            OnConfirm(OnNext);
            OnCancel(OnHome);
            return UniTask.CompletedTask;
        }

        protected override UniTask OnShow(object args)
        {
            _result = args as SettlementResult;
            if (_result != null)
            {
                SetTitle(_result.IsDaily ? "每日挑战完成" : "对局完成");
                string time = GameplayView.FormatTime(_result.TimeSec);
                string hints = _result.HintsUsed > 0 ? $"  提示 {_result.HintsUsed}" : "";
                SetMessage($"星级 {_result.StarRating}/3   用时 {time}   错误 {_result.MistakeCount}{hints}");
            }
            return UniTask.CompletedTask;
        }

        async void OnNext()
        {
            if (_result != null) _result.Action = SettlementAction.Next;
            await PopSelf();
        }

        async void OnHome()
        {
            if (_result != null) _result.Action = SettlementAction.Home;
            await PopSelf();
        }

        async UniTask PopSelf()
        {
            if (_svc != null) await _svc.Router.PopAsync();
        }
    }
}
