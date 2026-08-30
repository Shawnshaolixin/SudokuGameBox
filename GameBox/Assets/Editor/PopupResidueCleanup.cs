using UnityEditor;
using UnityEngine;

/// <summary>
/// 清理弹窗预制体残留死节点(2026-08-30 SettlementPopup 发现):
/// Phase 4 旧版三行拆分(StarsText/TimeText/Message),后改为 Message 整段文案
/// (SettlementPopupView.OnShow 只 SetMessage,含 Stars/Time/Mistakes),但预制体内
/// StarsText/TimeText 节点未删除,运行时显示静态假数据与 Message 真实数据重复。
/// 幂等:节点不存在则跳过。CLI 无头:unity -executeMethod PopupResidueCleanup.CleanSettlement
/// </summary>
public static class PopupResidueCleanup
{
    const string SettlementPath = "Assets/UI/Prefabs/Popups/SettlementPopup.prefab";

    [MenuItem("Box/Fix/Clean SettlementPopup Residue Nodes")]
    public static void CleanSettlement()
    {
        var root = PrefabUtility.LoadPrefabContents(SettlementPath);
        if (root == null)
        {
            Debug.LogError("[PopupResidueCleanup] 缺失 prefab: " + SettlementPath);
            EditorApplication.Exit(1);
            return;
        }

        // 删除残留节点(容错:Card 下或根下两层查找)
        foreach (var name in new[] { "StarsText", "TimeText" })
        {
            var t = FindInactive(root.transform, "Card/" + name) ?? FindInactive(root.transform, name);
            if (t != null)
            {
                Object.DestroyImmediate(t.gameObject);
                Debug.Log("[PopupResidueCleanup] 已删除残留节点: " + name);
            }
            else
            {
                Debug.Log("[PopupResidueCleanup] 节点不存在(幂等跳过): " + name);
            }
        }

        // Message 上移居中(原三行布局 y=60,删行后单行微调 y=40 视觉居中)
        var msg = FindInactive(root.transform, "Card/Message");
        if (msg != null)
        {
            var rt = msg.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = new Vector2(0, 40);
        }

        PrefabUtility.SaveAsPrefabAsset(root, SettlementPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        // 防缓存覆盖(2026-08-30 实测坑):SaveAsPrefabAsset 后 AssetDatabase 可能仍持有旧 prefab 缓存,
        // 后续自动保存(如测试运行/编辑器退出)会把旧内容写回磁盘 → ForceUpdate 强制磁盘内容进缓存。
        AssetDatabase.ImportAsset(SettlementPath, ImportAssetOptions.ForceUpdate);

        // 自校验:确认磁盘内容已是修复版,防静默回退
        var check = PrefabUtility.LoadPrefabContents(SettlementPath);
        bool hasResidue = false;
        foreach (var name in new[] { "StarsText", "TimeText" })
        {
            if ((FindInactive(check.transform, "Card/" + name) ?? FindInactive(check.transform, name)) != null)
                hasResidue = true;
        }
        if (hasResidue)
        {
            PrefabUtility.UnloadPrefabContents(check);
            Debug.LogError("[PopupResidueCleanup] 自校验失败:残留节点仍存在,缓存覆盖未生效");
            EditorApplication.Exit(1);
            return;
        }
        PrefabUtility.UnloadPrefabContents(check);
        Debug.Log("[PopupResidueCleanup] SettlementPopup 残留清理完成(含缓存刷新+自校验)");
    }

    /// <summary>递归按路径查找 Transform(含未激活对象):Transform.Find 跳过 inactive,用 GetChild 索引遍历。</summary>
    static Transform FindInactive(Transform root, string path)
    {
        Transform cur = root;
        foreach (var name in path.Split('/'))
        {
            Transform next = null;
            for (int i = 0; i < cur.childCount; i++)
            {
                var child = cur.GetChild(i);
                if (child.name == name) { next = child; break; }
            }
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }
}
