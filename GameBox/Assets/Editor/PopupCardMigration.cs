using Box.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 弹窗卡片迁移(2026-08 弹窗改造):把 6 个弹窗 prefab 从「居中面板」升级为
/// 「全屏遮罩根 + Card 子节点」结构:
///   根   = 全屏拉伸 + 黑 50% 遮罩(纯视觉压暗,点击无行为)
///   Card = 中心锚卡片(尺寸沿用原根,内容坐标相对卡片中心天然不变),
///          外观完全沿用旧根 Image 的贴图与颜色(卡片上已放置 UI 图,不拼色)+
///          raycastTarget 模态拦截(卡片内点击不穿透)
/// 幂等:检测根下 Card 子节点,已迁移仅自愈根锚(中断/半迁移修复)。
/// 由各弹窗生成器(Phase3/4/5/MoreGames)调用;保 GUID:LoadPrefabContents 原地迁移 →
/// SaveAsPrefabAsset 同路径覆盖,Addressables UI_Local 引用不断。
/// </summary>
public static class PopupCardMigration
{
    /// <summary>幂等检测:Card 子节点存在即已迁移。</summary>
    public static bool IsMigrated(GameObject root) => root.transform.Find("Card") != null;

    /// <summary>
    /// 迁移单个弹窗根(幂等):根改为全屏遮罩,原内容整体移入新建的 Card 子节点。
    /// root 为 LoadPrefabContents 产物或新建 GameObject,均可原地修改后落盘。
    /// </summary>
    public static void MigrateInstance(GameObject root)
    {
        if (IsMigrated(root))
        {
            EnsureStretchRoot(root); // 中断/半迁移:自愈根锚
            return;
        }
        var rootRt = root.GetComponent<RectTransform>();
        if (rootRt == null) return;
        var originalSize = rootRt.sizeDelta; // 先取原尺寸,根改拉伸锚后 sizeDelta 无意义

        // 1) 根:全屏拉伸 + 遮罩(黑 50%);原贴图/类型/颜色先留存给 Card
        //    ⚠️ 颜色必须在改遮罩之前取出,否则取到的是 MaskColor
        var rootImg = root.GetComponent<Image>();
        var oldSprite = rootImg != null ? rootImg.sprite : null;
        var oldType = rootImg != null ? rootImg.type : Image.Type.Simple;
        var oldColor = rootImg != null ? rootImg.color : Color.white;
        SetStretch(rootRt);
        if (rootImg != null)
        {
            rootImg.sprite = null;      // 遮罩纯色,不载贴图
            rootImg.type = Image.Type.Simple;
            rootImg.color = UITheme.MaskColor;
        }

        // 2) 建 Card:居中卡片,尺寸沿用原根(内容簇坐标相对卡片中心 = 原根中心,天然不变)。
        //    外观完全沿用旧根 Image 的贴图与颜色(卡片上已放置 UI 图,不做任何染色)
        var cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image));
        cardGo.transform.SetParent(root.transform, false);
        var cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta = originalSize;
        var cardImg = cardGo.GetComponent<Image>();
        cardImg.sprite = oldSprite;   // 原样保留旧根贴图(用户放置的 UI 图)
        cardImg.type = oldType;
        cardImg.color = oldColor; // 原样保留旧根颜色(用户放置的 UI 图配套色)
        cardImg.raycastTarget = true;  // 模态拦截:卡片区域内点击不穿透到遮罩(遮罩无处理器,纯拦截)

        // 3) 原根直接子节点整体移入 Card(worldPositionStays:false,本地坐标不变)
        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            var child = root.transform.GetChild(i);
            if (child == cardGo.transform) continue;
            child.SetParent(cardGo.transform, false);
        }
    }

    /// <summary>
    /// 升级路径:LoadPrefabContents 原地迁移 → SaveAsPrefabAsset 同路径(保 GUID)。
    /// ⚠️ 必须用 Contents 系列 API:实测(2026-08-28)实例化→SaveAsPrefabAsset 会丢弃
    /// 既有子节点的重挂载与根锚点改动(Card 新增成功但内容未移入),Contents 为纯对象树,逐字落盘。
    /// </summary>
    public static void MigratePath(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        if (IsMigrated(root))
        {
            PrefabUtility.UnloadPrefabContents(root);
            return; // 已迁移(幂等),零写入
        }
        MigrateInstance(root);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    /// <summary>容错直查(生成器升级步骤用):先查 Card/路径,未迁移 prefab 回退根直查。</summary>
    public static Transform FindInCard(GameObject root, string path)
        => root.transform.Find("Card/" + path) ?? root.transform.Find(path);

    /// <summary>生成器新建节点的挂载目标:已迁移返回 Card(内容统一进卡片),未迁移返回根。</summary>
    public static Transform FindCardOrRoot(GameObject root)
        => root.transform.Find("Card") ?? root.transform;

    /// <summary>
    /// 已存在 prefab 的幂等补迁:「创建即跳过」类生成器对历史产物无效——
    /// 弹窗在改造前已生成且跳过检查命中时,迁移须补跑(无 Card → 实例化→迁移→覆盖保存,保 GUID)。
    /// </summary>
    public static void MigrateExistingIfNeeded(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        if (IsMigrated(root))
        {
            PrefabUtility.UnloadPrefabContents(root);
            return; // 已迁移(幂等),零写入
        }
        MigrateInstance(root);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log("[PopupCardMigration] 旧版弹窗补迁: " + path);
    }

    // ---- 内部 ----

    /// <summary>根锚自愈:Card 已存在但根非全屏拉伸(中断/半迁移)时修正。</summary>
    static void EnsureStretchRoot(GameObject root)
    {
        var rt = root.GetComponent<RectTransform>();
        if (rt == null) return;
        if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one) return;
        SetStretch(rt);
    }

    /// <summary>全屏拉伸锚 + 偏移清零。</summary>
    static void SetStretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
