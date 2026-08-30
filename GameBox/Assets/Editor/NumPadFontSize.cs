using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 数字盘字号放大(2026-08-30 棋盘动效任务:棋盘数字 40→44,下方待选数字 50→54,视觉更饱满)。
/// 只改 NumberPanel/Num1..Num9 按钮下的 Label TMP,不动工具条/标题等其他 TMP。
/// 幂等:已为 54 则跳过;可反复执行。CLI 无头:unity -executeMethod NumPadFontSize.Run
/// 模式对齐 PopupResidueCleanup:LoadPrefabContents → 改 → SaveAsPrefabAsset → ForceUpdate 防缓存覆盖 → 自校验。
/// </summary>
public static class NumPadFontSize
{
    const string PrefabPath = "Assets/Modules/Sudoku/Prefabs/GameplayView.prefab";
    const int TargetSize = 54; // 原 50

    [MenuItem("Box/Phase4/Num Pad Font Size 54")]
    public static void Run()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("[NumPadFontSize] 缺失 prefab: " + PrefabPath);
            EditorApplication.Exit(1);
            return;
        }

        int changed = 0, total = 0;
        for (int n = 1; n <= 9; n++)
        {
            var numBtn = FindInactive(root.transform, "NumberPanel/Num" + n);
            if (numBtn == null)
            {
                Debug.LogWarning("[NumPadFontSize] 未找到数字按钮: NumberPanel/Num" + n);
                continue;
            }
            // Label 子节点(SetButtonLabel 同路径约定);找不到时兜底找第一个 TMP
            var label = numBtn.Find("Label") ?? FirstTmp(numBtn);
            if (label == null)
            {
                Debug.LogWarning("[NumPadFontSize] 数字按钮无 Label TMP: Num" + n);
                continue;
            }
            var tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null) continue;
            total++;
            if (tmp.fontSize != TargetSize)
            {
                tmp.fontSize = TargetSize;
                changed++;
            }
        }

        if (changed > 0)
        {
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            // 防缓存覆盖(2026-08-30 实测坑):ForceUpdate 强制磁盘内容进缓存,防编辑器退出回写旧值
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[NumPadFontSize] {total} 个数字按钮 TMP 已处理,{changed} 个改为 {TargetSize}");
        }
        PrefabUtility.UnloadPrefabContents(root);

        // 自校验:确认磁盘内容已是目标字号,防静默回退
        var check = PrefabUtility.LoadPrefabContents(PrefabPath);
        int ok = 0, fail = 0;
        for (int n = 1; n <= 9; n++)
        {
            var numBtn = FindInactive(check.transform, "NumberPanel/Num" + n);
            var label = numBtn != null ? (numBtn.Find("Label") ?? FirstTmp(numBtn)) : null;
            var tmp = label != null ? label.GetComponent<TextMeshProUGUI>() : null;
            if (tmp == null) { fail++; continue; }
            if (tmp.fontSize == TargetSize) ok++; else fail++;
        }
        PrefabUtility.UnloadPrefabContents(check);
        if (fail > 0)
        {
            Debug.LogError($"[NumPadFontSize] 自校验失败:仅 {ok}/9 达到 {TargetSize}");
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log($"[NumPadFontSize] done: 数字盘 9 格全部 fontSize={TargetSize}(自校验通过)");
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

    /// <summary>第一个 TextMeshProUGUI 后代(Label 缺失时兜底)。</summary>
    static Transform FirstTmp(Transform root)
    {
        var tmp = root.GetComponent<TextMeshProUGUI>();
        if (tmp != null) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FirstTmp(root.GetChild(i));
            if (found != null) return found;
        }
        return null;
    }
}
