using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// 安全区适配组件(11 文档 §10.1):刘海/挖孔、横竖屏切换、键盘弹出时动态刷新锚点。
    /// UIView 基类 Awake 统一挂载(§3.5 第 8 条)。SetInsets 为静态纯函数,供 EditMode 测试。
    /// </summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _lastSafe;
        Vector2 _lastScreen;

        void Awake()
        {
            Apply();
        }

        void Update()
        {
            var safe = Screen.safeArea;
            var screen = new Vector2(Screen.width, Screen.height);
            if (_lastSafe != safe || _lastScreen != screen)
                Apply();
        }

        public void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_rect == null) return;
            _lastSafe = Screen.safeArea;
            _lastScreen = new Vector2(Screen.width, Screen.height);
            SetInsets(_rect, _lastSafe, _lastScreen);
        }

        /// <summary>纯逻辑:按安全区与屏幕尺寸计算锚点内缩;全屏无刘海时即为全拉伸。</summary>
        public static void SetInsets(RectTransform rect, Rect safe, Vector2 screen)
        {
            if (screen.x <= 0f || screen.y <= 0f) return;
            rect.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            rect.anchorMax = new Vector2(1f - (screen.x - safe.xMax) / screen.x, 1f - (screen.y - safe.yMax) / screen.y);
        }
    }
}
