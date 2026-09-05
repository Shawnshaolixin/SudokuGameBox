#if SUDOKU_FIREBASE
using System;
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using UnityEngine;

namespace Box.Services
{
    /// <summary>
    /// 分析真实现（Phase 11 前置：封闭测试前提前接入，2026-08 用户拍板）：
    /// Firebase Analytics 埋点 + Crashlytics 崩溃/非致命上报（08 文档 §6）。
    /// 与 AdMob/IAP 真实现相同的 #if SUDOKU_FIREBASE 编译开关设计：
    /// 未定义符号时本文件不参与编译，AppBootstrap 自动回退 AnalyticsServiceStub。
    /// 依赖 Assets/google-services.json（Firebase 控制台下载）；缺失时
    /// CheckAndFixDependenciesAsync 返回 Unavailable，埋点静默丢弃、崩溃不上报 —— 不影响游戏运行。
    /// 事件契约沿用 08 文档 §6.5 / 10 文档 §8.4（sudoku.level_start 等，由玩法层既有调用点上报）。
    /// </summary>
    public sealed class FirebaseAnalyticsService : IAnalyticsService
    {
        /// <summary>Firebase 依赖修复是否完成（首次成功后将一直可用）。</summary>
        private bool _available;

        /// <summary>
        /// 初始化 Firebase：检查并修复依赖（Android 需 google-services.json + Google Play 服务）。
        /// 异步完成；完成前调用埋点会静默丢弃（事件量小，无需缓冲）。
        /// 成功后上报 services_initialized，作为 Firebase 链路打通的验收信号（后台看到即接通）。
        /// </summary>
        public void Initialize()
        {
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
            {
                try
                {
                    var status = task.Result;
                    if (status != DependencyStatus.Available)
                    {
                        Debug.LogWarning($"[Firebase] 依赖修复失败:{status}(检查 Assets/google-services.json 与 Google Play 服务)");
                        return;
                    }
                    _available = true;
                    LogEvent("services_initialized"); // 链路验收信号:后台出现此事件 = SDK 打通
                    Debug.Log("[Firebase] Analytics + Crashlytics 初始化完成");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Firebase] 初始化异常:{e.Message}");
                }
            });
        }

        public void LogEvent(string eventName) => Log(eventName, null, null);

        public void LogEvent(string eventName, string parameterName, object parameterValue)
            => Log(eventName, parameterName, parameterValue);

        /// <summary>
        /// 统一事件上报：GA4 参数仅支持 string/long/double，bool 转 0/1，其余转字符串。
        /// 依赖未就绪时静默丢弃（与桩行为一致，不阻塞业务链路）。
        /// 事件名先过一次契约校验(AnalyticsEvents,04 文档 §6.1)——非法名会被 FA SDK 静默
        /// 拒收(2026-09-05 带点/斜杠命名全丢的教训),违规打 Warning 便于开发期发现。
        /// </summary>
        private void Log(string eventName, string parameterName, object parameterValue)
        {
            if (!_available) return;
            if (!AnalyticsEvents.IsValidName(eventName))
            {
                Debug.LogWarning($"[Firebase] 埋点事件名非法被丢弃(仅 [a-z0-9_] 且字母开头 ≤40,04 文档 §6.1): {eventName}");
                return;
            }
            try
            {
                if (parameterValue == null)
                {
                    FirebaseAnalytics.LogEvent(eventName);
                    return;
                }
                switch (parameterValue)
                {
                    case string s: FirebaseAnalytics.LogEvent(eventName, parameterName, s); break;
                    case bool b:   FirebaseAnalytics.LogEvent(eventName, parameterName, b ? 1L : 0L); break;
                    case int i:    FirebaseAnalytics.LogEvent(eventName, parameterName, (long)i); break;
                    case long l:   FirebaseAnalytics.LogEvent(eventName, parameterName, l); break;
                    case float f:  FirebaseAnalytics.LogEvent(eventName, parameterName, (double)f); break;
                    case double d: FirebaseAnalytics.LogEvent(eventName, parameterName, d); break;
                    default:       FirebaseAnalytics.LogEvent(eventName, parameterName, parameterValue.ToString()); break;
                }
            }
            catch (Exception e)
            {
                // 埋点失败不上抛、不重试:分析链路异常不能影响游戏本体
                Debug.LogWarning($"[Firebase] 埋点失败:{eventName} → {e.Message}");
            }
        }

        public void LogNonFatal(string message)
        {
            if (!_available) return;
            try
            {
                // 非致命错误以异常形式记录,Crashlytics 后台「非致命错误」分类可见(08 文档 §6.4)
                Crashlytics.LogException(new Exception(message));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Firebase] 非致命上报失败:{e.Message}");
            }
        }
    }
}
#endif
