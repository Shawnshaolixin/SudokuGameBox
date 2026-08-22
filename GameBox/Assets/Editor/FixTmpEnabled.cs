using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 真机/编辑器文字不显示修复:排查发现 4 个 UI prefab 的全部 TextMeshProUGUI
/// 组件 m_Enabled 被置为 0(上次会话 prefab 重写时带入,git diff 确认
/// 提交版本均为 m_Enabled: 1)。组件禁用 → 文字不渲染且无任何报错,
/// 与"真机无字 + logcat 零 TMP 日志"现象完全吻合。
/// 本脚本幂等:已启用组件不动,运行后可反复执行。
/// 编辑器内执行:菜单 Box/Phase4/Fix TMP Enabled
/// CLI 无头执行:unity run GameBox -- -executeMethod FixTmpEnabled.Run
/// </summary>
public static class FixTmpEnabled
{
    [MenuItem("Box/Phase4/Fix TMP Enabled")]
    public static void Run()
    {
        string[] prefabPaths = Directory.GetFiles("Assets/Resources/UI", "*.prefab", SearchOption.AllDirectories);
        int totalEnabled = 0, totalDisabled = 0;

        foreach (var path in prefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            int enabledCount = 0;
            foreach (var tmp in tmps)
            {
                if (!tmp.enabled) { tmp.enabled = true; enabledCount++; }
            }
            totalDisabled += enabledCount;
            totalEnabled += tmps.Length;
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            if (enabledCount > 0)
                Debug.Log($"[FixTmpEnabled] {path}: 启用 {enabledCount} 个 TMP 组件");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FixTmpEnabled] done: {prefabPaths.Length} 个 prefab, TMP 组件共 {totalEnabled} 个, 本次启用 {totalDisabled} 个");
    }
}
