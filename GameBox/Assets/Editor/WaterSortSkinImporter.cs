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
///    同目录 .shader(液体软裁剪 ws_liquid_soft)按同一地址约定入组,热更侧经 IAssetService 加载。
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
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!IsConformingName(name))
            {
                // 命名不合规(空格/大写等,21 文档 §6.6/18 文档 CI-2):跳过入组并提示,防脏地址进产物
                Debug.LogWarning($"[WaterSortSkin] 跳过不合规文件名(请改名后重存): {name}");
                continue;
            }
            ApplyImportPreset(path);                  // 有导入变更 → SaveAndReimport(条目随后同轮补上)
            WaterSortViewSetup.EnsureEntry(path, AddressOf(path)); // 幂等:缺则建,组错/地址错则改
        }
        // 同目录 shader(液体软裁剪等)按同一地址约定入组;无纹理导入预设,仅注册
        foreach (var file in Directory.GetFiles(UiDir, "*.shader"))
        {
            var path = file.Replace('\\', '/');
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!IsConformingName(name))
            {
                Debug.LogWarning($"[WaterSortSkin] 跳过不合规文件名(请改名后重存): {name}");
                continue;
            }
            WaterSortViewSetup.EnsureEntry(path, AddressOf(path));
        }
        AssetDatabase.SaveAssets(); // 统一落盘(EnsureEntry 内部已 SetDirty;无变更时为空转 no-op)
    }

    /// <summary>导入设置按 21 文档 §6.4「UI 皮肤图」行对齐;有变更才写并重导入,返回是否有变更。</summary>
    static bool ApplyImportPreset(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return false;
        var name = System.IO.Path.GetFileNameWithoutExtension(path); // 供 _mask 网格规则判定
        bool dirty = false;
        if (imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; dirty = true; }
        if (imp.spriteImportMode != SpriteImportMode.Single) { imp.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; dirty = true; }
        if (imp.mipmapEnabled) { imp.mipmapEnabled = false; dirty = true; }
        if (imp.filterMode != FilterMode.Bilinear) { imp.filterMode = FilterMode.Bilinear; dirty = true; }
        // 网格类型:ws_*_mask(内腔剪影)须 Tight —— shader 未就绪时的 UGUI Mask 兜底路径
        // 靠遮罩图形网格(useSpriteMesh)当模板,FullRect 会退化成矩形裁剪;
        // 主路径为软裁剪 shader 逐像素采样 alpha,网格形状不参与。其余 UI 图统一 FullRect 防误配。
        // spriteMeshType 在 TextureImporterSettings 上(TextureImporter 无此属性),经 Read/Set 设置。
        bool wantTight = name.EndsWith("_mask", System.StringComparison.Ordinal);
        var wantMesh = wantTight ? SpriteMeshType.Tight : SpriteMeshType.FullRect;
        var tis = new TextureImporterSettings();
        imp.ReadTextureSettings(tis);
        // 剪影挤出归零:extrude 会把 Tight 网格外扩 1px,兜底 Mask 模板随之比剪影大一圈,
        // 液体裁剪边越过美术剪影落在壁线外(底弧毛边成因之一),故 _mask 一律关闭
        if (wantTight && tis.spriteExtrude != 0)
        {
            tis.spriteExtrude = 0;
            dirty = true;
        }
        if (tis.spriteMeshType != wantMesh)
        {
            tis.spriteMeshType = wantMesh;
            imp.SetTextureSettings(tis);
            dirty = true;
        }
        if (!dirty) return false;
        imp.SaveAndReimport();
        return true;
    }

    /// <summary>代码地址 = 去掉 Assets/Modules/ 前缀的相对路径去扩展名(如 WaterSort/UI/ws_tube),
    /// 21 文档 §6.1 —— 与题库 WaterSort/Levels/*.json、数独 Sudoku/Fx/* 同一约定;
    /// 注册地址与加载 key 不一致(如误只去 Assets/)会致 PlayMode 各模式都报 No Location。
    /// 扩展名泛化剥离(png / shader 共用,ChangeExtension 只改尾缀等价于去除)。</summary>
    static string AddressOf(string path)
    {
        const string prefix = "Assets/Modules/";
        var noExt = System.IO.Path.ChangeExtension(path, null).Replace('\\', '/');
        return noExt.Substring(prefix.Length);
    }

    /// <summary>文件名合规校验:小写下划线 + 数字(与 18 文档 CI-2 后缀约定配套,地址不得含空格)。</summary>
    static bool IsConformingName(string name)
    {
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (!ok) return false;
        }
        return true;
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
