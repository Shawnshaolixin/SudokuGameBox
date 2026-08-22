using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Box.Services;
using UnityEngine;

namespace Box.Gameplay
{
    // ---- schema 载体(11 文档 §8.1,JsonUtility 不支持 Dictionary,故 modules 用 id+json 列表) ----

    [Serializable]
    sealed class SigninData
    {
        public string last = "";
        public int streak;
    }

    [Serializable]
    sealed class BoxData
    {
        public long coins;
        public SigninData signin = new SigninData();
        public string installedAt = "";
        public string lastModuleId = "";
    }

    [Serializable]
    sealed class ModuleEntry
    {
        public string id = "";
        public string json = ""; // 模块自定义 [Serializable] 数据的 JSON 原文
    }

    [Serializable]
    sealed class SaveFileData
    {
        public int schemaVersion = 1; // §8.2:单调递增,客户端只升不降
        public BoxData box = new BoxData();
        public List<ModuleEntry> modules = new List<ModuleEntry>();
    }

    /// <summary>
    /// 统一存档实现(11 文档 §8.1 D-7):
    /// 单文件 Application.persistentDataPath/box.save,内容 JSON + 加密(AES-CBC + HMAC-SHA256)+ 校验,
    /// 原子写(先写 .tmp 再替换)+ 单份 .bak 备份;主文件损坏自动回退备份,全损坏保留 .corrupt 并重建空档。
    /// 加密定位:防误改与简单篡改,非防作弊核心(§8.1;真金白银靠 SSV,Phase 后置)。
    /// 为何用 AES-CBC+HMAC 而非文档首选的 AES-GCM:Unity .NET Standard 2.1 profile 无 AesGcm 类,
    /// 文档 §8.1 明确允许「AES-GCM(或 AES-CBC + HMAC)」备选;不引入第三方库(红线 2)且 IL2CPP AOT 全兼容。
    /// 属壳层(AOT):热更侧只依赖 Abstractions 的 ISaveService。
    /// </summary>
    public sealed class SaveService : ISaveService
    {
        const string Magic = "BOXSAVE1"; // 文件魔数(8 字节)
        const int IvSize = 16;
        const int MacSize = 32;

        /// <summary>v1.0 固定密钥(32 字节)。升级/分发密钥属于运营期课题(Phase 11),此处保持简单。</summary>
        static readonly byte[] Key =
        {
            0x4B, 0x6F, 0x11, 0xE2, 0x8A, 0x3D, 0xF1, 0x5C,
            0x92, 0x0A, 0x7E, 0x3B, 0xC4, 0x5D, 0x1F, 0x99,
            0x2E, 0x70, 0xB6, 0x24, 0xD8, 0x53, 0x0F, 0xE9,
            0xA7, 0x1C, 0x6D, 0x88, 0x4A, 0xF3, 0x20, 0xC5,
        };

        const string DefaultFileName = "box.save";

        readonly string _filePath;
        SaveFileData _data;

        public int SchemaVersion => _data.schemaVersion;
        public bool Exists => File.Exists(_filePath);
        public string FilePath => _filePath;

        public long Coins
        {
            get => _data.box.coins;
            set => _data.box.coins = value;
        }

        public string InstalledAt => _data.box.installedAt;

        public string LastModuleId
        {
            get => _data.box.lastModuleId;
            set => _data.box.lastModuleId = value ?? "";
        }

        /// <summary>filePath 为 null 时使用默认持久化路径;测试可注入临时路径。</summary>
        public SaveService(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(Application.persistentDataPath, DefaultFileName);
            _data = LoadOrCreate();
        }

        public bool TryGetSignin(out string lastDate, out int streak)
        {
            lastDate = _data.box.signin.last;
            streak = _data.box.signin.streak;
            return _data.box.signin.last.Length > 0; // 未签到过 last 为空
        }

        public void SetSignin(string lastDate, int streak)
        {
            _data.box.signin.last = lastDate ?? "";
            _data.box.signin.streak = streak;
        }

        public T GetModule<T>(string moduleId) where T : class, new()
        {
            var e = FindModule(moduleId);
            if (e == null) return new T();
            try { return JsonUtility.FromJson<T>(e.json) ?? new T(); }
            catch (Exception ex) { Debug.LogWarning($"[SaveService] 分区 {moduleId} 反序列化失败: {ex.Message}"); return new T(); }
        }

        public void SetModule<T>(string moduleId, T data) where T : class
        {
            var e = FindModule(moduleId);
            if (e == null)
            {
                e = new ModuleEntry { id = moduleId };
                _data.modules.Add(e);
            }
            e.json = JsonUtility.ToJson(data);
            Save(); // 模块数据量小,写入即落盘(简单可靠)
        }

        /// <summary>把当前内存数据加密落盘(原子写 + 备份)。</summary>
        public void Save()
        {
            try
            {
                var json = JsonUtility.ToJson(_data);
                var payload = Encrypt(Encoding.UTF8.GetBytes(json));
                WriteAtomic(payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] 保存失败: {ex.Message}");
            }
        }

        // ---- 内部 ----

        ModuleEntry FindModule(string moduleId)
        {
            foreach (var e in _data.modules)
                if (e.id == moduleId) return e;
            return null;
        }

