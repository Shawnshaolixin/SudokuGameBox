using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 诊断工具:单独重跑 Addressables 内容构建(不打 Player),观察依赖收集日志。
/// CLI 无头执行:unity ... -executeMethod RebuildAddressables.Run
/// </summary>
public static class RebuildAddressables
{
    public static void Run()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        Debug.Log($"[RebuildAA] 构建器索引 {settings.ActivePlayerDataBuilderIndex}, 开始 BuildPlayerContent...");
        AddressableAssetSettings.BuildPlayerContent();
        Debug.Log("[RebuildAA] BuildPlayerContent 完成");
    }
}
