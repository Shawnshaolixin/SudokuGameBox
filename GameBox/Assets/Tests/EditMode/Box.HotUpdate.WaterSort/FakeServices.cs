using System.Collections.Generic;
using Box.Services;
using UnityEngine;

namespace Box.HotUpdate.WaterSort.Tests
{
    /// <summary>
    /// 玩法侧测试专用内存存档(ISaveService;照数独同目录 FakeServices 复制,玩法各自独立副本):
    /// 不落盘,便于断言分区内容。加密/原子写/备份/恢复等属壳层职责(Box.Gameplay.Tests 覆盖)。
    /// 玩法侧职责:分区读写语义(M2.3 dailyDoneSeeds 落盘/读回)。
    /// </summary>
    sealed class FakeSaveService : ISaveService
    {
        readonly Dictionary<string, string> _modules = new Dictionary<string, string>();

        public int SchemaVersion => 1;
        public bool Exists => true;
        public string FilePath => "fake";

        public long Coins { get; set; }
        public string InstalledAt => "2026-08-22T00:00:00Z";
        public string LastModuleId { get; set; }

        public bool TryGetSignin(out string lastDate, out int streak)
        {
            lastDate = "";
            streak = 0;
            return false;
        }

        public void SetSignin(string lastDate, int streak) { }

        public T GetModule<T>(string moduleId) where T : class, new()
        {
            if (_modules.TryGetValue(moduleId, out var json))
            {
                try { return JsonUtility.FromJson<T>(json) ?? new T(); }
                catch { return new T(); }
            }
            return new T();
        }

        public void SetModule<T>(string moduleId, T data) where T : class
        {
            _modules[moduleId] = JsonUtility.ToJson(data);
        }

        /// <summary>测试断言用:分区原始 JSON。</summary>
        public string RawModuleJson(string moduleId) =>
            _modules.TryGetValue(moduleId, out var json) ? json : null;

        public void Save() { }
    }

    /// <summary>玩法侧测试专用内存偏好(ISettingsService;ServiceLocator.Register 成对注册用)。</summary>
    sealed class FakeSettingsService : ISettingsService
    {
        public bool SoundEnabled { get; set; } = true;
        public bool MusicEnabled { get; set; } = true;
        public int ThemeIndex { get; set; }
        public string Language { get; set; } = "zh";
        public void Save() { }
    }
}
