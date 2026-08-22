using System;
using System.IO;
using System.Text;
using Box.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// SaveService 测试(Phase 5 5-1,D-7 加密单文件分区存档):
    /// 临时目录注入隔离(不碰真机路径);覆盖 加密落盘/备份/损坏回退/box 字段/跨实例持久。
    /// 职责边界:壳层只管文件+box.*;玩法分区读写与迁移在 Box.HotUpdate.Sudoku.Tests(见 DailyChallengeTests)。
    /// </summary>
    public class SaveServiceTests
    {
        string _dir;

        // 测试需跨实例断言的文件名
        const string ModuleId = "sudoku";

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "SaveTests" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { /* 尽力清理 */ }
        }

        string PathOf(string name) => Path.Combine(_dir, name + ".save");

        [Serializable]
        sealed class TestModuleData
        {
            public int progress;
            public string note = "";
        }

        // ---- 往返与持久化 ----

        [Test]
        public void Module_RoundTrip_Across_Instances()
        {
            var path = PathOf("a");
            var svc1 = new SaveService(path);
            svc1.SetModule(ModuleId, new TestModuleData { progress = 42, note = "你好" });

            var svc2 = new SaveService(path); // 新实例重新解密加载
            var d = svc2.GetModule<TestModuleData>(ModuleId);
            Assert.AreEqual(42, d.progress);
            Assert.AreEqual("你好", d.note, "中文必须无损(UTF-8)");
        }

        [Test]
        public void Save_File_Is_Encrypted_Not_Plaintext()
        {
            var path = PathOf("e");
            var svc = new SaveService(path);
            svc.Coins = 999;
            svc.SetModule(ModuleId, new TestModuleData { progress = 7 });
            svc.Save();

            var bytes = File.ReadAllBytes(path);
            var text = Encoding.UTF8.GetString(bytes);
            Assert.IsFalse(text.Contains("schemaVersion"), "不得出现明文 schemaVersion(加密落盘)");
            Assert.IsFalse(text.Contains("999"), "coins 不得明文");
            Assert.IsTrue(text.Contains("BOXSAVE1"), "应包含魔数标记(加密容器,非裸 JSON)");
        }

        [Test]
        public void Save_Creates_Backup_And_No_Tmp_Left()
        {
            var path = PathOf("b");
            var svc = new SaveService(path);
            svc.Save();                      // 第一次写:主文件
            svc.Coins = 100;
            svc.Save();                      // 第二次写:形成 .bak(上一次版本)
            svc.Coins = 200;
            svc.Save();                      // 第三次写:bak=coins100

            Assert.IsTrue(File.Exists(path), "主文件存在");
            Assert.IsTrue(File.Exists(path + ".bak"), "备份存在");
            Assert.IsFalse(File.Exists(path + ".tmp"), "临时文件必须清理干净");
        }

        [Test]
        public void Backup_Fallback_When_Main_Corrupt()
        {
            var path = PathOf("c");
            var svc = new SaveService(path);
            svc.Coins = 100;
            svc.Save();
            svc.Coins = 200;
            svc.Save(); // bak 停留在 coins=100 版本

            File.WriteAllBytes(path, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }); // 故意损坏主文件

            var recovered = new SaveService(path);
            Assert.AreEqual(100, recovered.Coins, "主文件损坏 → 自动回退到备份版本(coins=100)");
            recovered.Save(); // 回退后保存 → 主文件重建
            Assert.AreEqual(100, new SaveService(path).Coins, "重建后仍可读");
        }

        [Test]
        public void All_Corrupt_Keeps_Evidence_And_Creates_Fresh()
        {
            var path = PathOf("d");
            var svc = new SaveService(path);
            svc.Coins = 300;
            svc.Save();
            svc.Save(); // 确保 bak 存在

            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });          // 损坏主
            File.WriteAllBytes(path + ".bak", new byte[] { 9, 9 });    // 损坏备份

            var fresh = new SaveService(path);
            Assert.IsTrue(File.Exists(path + ".corrupt"), "损坏证据保留为 .corrupt,不静默覆盖");
            Assert.AreEqual(0, fresh.Coins, "全损坏 → 重建空档");
            Assert.IsTrue(fresh.Exists, "重建后 Exists=true(主文件已落盘)");
        }

        [Test]
        public void Tampered_File_Treated_As_Corrupt()
        {
            var path = PathOf("t");
            var svc = new SaveService(path);
            svc.Coins = 555;
            svc.Save();

            var bytes = File.ReadAllBytes(path);
            bytes[bytes.Length - 1] ^= 0xFF; // 翻转 MAC 末字节 → HMAC 校验必失败(防篡改)
            File.WriteAllBytes(path, bytes);

            var svc2 = new SaveService(path);
            Assert.AreEqual(0, svc2.Coins, "HMAC 失败 → 视为损坏,绝不接受被篡改数据");
        }

        // ---- box 字段(§8.1:box.* 仅 Shell 可写) ----

        [Test]
        public void Box_Fields_RoundTrip()
        {
            var path = PathOf("f");
            var svc = new SaveService(path);
            svc.Coins = 12345;
            svc.LastModuleId = "sudoku";
            svc.SetSignin("2026-08-22T00:00:00Z", 3);
            svc.Save();

            var svc2 = new SaveService(path);
            Assert.AreEqual(12345, svc2.Coins);
            Assert.AreEqual("sudoku", svc2.LastModuleId);
            Assert.IsTrue(svc2.TryGetSignin(out var last, out var streak));
            Assert.AreEqual("2026-08-22T00:00:00Z", last);
            Assert.AreEqual(3, streak, "连续签到 3 天");
        }

        [Test]
        public void InstalledAt_Stable_Across_Instances()
        {
            var path = PathOf("i");
            var svc = new SaveService(path);
            var first = svc.InstalledAt;
            Assert.IsFalse(string.IsNullOrEmpty(first), "首装时间应写入");

            var svc2 = new SaveService(path);
            Assert.AreEqual(first, svc2.InstalledAt, "多次启动安装时间不变");
        }

        // ---- 分区覆盖语义 ----

        [Test]
        public void SetModule_Overwrites_Existing_Entry()
        {
            var path = PathOf("o");
            var svc = new SaveService(path);
            svc.SetModule(ModuleId, new TestModuleData { progress = 1 });
            svc.SetModule(ModuleId, new TestModuleData { progress = 2 }); // 同 id 覆盖

            var svc2 = new SaveService(path);
            Assert.AreEqual(2, svc2.GetModule<TestModuleData>(ModuleId).progress, "同 id 覆盖而非追加");
        }

        [Test]
        public void Unknown_Module_Returns_Fresh_Empty()
        {
            var path = PathOf("u");
            var svc = new SaveService(path);
            var d = svc.GetModule<TestModuleData>("nope");
            Assert.AreEqual(0, d.progress, "未写入的模块 → 空数据(不抛)");
            Assert.AreEqual("", d.note);
        }
    }
}