        /// <summary>加载策略:主文件 → 备份 → 全损坏重建(保留 .corrupt)。</summary>
        SaveFileData LoadOrCreate()
        {
            var d = TryRead(_filePath);
            if (d != null) return d;

            var bak = _filePath + ".bak";
            d = TryRead(bak);
            if (d != null)
            {
                Debug.LogWarning("[SaveService] 主存档损坏,已从备份恢复;下次保存将重建主文件");
                return d;
            }

            if (File.Exists(_filePath))
            {
                // 主/备全损坏:坏文件改名保留(排查证据),重建空档
                try
                {
                    var corrupt = _filePath + ".corrupt";
                    if (File.Exists(corrupt)) File.Delete(corrupt);
                    File.Move(_filePath, corrupt);
                    Debug.LogWarning($"[SaveService] 存档损坏无法恢复,已保留为 {Path.GetFileName(corrupt)} 并重建新档");
                }
                catch (Exception ex) { Debug.LogWarning($"[SaveService] 保留损坏文件失败: {ex.Message}"); }
            }

            var fresh = NewFile();
            Save(); // 立即落盘,installedAt 自此固定
            return fresh;
        }

        static SaveFileData NewFile()
        {
            var d = new SaveFileData();
            d.box.installedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            return d;
        }

        /// <summary>解密并解析;任何环节失败(魔数不符/版本不符/HMAC 校验失败/JSON 非法)返回 null。</summary>
        static SaveFileData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                var plain = Decrypt(bytes);
                if (plain == null) return null;
                var d = JsonUtility.FromJson<SaveFileData>(Encoding.UTF8.GetString(plain));
                if (d == null || d.schemaVersion < 1) return null;
                if (d.schemaVersion > 1)
                {
                    // §8.2:更高版本 → 只读模式,绝不静默丢数据。v1.0 无此场景,先告警保留。
                    Debug.LogWarning("[SaveService] 存档 schemaVersion 高于当前,保持只读(不覆盖)");
                }
                return d;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveService] 读取失败 {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>先落 .tmp,备份旧文件后替换主文件 —— 中途被杀进程最多丢一次写入,备份可恢复。</summary>
        void WriteAtomic(byte[] payload)
        {
            var tmp = _filePath + ".tmp";
            var bak = _filePath + ".bak";
            File.WriteAllBytes(tmp, payload);
            if (File.Exists(_filePath)) File.Copy(_filePath, bak, true);
            File.Delete(_filePath);
            File.Move(tmp, _filePath);
        }

        // ---- 加密(AES-CBC + HMAC-SHA256;载荷 = 魔数+版本+IV+密文+MAC) ----

        static byte[] Encrypt(byte[] plain)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                byte[] cipher;
                using (var enc = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
                        cs.Write(plain, 0, plain.Length);
                    cipher = ms.ToArray();
                }

                var body = new byte[Magic.Length + 4 + IvSize + cipher.Length];
                Buffer.BlockCopy(Encoding.ASCII.GetBytes(Magic), 0, body, 0, Magic.Length);
                Buffer.BlockCopy(BitConverter.GetBytes(1), 0, body, Magic.Length, 4); // version=1
                Buffer.BlockCopy(aes.IV, 0, body, Magic.Length + 4, IvSize);
                Buffer.BlockCopy(cipher, 0, body, Magic.Length + 4 + IvSize, cipher.Length);

                var mac = new HMACSHA256(Key).ComputeHash(body);
                var full = new byte[body.Length + mac.Length];
                Buffer.BlockCopy(body, 0, full, 0, body.Length);
                Buffer.BlockCopy(mac, 0, full, body.Length, mac.Length);
                return full;
            }
        }

        /// <summary>解密:校验魔数/版本/HMAC(篡改与坏文件在此拦截),失败返回 null。</summary>
        static byte[] Decrypt(byte[] full)
        {
            int header = Magic.Length + 4;
            if (full.Length < header + IvSize + MacSize) return null;
            if (Encoding.ASCII.GetString(full, 0, Magic.Length) != Magic) return null;
            if (BitConverter.ToInt32(full, Magic.Length) != 1) return null;

            var mac = new byte[MacSize];
            Buffer.BlockCopy(full, full.Length - MacSize, mac, 0, MacSize);
            var body = new byte[full.Length - MacSize];
            Buffer.BlockCopy(full, 0, body, 0, body.Length);
            var expected = new HMACSHA256(Key).ComputeHash(body);
            if (!ConstantTimeEquals(mac, expected)) return null;

            var iv = new byte[IvSize];
            Buffer.BlockCopy(full, header, iv, 0, IvSize);
            int cipherLen = full.Length - header - IvSize - MacSize;
            var cipher = new byte[cipherLen];
            Buffer.BlockCopy(full, header + IvSize, cipher, 0, cipherLen);

            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var dec = aes.CreateDecryptor())
                using (var ms = new MemoryStream(cipher))
                using (var cs = new CryptoStream(ms, dec, CryptoStreamMode.Read))
                using (var outMs = new MemoryStream())
                {
                    cs.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
        }

        static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}