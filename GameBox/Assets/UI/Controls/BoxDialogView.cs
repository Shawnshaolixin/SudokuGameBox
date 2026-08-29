using System;
using TMPro;
using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// 通用弹窗(10 文档 §8 3-2):标题/正文/确认/取消,Popup 层,配合 PopupArbiter 使用。
    /// 约定子节点名:Title / Message / Confirm / Cancel(由 Phase3SceneSetup 生成)。
    /// 关闭动作(调 UIRouter.PopAsync)由业务方在回调里发起,弹窗自身不碰路由。
    /// 按钮文案:prefab 静态英文兜底;业务方经 SetConfirmText/SetCancelText 传 L10n 文案
    /// (2026-08-29 Bug 清单:按钮文案英语化,防静态中文残留)。
    /// </summary>
    public class BoxDialogView : UIView
    {
        BoxText _title;
        BoxText _message;
        BoxButton _confirm;
        BoxButton _cancel;
        TextMeshProUGUI _confirmLabel; // 确认按钮 Label(按钮文案载体)
        TextMeshProUGUI _cancelLabel;  // 取消按钮 Label

        protected override void Awake()
        {
            Layer = UILayer.Popup; // 模态弹窗,受 PopupArbiter 互斥管辖
            base.Awake();
            // 弹窗改造(2026-08)后内容在 Card 子节点下,走容错查找(未迁移 prefab 回退根直查)
            _title = FindInCard("Title")?.GetComponent<BoxText>();
            _message = FindInCard("Message")?.GetComponent<BoxText>();
            _confirm = FindInCard("Confirm")?.GetComponent<BoxButton>();
            _cancel = FindInCard("Cancel")?.GetComponent<BoxButton>();
            _confirmLabel = FindInCard("Confirm/Label")?.GetComponent<TextMeshProUGUI>();
            _cancelLabel = FindInCard("Cancel/Label")?.GetComponent<TextMeshProUGUI>();
        }

        public void SetTitle(string text) { if (_title != null) _title.Text = text; }
        public void SetMessage(string text) { if (_message != null) _message.Text = text; }
        public void SetConfirmText(string text) { if (_confirmLabel != null) _confirmLabel.text = text; }
        public void SetCancelText(string text) { if (_cancelLabel != null) _cancelLabel.text = text; }
        public void OnConfirm(Action callback) => _confirm?.OnClick(callback);
        public void OnCancel(Action callback) => _cancel?.OnClick(callback);
        public void HideConfirm() { if (_confirm != null) _confirm.gameObject.SetActive(false); }
    }
}
