using Box.Services;
using Box.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 结算弹窗(Phase 4 4-3):展示星级/用时/错误/提示。
    /// Confirm=Next(同难度重开),Cancel=Home(回主菜单);返回键关闭不导航(Action=None)。
    /// 由 PopupArbiter 展示(被动弹窗),args 为 SettlementResult,关闭后回写 Action。
    /// 文案走 L10n(FR-17):标题/消息按当前语言渲染。
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
                SetTitle(L10n.Get(_result.IsDaily ? "settlement.title.daily" : "settlement.title.normal"));
                string time = GameplayView.FormatTime(_result.TimeSec);
                string hints = _result.HintsUsed > 0 ? L10n.Format("settlement.hints", _result.HintsUsed) : "";
                SetMessage(L10n.Format("settlement.message", _result.StarRating, time, _result.MistakeCount, hints));
            }
            SetConfirmText(L10n.Get("settlement.next")); // 按钮文案走 L10n(2026-08-29 Bug 清单)
            SetCancelText(L10n.Get("settlement.home"));
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
