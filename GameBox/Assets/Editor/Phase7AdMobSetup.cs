using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build; // NamedBuildTarget(CS0103: UnityEditor.Build 命名空间,CI #19 抓出)
using UnityEngine;

/// <summary>
/// Phase 7 7-1 商业化编译符号管理（15 号文档 §2 验收：Android define = SUDOKU_ADMOB;SUDOKU_IAP）。
/// 背景：
/// - SUDOKU_IAP 直接可用：manifest.json 已声明 com.unity.purchasing，包解析后即编译通过。
/// - SUDOKU_ADMOB 必须先导入 google_mobile_ads v11.x .unitypackage，否则加了符号会编译失败，
///   因此本脚本负责「检测插件 → 存在则写入符号，不存在则弹窗提示」，保证导入顺序正确。
/// 菜单路径：Box/商业化/应用 AdMob+IAP 编译符号（可重复执行，幂等合并）。
/// CLI 无头执行方式（Jenkins）：
///   unity ... -executeMethod Phase7AdMobSetup.ApplyAdSymbols
/// </summary>
public static class Phase7AdMobSetup
{
    /// <summary>AdMob 插件的核心类型全名（用于探测插件是否已导入）。</summary>
    const string AdMobTypeName = "GoogleMobileAds.Api.MobileAds";

    /// <summary>
    /// Editor 菜单入口：检测 AdMob 插件是否导入，存在则把 SUDOKU_ADMOB;SUDOKU_IAP
    /// 幂等合并进 Android 编译符号；不存在则弹窗提示先导入插件。
    /// </summary>
    [MenuItem("Box/商业化/应用 AdMob+IAP 编译符号")]
    public static void ApplyAdSymbols()
    {
        if (!IsAdMobPluginImported())
        {
            EditorUtility.DisplayDialog(
                "AdMob 插件未导入",
                "未检测到 GoogleMobileAds.Api.MobileAds 类型。\n\n请先下载 google_mobile_ads v11.x " +
                "（https://github.com/googleads/googleads-mobile-unity/releases）并导入 .unitypackage，" +
                "然后在 Unity 中重新执行本菜单。\n\n说明：SUDOKU_IAP 符号会同时写入（manifest 已声明包，安全）。",
                "知道了");
            return;
        }

        // 插件已存在：合并 AdMob + IAP 符号（幂等：已含则跳过，不会重复追加）
        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
        var merged = MergeDefine(defines, "SUDOKU_ADMOB", "SUDOKU_IAP");
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, merged);
        Debug.Log($"[Phase7] Android 编译符号已更新：{merged}");

        // 提醒替换测试广告位 ID 的时机（账号申请后）
        Debug.Log("[Phase7] 记住：账号申请下来后将 AdMobAdsService.cs 中的测试广告位 ID 替换为真实 ID");
    }

    /// <summary>回退入口：移除 AdMob/IAP 符号（调试或切换桩实现时用）。</summary>
    [MenuItem("Box/商业化/移除 AdMob+IAP 编译符号")]
    public static void RemoveAdSymbols()
    {
        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
        var removed = string.Join(";", defines.Split(';')
            .Where(d => d != "SUDOKU_ADMOB" && d != "SUDOKU_IAP" && d.Length > 0));
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, removed);
        Debug.Log($"[Phase7] Android 编译符号已回退：{removed}");
    }

    /// <summary>把若干符号幂等合并进现有 define 字符串（分号分隔，保持原有符号顺序）。</summary>
    static string MergeDefine(string existing, params string[] symbols)
    {
        var list = existing.Split(';').Where(s => s.Length > 0).ToList();
        foreach (var s in symbols)
        {
            if (!list.Contains(s)) list.Add(s);
        }
        return string.Join(";", list);
    }

    /// <summary>探测 AdMob 插件类型是否已导入（遍历所有已加载程序集，兼容 dll/源码两种形态）。</summary>
    static bool IsAdMobPluginImported()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Any(t => t.FullName == AdMobTypeName);
    }
}