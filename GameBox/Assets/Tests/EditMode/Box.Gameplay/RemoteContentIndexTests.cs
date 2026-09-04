using Box.Gameplay.HotUpdate;
using NUnit.Framework;

namespace Box.Gameplay.Tests
{
    /// <summary>
    /// 版本化远程内容纯逻辑测试(2026-09-04 Phase 10-2 前置,布局见 tools/deploy_firebase.ps1 / 20 文档 §11):
    /// 覆盖 BuildRemoteBasePath 的分型规则(catalog 走版本目录 / bundle 走共享路径 / 版本未知回退旧路径)
    /// 与 TryParseIndexJson 的指针解析容错。网络拉取(ResolveRemoteVersionAsync)依赖真机环境,不在单测范围。
    /// </summary>
    public class RemoteContentIndexTests
    {
        const string ServerUrl = "https://sudokugamebox.web.app/staging";
        const string Version = "v20260904-153001";

        // ===== BuildRemoteBasePath:catalog 文件按版本分目录 =====

        /// <summary>catalog .bin + 有版本 → 指向 {服务器}/{版本},之后拼烘焙 id 里的 /Android/catalog_1.0.bin。</summary>
        [Test]
        public void BuildRemoteBasePath_CatalogBin_WithVersion_GoesToVersionDir()
        {
            var path = AddressablesHotUpdateSource.BuildRemoteBasePath(
                ServerUrl, Version, "RemoteHostURL/Android/catalog_1.0.bin");
            Assert.AreEqual(ServerUrl + "/" + Version, path);
        }

        /// <summary>catalog .hash 同规则(hash 与 bin 成对,客户端以 hash 做变更检测)。</summary>
        [Test]
        public void BuildRemoteBasePath_CatalogHash_WithVersion_GoesToVersionDir()
        {
            var path = AddressablesHotUpdateSource.BuildRemoteBasePath(
                ServerUrl, Version, "RemoteHostURL/Android/catalog_1.0.hash");
            Assert.AreEqual(ServerUrl + "/" + Version, path);
        }

        /// <summary>扩展名大小写不敏感(烘焙 id 的命名约定虽固定,防御性覆盖)。</summary>
        [Test]
        public void BuildRemoteBasePath_CatalogExtensionCaseInsensitive()
        {
            var path = AddressablesHotUpdateSource.BuildRemoteBasePath(
                ServerUrl, Version, "RemoteHostURL/Android/catalog_1.0.BIN");
            Assert.AreEqual(ServerUrl + "/" + Version, path);
        }

        // ===== BuildRemoteBasePath:bundle 共享路径(URL 稳定 → 设备缓存跨版本命中) =====

        /// <summary>bundle + 有版本 → 仍落共享根路径(内容寻址,全版本共用一份,版本切换不重下)。</summary>
        [Test]
        public void BuildRemoteBasePath_Bundle_WithVersion_StaysSharedPath()
        {
            var path = AddressablesHotUpdateSource.BuildRemoteBasePath(
                ServerUrl, Version, "RemoteHostURL/Android/hotupdate_local_assets_all_7387a82a.bundle");
            Assert.AreEqual(ServerUrl, path);
        }

        // ===== BuildRemoteBasePath:版本未知 → 全部落共享旧路径(旧客户端兼容目录) =====

        /// <summary>指针从未解析成功(version=null)→ catalog 也走共享旧路径,保证可降级可用。</summary>
        [Test]
        public void BuildRemoteBasePath_NullVersion_FallsBackToSharedPath()
        {
            var catalog = AddressablesHotUpdateSource.BuildRemoteBasePath(
                ServerUrl, null, "RemoteHostURL/Android/catalog_1.0.bin");
            var bundle = AddressablesHotUpdateSource.BuildRemoteBasePath(
                ServerUrl, null, "RemoteHostURL/Android/hotupdate_local_assets_all_7387a82a.bundle");
            Assert.AreEqual(ServerUrl, catalog, "版本未知时 catalog 应回退共享旧路径");
            Assert.AreEqual(ServerUrl, bundle);
        }

        /// <summary>version 为空串与 null 同语义(防御性覆盖)。</summary>
        [Test]
        public void BuildRemoteBasePath_EmptyVersion_FallsBackToSharedPath()
        {
            var path = AddressablesHotUpdateSource.BuildRemoteBasePath(
                ServerUrl, "", "RemoteHostURL/Android/catalog_1.0.bin");
            Assert.AreEqual(ServerUrl, path);
        }

        // ===== TryParseIndexJson:指针解析与容错 =====

        /// <summary>合法指针(与 deploy_firebase.ps1 Write-ChannelIndex 产出字段一致)→ 全字段解析。</summary>
        [Test]
        public void TryParseIndexJson_ValidJson_ParsesAllFields()
        {
            const string json = "{" +
                                "\"version\": \"v20260904-153001\"," +
                                "\"channel\": \"staging\"," +
                                "\"catalogHash\": \"abc123\"," +
                                "\"deployedAt\": \"2026-09-04 15:30:01 +08:00\"," +
                                "\"previousVersion\": \"v20260903-080000\"" +
                                "}";
            var ok = AddressablesHotUpdateSource.TryParseIndexJson(json, out var index);
            Assert.IsTrue(ok, "合法指针应解析成功");
            Assert.AreEqual("v20260904-153001", index.version);
            Assert.AreEqual("staging", index.channel);
            Assert.AreEqual("abc123", index.catalogHash);
            Assert.AreEqual("v20260903-080000", index.previousVersion);
        }

        /// <summary>缺少 version 字段(或为空)= 无效指针——没有版本目录名,版本化路径无从拼起。</summary>
        [Test]
        public void TryParseIndexJson_MissingVersion_Invalid()
        {
            const string json = "{\"channel\": \"staging\", \"catalogHash\": \"abc123\"}";
            var ok = AddressablesHotUpdateSource.TryParseIndexJson(json, out var index);
            Assert.IsFalse(ok, "缺 version 的指针应判无效");
            Assert.IsNull(index);
        }

        /// <summary>空串/非 JSON 输入 → false,不抛异常(调用方 ResolveRemoteVersionAsync 依赖其静默容错)。</summary>
        [Test]
        public void TryParseIndexJson_EmptyOrGarbage_ReturnsFalseWithoutThrow()
        {
            Assert.IsFalse(AddressablesHotUpdateSource.TryParseIndexJson(null, out _));
            Assert.IsFalse(AddressablesHotUpdateSource.TryParseIndexJson("", out _));
            Assert.IsFalse(AddressablesHotUpdateSource.TryParseIndexJson("{ broken json !!!", out _));
        }
    }
}
