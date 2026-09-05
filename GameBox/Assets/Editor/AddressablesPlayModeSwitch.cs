using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Addressables Play Mode 切换器(CLI 批处理用):Asset Database ↔ Use Existing Build。
/// 用法:unity -batchmode -quit -executeMethod AddressablesPlayModeSwitch.UseAssetDatabase
/// 或 ...UseExistingBuild(编译零 error CS 后生效;项目 DataBuilders:FastMode=索引 0,PackedPlayMode=索引 1)。
/// 背景:Play Mode 索引存 Library/AddressablesConfig.dat(ProjectConfigData.ActivePlayModeIndex,不入库),
/// 只能经编辑器 API 写——菜单/CLI 手工改 .asset 均无效。本工具按脚本类型名定位索引,
/// 不依赖 DataBuilders 列表顺序(构建脚本 PackedMode 混在列表中也安全)。
/// 「Use Existing Build」直读上次打包产物(catalog+bundle,Library/com.unity.addressables/aa),
/// 用于随包链路自验:与 APK 内打包内容同源,真机问题可在编辑器先行复现(19 文档 §10 车辆)。
/// </summary>
public static class AddressablesPlayModeSwitch
{
    /// <summary>按 DataBuilder 脚本类型名找索引(找不到返回 -1)。</summary>
    static int IndexOfBuilder(string typeName)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings?.DataBuilders == null) return -1;
        for (int i = 0; i < settings.DataBuilders.Count; i++)
            if (settings.DataBuilders[i] != null && settings.DataBuilders[i].GetType().Name == typeName)
                return i;
        return -1;
    }

    static void SetPlayMode(string label, string typeName)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[PlayModeSwitch] Addressables 未初始化(先执行 Phase6AddressablesSetup.EnsureSetup)");
            return;
        }
        int index = IndexOfBuilder(typeName);
        if (index < 0)
        {
            Debug.LogError($"[PlayModeSwitch] 找不到 DataBuilder: {typeName}(当前列表:" +
                           $"{string.Join(",", settings.DataBuilders.ConvertAll(b => b?.GetType().Name))})");
            return;
        }
        ProjectConfigData.ActivePlayModeIndex = index; // 立即写 Library/AddressablesConfig.dat
        Debug.Log($"[PlayModeSwitch] Play Mode 已切 {label}(builder 索引 {index}/{settings.DataBuilders.Count})");
    }

    /// <summary>切 Use Asset Database(编辑器直接读资产,最快;默认/日常开发模式)。</summary>
    [MenuItem("Box/Addressables/Play Mode: Use Asset Database")]
    public static void UseAssetDatabase() => SetPlayMode("Use Asset Database", "BuildScriptFastMode");

    /// <summary>切 Use Existing Build(直读打包 catalog+bundle;验证打包内容链路时用)。</summary>
    [MenuItem("Box/Addressables/Play Mode: Use Existing Build")]
    public static void UseExistingBuild() => SetPlayMode("Use Existing Build", "BuildScriptPackedPlayMode");
}
