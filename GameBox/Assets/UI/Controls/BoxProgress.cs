using UnityEngine;
using UnityEngine.UI;

namespace Box.UI
{
    /// <summary>进度条(10 文档 §8 3-2):Image fillAmount 薄封装(0~1)。</summary>
    /// <remarks>单类文件(Unity 约定):多类同文件会导致非首类 prefab 序列化为 missing script。</remarks>
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
