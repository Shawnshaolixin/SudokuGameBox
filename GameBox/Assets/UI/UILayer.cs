using UnityEngine;
using UnityEngine.UI;

namespace Box.UI
{
    /// <summary>
    /// 层级定义(11 文档 §3.5 第 2 条):每层独立 Canvas,避免全屏重建。
    /// 层级深度即 Canvas sortingOrder,低层在下、高层在上。
    /// </summary>
    public enum UILayer
    {
        Scene = 0,   // 场景内界面(玩法自身 Canvas,不由 UIKit 管理)
        HUD = 100,   // 常驻 HUD(金币、设置按钮)
        Window = 200,// 普通窗口(设置、签到)
        Popup = 300, // 模态弹窗(互斥仲裁管辖)
        Toast = 400, // 轻提示(可盖在弹窗上,不参与互斥)
        Loading = 500,
        Debug = 600,
    }

    /// <summary>
    /// 层级管理器:懒创建 BoxUI 根节点 + 每层独立 Canvas(CanvasScaler + GraphicRaycaster)。
    /// </summary>
    public sealed class UILayerManager
    {
        const string RootName = "BoxUI";
        const float RefWidth = 1080f, RefHeight = 1920f;

        readonly Canvas[] _canvases = new Canvas[7];

        public Canvas GetCanvas(UILayer layer)
        {
            var idx = (int)layer;
            if (_canvases[idx] != null) return _canvases[idx];

            var root = EnsureRoot();
            var go = new GameObject($"Canvas_{layer}", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(root.transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = idx;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvases[idx] = canvas;
            return canvas;
        }

        static GameObject EnsureRoot()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) return existing;
            var root = new GameObject(RootName);
            Object.DontDestroyOnLoad(root); // 常驻,跨场景存活
            return root;
        }
    }
}
