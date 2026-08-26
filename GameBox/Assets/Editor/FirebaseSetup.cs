using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build; // NamedBuildTarget(与 Phase7AdMobSetup 相同用法)
using UnityEngine;

/// <summary>
/// Firebase 编译符号管理（Phase 11 前置：封闭测试前提前接入，08 文档 §6）。
/// 背景：
/// - SDK 经 Packages/manifest.json 以本地 tgz 引入（com.google.firebase.app/analytics/crashlytics 13.15.0），
///   包解析完成即编译通过，无需像 AdMob 那样手动导入 unitypackage。
/// - 本脚本负责「检测 Firebase 类型 → 存在则把 SUDOKU_FIREBASE 幂等写入 Android 编译符号」，
///   未定义符号时 AppBootstrap 自动回退 AnalyticsServiceStub（不影响日常开发与 CI）。
/// 菜单路径：Box/商业化/应用 Firebase 编译符号（可重复执行，幂等合并）。
/// CLI 无头执行方式（Jenkins）：
///   unity ... -executeMethod FirebaseSetup.ApplyFirebaseSymbols
/// </summary>
public static class FirebaseSetup
{
    /// <summary>Firebase 核心类型全名（用于探测 SDK 是否已导入）。</summary>
    const string FirebaseTypeName = "Firebase.FirebaseApp";

    /// <summary>
    /// Editor 菜单入口：检测 Firebase SDK 是否导入，存在则把 SUDOKU_FIREBASE
    /// 幂等合并进 Android 编译符号；不存在则弹窗提示先确认 manifest 包解析。
    /// </summary>
    [MenuItem("Box/商业化/应用 Firebase 编译符号")]
    public static void ApplyFirebaseSymbols()
    {
        if (!IsFirebasePluginImported())
        {
            EditorUtility.DisplayDialog(
                "Firebase SDK 未导入",
                "未检测到 Firebase.FirebaseApp 类型。\n\n请确认 Packages/manifest.json 已声明 " +
                "com.google.firebase.app/analytics/crashlytics（本地 tgz），并让 Unity 完成包解析后重试。",
                "知道了");
            return;
        }

        // SDK 已存在：合并 Firebase 符号（幂等：已含则跳过，不会重复追加）
        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
        var merged = MergeDefine(defines, "SUDOKU_FIREBASE");
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, merged);
        Debug.Log($"[Firebase] Android 编译符号已更新：{merged}");

        // 提醒 google-services.json（Firebase 控制台下载，需用户操作）
        Debug.Log("[Firebase] 记住：将 Firebase 控制台下载的 google-services.json 放入 Assets/ 根目录");
    }

    /// <summary>回退入口：移除 Firebase 符号（调试或切换桩实现时用）。</summary>
    [MenuItem("Box/商业化/移除 Firebase 编译符号")]
    public static void RemoveFirebaseSymbols()
    {
        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
        var removed = string.Join(";", defines.Split(';')
            .Where(d => d != "SUDOKU_FIREBASE" && d.Length > 0));
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, removed);
        Debug.Log($"[Firebase] Android 编译符号已回退：{removed}");
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

    /// <summary>探测 Firebase 核心类型是否已导入（遍历所有已加载程序集，兼容 dll/源码两种形态）。</summary>
    static bool IsFirebasePluginImported()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Any(t => t.FullName == FirebaseTypeName);
    }
}
