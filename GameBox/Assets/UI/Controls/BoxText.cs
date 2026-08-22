using TMPro;
using UnityEngine;

namespace Box.UI
{
    /// <summary>文本(10 文档 §8 3-2):TMP 薄封装。</summary>
    /// <remarks>单类文件(Unity 约定):多类同文件会导致非首类 prefab 序列化为 missing script。</remarks>
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
}
