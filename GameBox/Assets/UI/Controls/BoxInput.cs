using TMPro;
using UnityEngine;

namespace Box.UI
{
    /// <summary>输入框(10 文档 §8 3-2):TMP_InputField 薄封装。</summary>
    /// <remarks>单类文件(Unity 约定):多类同文件会导致非首类 prefab 序列化为 missing script。</remarks>
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
}
