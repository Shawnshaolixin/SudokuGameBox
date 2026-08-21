using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Box.UI
{
    /// <summary>
    /// 基础控件薄封装(10 文档 §8 3-2):按钮/文本/输入/进度。
    /// 全部无第三方依赖、全 AOT;热更侧只调这些类型,不直接碰 UGUI/TMP 事件。
    /// </summary>

    /// <summary>按钮:点击回调注册 + 防重入。</summary>
    public sealed class BoxButton : MonoBehaviour
    {
        readonly List<Action> _clicks = new();
        Button _button;
        bool _firing;

        void Awake()
        {
            _button = GetComponent<Button>();
            if (_button != null) _button.onClick.AddListener(Fire);
        }

        public void OnClick(Action callback)
        {
            if (callback != null) _clicks.Add(callback);
        }

        public void SetInteractable(bool value)
        {
            if (_button != null) _button.interactable = value;
        }

        void Fire()
        {
            if (_firing) return; // 防重入:回调内再次点击不递归
            _firing = true;
            foreach (var cb in _clicks)
            {
                try { cb(); }
                catch (Exception e) { Debug.LogWarning($"[UIKit] 点击回调异常: {e.Message}"); }
            }
            _firing = false;
        }
    }

    /// <summary>文本:TMP 薄封装。</summary>
    public sealed class BoxText : MonoBehaviour
    {
        TMP_Text _text;

        void Awake() => _text = GetComponent<TMP_Text>();

        public string Text
        {
            get => _text != null ? _text.text : string.Empty;
            set { if (_text != null) _text.text = value; }
        }

        public void SetColor(Color color) { if (_text != null) _text.color = color; }
        public void SetFontSize(float size) { if (_text != null) _text.fontSize = size; }
        public void SetVisible(bool visible) { if (_text != null) _text.enabled = visible; }
    }

    /// <summary>输入框:TMP_InputField 薄封装。</summary>
    public sealed class BoxInput : MonoBehaviour
    {
        TMP_InputField _input;

        void Awake() => _input = GetComponent<TMP_InputField>();

        public string Text
        {
            get => _input != null ? _input.text : string.Empty;
            set { if (_input != null) _input.text = value; }
        }

        public void SetPlaceholder(string text)
        {
            if (_input != null && _input.placeholder is TMP_Text ph) ph.text = text;
        }

        public void SetInteractable(bool value) { if (_input != null) _input.interactable = value; }
        public void SetPasswordMode(bool value) { if (_input != null) _input.contentType = value ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard; }
    }

    /// <summary>进度条:Image fillAmount 薄封装(0~1)。</summary>
    public sealed class BoxProgress : MonoBehaviour
    {
        Image _fill;

        void Awake() => _fill = GetComponent<Image>();

        public float Progress
        {
            get => _fill != null ? _fill.fillAmount : 0f;
            set { if (_fill != null) _fill.fillAmount = Mathf.Clamp01(value); }
        }

        public void SetColor(Color color) { if (_fill != null) _fill.color = color; }
    }
}
