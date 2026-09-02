using System.Collections.Generic;
using System.Linq;
using Box.HotUpdate.Sudoku;
using Box.UI;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 9 9-4 热更视图桥挂载迁移(10 文档 §16.5):给视图 prefab 根挂 HotViewBinder,
/// 解决 v1.1 真机热更组件序列化丢失(组件被 FilterHotFixAssemblies 剥离后反序列化找不到脚本)。
/// 由 CLI 无头执行:unity run GameBox -- -executeMethod Phase9HotViewBinderSetup.Attach
/// 幂等:根上已有配置正确的 Binder 则跳过;prefab 保存回原路径(GUID 不变,场景引用不丢)。
/// 架构纪律:v1.0/编辑器下组件序列化在 prefab 正常工作,桥空转;v1.1 下组件被剥,桥运行时挂回。
/// </summary>
public static class Phase9HotViewBinderSetup
{
    /// <summary>需挂桥的视图 prefab(热更 UIView 组件序列化在根的模块 prefab,随主包本地组/场景引用)。</summary>
    static readonly string[] ViewPrefabPaths =
    {
        "Assets/Modules/Sudoku/Prefabs/GameplayView.prefab",
        "Assets/Modules/Sudoku/Prefabs/DifficultySelect.prefab",
    };

    [MenuItem("Box/Phase9/Attach HotViewBinder to View Prefabs")]
    public static void Attach()
    {
        int attached = 0;
        foreach (var path in ViewPrefabPaths)
        {
            var go = PrefabUtility.LoadPrefabContents(path); // prefab 实例上下文编辑,不动场景引用
            try
            {
                var binder = EnsureBinder(go, path);
                if (binder != null) attached++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(go);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase9Binder] HotViewBinder 挂载迁移完成:新增 {attached} 个(prefab: {ViewPrefabPaths.Length})");
    }

    /// <summary>
    /// 在 prefab 根 GO 上保证 HotViewBinder 存在且 viewTypeFullName 指向热更视图类型。
    /// 视图类型识别:根上程序集名以 Box.HotUpdate 开头的 MonoBehaviour(排除 Binder 自身)。
    /// </summary>
    static HotViewBinder EnsureBinder(GameObject go, string path)
    {
        // 收集根(不含子节点)上的热更程序集组件
        var hotComponents = go.GetComponents<MonoBehaviour>()
            .Where(c => c != null && c.GetType().Assembly.GetName().Name.StartsWith("Box.HotUpdate"))
            .ToList();
        if (hotComponents.Count == 0)
        {
            Debug.LogWarning($"[Phase9Binder] {path} 根上无热更程序集组件,跳过挂载");
            return null;
        }
        if (hotComponents.Count > 1)
        {
            // 防御:多个热更组件时优先 UIView 子类(视图驱动组件),仍多个则告警取首个
            Debug.LogWarning($"[Phase9Binder] {path} 根上有 {hotComponents.Count} 个热更组件: " +
                             string.Join(", ", hotComponents.Select(c => c.GetType().FullName)));
        }

        var hotType = hotComponents.FirstOrDefault(c => c is UIView)?.GetType() ?? hotComponents[0].GetType();
        // 存"程序集限定名"(不带 Version),运行期 Type.GetType 宽松命中;裸全名由桥的 AppDomain 扫描兜底
        string typeName = hotType.FullName + ", " + hotType.Assembly.GetName().Name;

        var binder = go.GetComponent<HotViewBinder>();
        if (binder == null)
        {
            binder = go.AddComponent<HotViewBinder>();
            Debug.Log($"[Phase9Binder] {path} 根新增 HotViewBinder → {typeName}");
        }
        else if (binder.ViewTypeFullName == typeName)
        {
            Debug.Log($"[Phase9Binder] {path} HotViewBinder 已存在且配置一致,跳过");
            return null;
        }
        else
        {
            Debug.Log($"[Phase9Binder] {path} HotViewBinder 更新类型: {binder.ViewTypeFullName} → {typeName}");
        }
        binder.ViewTypeFullName = typeName;
        PrefabUtility.SaveAsPrefabAsset(go, path); // 覆盖保存同路径 → prefab GUID 不变
        return binder;
    }
}
