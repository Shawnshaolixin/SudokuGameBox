using System.IO;
using UnityEditor;
using UnityEditor.Build; // NamedBuildTarget(SetIcon 需按构建目标传参,替代已移除的 SetIconForTargetGroup)
using UnityEngine;

namespace Box.Editor
{
    /// <summary>
    /// Android App 图标设置(替换打出的 APK/AAB 的应用图标):
    /// 1) Legacy 图标(普通启动器/老设备) = Assets/Art/UI/Icons/app_icon.png(透明底 3D 图标);
    /// 2) 自适应图标(Android 8+ / Google Play 上架必配) = 前景用原图 + 背景用纯色深蓝(#031F43,与图标主体同色);
    /// 背景图 app_icon_bg.png 由脚本自动生成一次(_bg 后缀符合 CI-2 资产命名白名单)。
    /// 入口: 编辑器菜单 Box/Setup/1. Set Android App Icon,或 CLI -executeMethod Box.Editor.AppIconSetup.SetAndroidAppIcon。
    /// TODO: 若后续换新图标,仅需替换 Assets/Art/UI/Icons/app_icon.png 后重跑本方法。
    /// </summary>
    public static class AppIconSetup
    {
        const string IconPath = "Assets/Art/UI/Icons/app_icon.png";   // 主图标(透明底)
        const string BgPath = "Assets/Art/UI/Icons/app_icon_bg.png";  // 自适应背景(脚本生成)
        const int AdaptiveSize = 432;                                  // 自适应图标标准尺寸(px)

        // 与图标主体一致的深蓝,保证自适应图标的背景与前景融为一体
        static readonly Color BgColor = new Color32(0x03, 0x1F, 0x43, 0xFF);

        /// <summary>设置 Android 应用图标: legacy + round + 自适应(前景/背景)。幂等,可重复执行。</summary>
        [MenuItem("Box/Setup/1. Set Android App Icon")]
        public static void SetAndroidAppIcon()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogError("[AppIcon] 找不到 " + IconPath + ",请先将图标文件复制到该路径(命名须以 _icon 结尾)");
                return;
            }

            // Unity 6000.3 图标 API(SetIconForTargetGroup / IconKind.Adaptive* 已移除,经 IconApiProbe 实测确认):
            // GetSupportedIconKinds(Android) → 三种 kind: Adaptive(API 26,每槽 2 层=前景+背景)、
            // Round(API 25,1 层)、Legacy(1 层);每 kind 6 个尺寸槽(432→36px),引擎自动缩放。
            // 流程: GetPlatformIcons 取引擎提供的槽实例(勿 new,无公开构造) → 每槽 SetTextures → SetPlatformIcons 写回。
            var target = NamedBuildTarget.Android;
            var kinds = PlayerSettings.GetSupportedIconKinds(target);
            var bg = EnsureSolidBackground(); // 自适应背景(纯色,Play 要求背景必须存在)
            foreach (var kind in kinds)
            {
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (var slot in icons)
                {
                    // 层数语义: Adaptive 2 层 = [前景, 背景]; Round/Legacy 1 层直接放主图标
                    if (slot.maxLayerCount >= 2 && bg != null)
                        slot.SetTextures(new[] { icon, bg });
                    else
                        slot.SetTextures(new[] { icon });
                }
                PlayerSettings.SetPlatformIcons(target, kind, icons);
                Debug.Log($"[AppIcon] 已设置 {kind} 图标槽({icons.Length} 个尺寸)");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[AppIcon] Android 图标已设置: legacy + round + adaptive(foreground=app_icon, background=app_icon_bg)");
        }

        /// <summary>确保自适应背景存在: 首次生成 432x432 纯色 PNG 并导入,后续直接复用。</summary>
        static Texture2D EnsureSolidBackground()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(BgPath);
            if (existing != null) return existing;

            // 生成纯色纹理(432x432 为自适应图标标准输入尺寸)
            var tex = new Texture2D(AdaptiveSize, AdaptiveSize, TextureFormat.RGBA32, false);
            var pixels = new Color[AdaptiveSize * AdaptiveSize];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = BgColor;
            tex.SetPixels(pixels);
            tex.Apply();

            // 编码为 PNG 落盘(置于 Assets/Art/UI/Icons,后缀 _bg 符合命名白名单)
            var path = Application.dataPath.Replace('\\', '/') + "/Art/UI/Icons/app_icon_bg.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(BgPath);
            Debug.Log("[AppIcon] 已生成自适应背景 " + BgPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(BgPath);
        }
    }
}