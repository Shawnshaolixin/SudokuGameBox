using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 诊断工具:加载 Addressables 本地构建产物,列出各 bundle 内的资产
/// (用于验证 TMP 字体 SDF / TTF 是否打进包——真机缺字与无字疑点)。
/// CLI 无头执行:unity ... -executeMethod InspectBundle.Run
/// </summary>
public static class InspectBundle
{
    const string BundleDir = "Library/com.unity.addressables/aa/Android/Android";

    public static void Run()
    {
        var dir = new DirectoryInfo(BundleDir);
        if (!dir.Exists)
        {
            Debug.LogError("[InspectBundle] 目录不存在: " + dir.FullName);
            return;
        }
        foreach (var f in dir.GetFiles("*.bundle"))
        {
            var ab = AssetBundle.LoadFromFile(f.FullName);
            if (ab == null)
            {
                Debug.LogError("[InspectBundle] 加载失败: " + f.Name);
                continue;
            }
            var names = ab.GetAllAssetNames();
            Debug.Log($"[InspectBundle] === {f.Name} ({names.Length} assets) ===");
            foreach (var n in names)
            {
                var obj = ab.LoadAsset(n);
                Debug.Log($"[InspectBundle]   {obj?.GetType().Name,24}  {n}");
            }
            ab.Unload(false);
        }
        Debug.Log("[InspectBundle] 完成");
    }
}
