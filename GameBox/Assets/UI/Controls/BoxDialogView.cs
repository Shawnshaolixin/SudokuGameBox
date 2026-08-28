using System;
using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// 通用弹窗(10 文档 §8 3-2):标题/正文/确认/取消,Popup 层,配合 PopupArbiter 使用。
    /// 约定子节点名:Title / Message / Confirm / Cancel(由 Phase3SceneSetup 生成)。
    /// 关闭动作(调 UIRouter.PopAsync)由业务方在回调里发起,弹窗自身不碰路由。
    /// </summary>
    public class BoxDialogView : UIView
    {
        BoxText _title;
        BoxText _message;
        BoxButton _confirm;
        BoxButton _cancel;

        protected override void Awake()
        {
            Layer = UILayer.Popup; // 模态弹窗,受 PopupArbiter 互斥管辖
            base.Awake();
            // 弹窗改造(2026-08)后内容在 Card 子节点下,走容错查找(未迁移 prefab 回退根直查)
            _title = FindInCard("Title")?.GetComponent<BoxText>();
            _message = FindInCard("Message")?.GetComponent<BoxText>();
            _confirm = FindInCard("Confirm")?.GetComponent<BoxButton>();
            _cancel = FindInCard("Cancel")?.GetComponent<BoxButton>();
        }

        public void SetTitle(string text) { if (_title != null) _title.Text = text; }
        public void SetMessage(string text) { if (_message != null) _message.Text = text; }
        public void OnConfirm(Action callback) => _confirm?.OnClick(callback);
        public void OnCancel(Action callback) => _cancel?.OnClick(callback);
        public void HideConfirm() { if (_confirm != null) _confirm.gameObject.SetActive(false); }
    }
}
