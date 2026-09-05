using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 水排序皮肤资源「导入规范 + 自动入组」(21 文档 §6.1/§6.4 落地):
/// 1) Modules/WaterSort/UI/ 下 png 统一 Texture Type=Sprite / Single / Bilinear / 关 mipmap ——
///    运行时按单块 sprite 地址加载(WaterSortTubeRack.TubeSpriteAddress),误设 Multiple 多切块会被纠正
///    (ws_tube 曾误切出 ws_tube_0/1 两块致地址加载歧义);确认需要多切块的资产请移出本目录另放。
/// 2) 幂等注册进 Game_WaterSort 组:地址 = WaterSort/UI/&lt;文件名&gt;(21 文档 §6.1 约定),
///    入库走 WaterSortViewSetup.EnsureEntry —— 与 prefab/关卡 JSON 同一自愈入口,组/地址不符自动修复。
/// 每次域重载扫一遍目录,仅当导入设置或条目缺/错时才写盘,零日常开销;美术文件增改名自动生效。
/// </summary>
public static class WaterSortSkinImporter
{
    // 皮肤目录(21 文档 §6 落位:玩法专属 UI 图归模块目录,壳层共享仍走 Assets/Art/UI)
    const string UiDir = "Assets/Modules/WaterSort/UI";

    // 组名须与 WaterSortViewSetup(WS-20)一字不差;缺组时按 DefaultGroup 模式补建(逻辑同其 EnsureGroup)
    const string GroupGameWaterSort = "Game_WaterSort";

    [InitializeOnLoadMethod]
    static void SweepOnLoad()
    {
        // 延迟到首轮导入空闲后执行:域重载当下立刻 SaveAndReimport 会与资源导入队列互撞
        EditorApplication.delayCall += Sweep;
    }

    /// <summary>扫描 UI 目录:导入设置对齐 + Addressables 条目自愈(幂等;菜单可手动重跑)。</summary>
    [MenuItem("Box/WaterSort/Sweep Art Registration")]
    public static void Sweep()
    {
        if (!Directory.Exists(UiDir)) return;
        EnsureGroup();
        foreach (var file in Directory.GetFiles(UiDir, "*.png"))
        {
            var path = file.Replace('\\', '/');
            ApplyImportPreset(path);                  // 有导入变更 → SaveAndReimport(条目随后同轮补上)
            WaterSortViewSetup.EnsureEntry(path, AddressOf(path)); // 幂等:缺则建,组错/地址错则改
        }
        AssetDatabase.SaveAssets(); // 统一落盘(EnsureEntry 内部已 SetDirty;无变更时为空转 no-op)
    }

    /// <summary>导入设置按 21 文档 §6.4「UI 皮肤图」行对齐;有变更才写并重导入,返回是否有变更。</summary>
    static bool ApplyImportPreset(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return false;
        bool dirty = false;
        if (imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; dirty = true; }
        if (imp.spriteImportMode != SpriteImportMode.Single) { imp.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; dirty = true; }
        if (imp.mipmapEnabled) { imp.mipmapEnabled = false; dirty = true; }
        if (imp.filterMode != FilterMode.Bilinear) { imp.filterMode = FilterMode.Bilinear; dirty = true; }
        if (!dirty) return false;
        imp.SaveAndReimport();
        return true;
    }

    /// <summary>代码地址 = 去 Assets/ 前缀的相对路径去扩展名(如 WaterSort/UI/ws_tube),21 文档 §6.1。</summary>
    static string AddressOf(string path)
    {
        const string prefix = "Assets/";
        return path.Substring(prefix.Length, path.Length - prefix.Length - ".png".Length);
    }

    /// <summary>组缺失时补建(照 WaterSortViewSetup.EnsureGroup 同构;DefaultGroup schemas 免手动配打包规则)。</summary>
    static bool EnsureGroup()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return false;
        if (settings.FindGroup(GroupGameWaterSort) != null) return false;
        var group = settings.CreateGroup(GroupGameWaterSort, false, false, false,
            new System.Collections.Generic.List<AddressableAssetGroupSchema>(settings.DefaultGroup.Schemas));
        if (group == null)
        {
            Debug.LogError("[WaterSortSkin] 分组创建失败: " + GroupGameWaterSort);
            return false;
        }
        Debug.Log("[WaterSortSkin] 分组已创建: " + GroupGameWaterSort);
        return true;
    }
}
