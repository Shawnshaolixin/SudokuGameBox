using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// UI 主题常量(Phase 8 体验打磨:UI 打磨)。
    /// 收敛散落的硬编码颜色与贴图选择;浅/深两套主题(v1.0 换肤脚本写入浅色,
    /// 深色仅 SettingsPopup 即时预览,全 UI 运行时切换 v1.0 后置,见 SettingsView 注释)。
    /// 贴图来源:Kenney UI Pack(CC0),9-slice 圆角,Assets/Art/UI/{Buttons,Panels}。
    /// 运行时经 Addressables 加载同名资源(Phase6AddressablesSetup.RegisterArtAssets 已注册)。
    /// </summary>
    public static class UITheme
    {
        // ---- 贴图地址(Editor 换肤脚本/运行时共用,Art/ 前缀去扩展名) ----
        public const string ButtonTex = "Art/UI/Buttons/button_rectangle_depth_flat"; // 主按钮(圆角矩形)
        public const string PanelTex = "Art/UI/Panels/button_rectangle_depth_border_panel"; // 弹窗面板(描边圆角;2026-08-29 命名契约加 _panel 后缀同步)

        // ---- 浅色主题(默认,对齐 docs/UIDesignSystem 设计 token) ----
        public static readonly Color Button = new Color(0.35f, 0.60f, 0.95f); // 主按钮 tint:品牌蓝(贴图灰白纹理乘色)
        public static readonly Color Panel = new Color(1.00f, 0.9765f, 0.9137f); // 弹窗面板背景:Surface/Primary #FFF9E9
        public static readonly Color Text = new Color(0.12f, 0.12f, 0.14f);   // 正文(与 SettingsView 预览一致)

        // ---- 设计系统 token(docs/UIDesignSystem;卡片已放置 UI 图,仅保留被引用的) ----
        public static readonly Color TextPrimary = new Color(0.2275f, 0.1647f, 0.1020f); // 主文字 #3A2A1A(SettingsView 浅色预览用)
        public static readonly Color MaskColor = new Color(0f, 0f, 0f, 0.5f); // 弹窗遮罩:黑 50% 压暗背景

        // ---- 深色主题(v1.0 后置;与 SettingsView 预览色一致,方便未来切换) ----
        public static readonly Color DarkButton = new Color(0.22f, 0.42f, 0.72f);
        public static readonly Color DarkPanel = new Color(0.08f, 0.08f, 0.10f);
        public static readonly Color DarkText = Color.white;
    }
}
