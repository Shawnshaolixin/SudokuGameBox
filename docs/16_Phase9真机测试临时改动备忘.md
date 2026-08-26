# 16 — Phase 9 真机测试临时改动备忘（恢复指南）

> 目的：记录 2026-08-25 真机验证广告链路期间的所有改动，区分「正式值」与「临时值」，
> 上架前按本表恢复。改动均为可逆配置/常量，无结构性代码变更。

## 改动总览

| # | 文件 | 改动项 | 当前值 | 类型 | 上架前动作 |
|---|---|---|---|---|---|
| 1 | `Assets/Services/Ads/AdMobAdsService.cs` | `TestDeviceIds` | 真机 ID `AAC1C00E2A99B28A43349D7BD59ADE49` | **临时** | 发布版移除（否则该设备永收测试广告） |
| 2 | 同上 | `UmpEnabled` | `false`（UMP 禁用） | **临时** | 面向欧美市场时置 `true` 并配置后台表单 |
| 3 | 同上 | 广告单元 ID（激励/插屏） | 用户真实广告位 | 正式 | 无 |
| 4 | `Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset` | `adMobAndroidAppId` | 用户真实 App ID | 正式 | 无 |
| 5 | 同上 | `selectedGmaAndroidSdk` / `overrideDefaultGmaAndroidSdk` | `0` / `1`（Standard SDK） | **待定** | 账号获批后验证固化 |
| 6 | `ProjectSettings/ProjectSettings.asset` | `applicationIdentifier.Android` | `com.lixingames.rovilo` | 正式 | 无（用户拍板） |

## 逐项说明

### 1. TestDeviceIds（临时，发布前必恢复）

- 位置：[AdMobAdsService.cs](Assets/Services/Ads/AdMobAdsService.cs) 顶部 `TestDeviceIds` 列表
- 目的：真机请求广告时 SDK 返回**测试广告**（防无效流量违规）
- 恢复：改为空列表 `private static readonly List<string> TestDeviceIds = new();`
- 注意：发布前若仍想在自己手机上验证真实广告，可保留此设备 ID（自己手机无害）；
  但**必须**确认列表里没有其他测试人员的设备 ID。

### 2. UmpEnabled = false（临时，面向欧美前恢复）

- 位置：`AdMobAdsService.cs` 内 `private const bool UmpEnabled`
- 背景：GDPR 只约束欧洲（EEA）用户；国内访问 consent.google.com 挂起会阻塞广告初始化。
  真实 App ID 未配置 UMP 表单时原生调用也可能挂起。
- 恢复：置 `true`，并先在 AdMob 后台「隐私与消息」创建同意消息（否则报
  `no form(s) configured for the input app ID`）。
- 保留项：`UmpFlowWithTimeout()`（15 秒超时兜底）与 `Initialize()` 的分支逻辑**保留**，
  开关恢复即生效，无需改代码结构。

### 3 & 4. 真实广告位 ID / App ID（正式，勿改回）

- 广告单元 ID：激励 `ca-app-pub-6367116322180531/5022991846`、插屏 `.../4813896836`
- App ID：`ca-app-pub-6367116322180531~1995277327`（manifest 由
  `GoogleMobileAdsSettings.asset` 构建时自动注入，勿手动改 manifest）
- 若将来迁移广告位，只需替换常量与 Settings 字段，勿动其他代码。

### 5. SDK 架构（当前 Standard，待账号获批后固化）

- 背景：Standard SDK 25.4.0 下官方**激励测试位**报 `code=0 Internal error`（测试位兼容问题，
  真实广告位不受影响）；Next-Gen 1.3.1 下测试位报 `code=3 No fill`（请求已到服务器）。
- 当前选择 Standard（`selectedGmaAndroidSdk = 0`），原因：真实位在 Standard 下报
  `Account not approved yet`（明确错误码），Next-Gen 下误报 HTTP 400——Standard 行为更清晰。
- 回退方法：Settings 中 `selectedGmaAndroidSdk` 改 `1`、`overrideDefaultGmaAndroidSdk` 改 `1`。
- 验证标准：AdMob 账号审核通过后，真实激励/插屏位能加载即固化当前架构；否则换架构复测。

### 5.5 AdMob 账号审核（2026-08-25 关键发现）

- 根因：**新注册 AdMob 账号处于审核期（1~14 天），审核通过前广告单元不返回广告**。
  - 官方测试位绕过审核（人人可用）→ 解释了测试位能加载、真实位全部失败
  - Next-Gen 将「未获批」映射为 HTTP 400，Standard 报 `Account not approved yet`
- 当前状态：真实广告位已配置在代码中，**审核通过后无需改代码，广告自动可用**。
- 待办：用户完成 AdMob 后台政策中心/账号信息待办项以加快审核。

### 6. 包名（正式，用户拍板）

- `com.lixingames.rovilo`，与 AdMob 广告单元、将来 Play Console 应用保持一致。

## 已还原 / 放弃的尝试（无需处理）

| 尝试 | 结果 |
|---|---|
| play-services-ads 降级 22.6.0（`AndroidBuildPreProcessor.cs`） | **已还原 25.4.0**；插件 aar 与旧版不兼容导致启动卡死 |
| UMP 15 秒超时兜底 | 正式保留（`UmpFlowWithTimeout`） |
| 激励视频持续重试（60s×2 → 5min 无限） | 正式保留（防测试位波动导致广告永不可用） |

## 恢复操作清单（上架前）

```text
1. AdMobAdsService.cs: TestDeviceIds → 空列表（或确认无他人设备）
2. AdMobAdsService.cs: UmpEnabled → true（且 AdMob 后台已配置同意消息）
3. 账号获批后按第 5 项验证标准固化当前 SDK 架构（或换 Next-Gen 复测）
4. 回归测试:激励/插屏加载 + 展示 + 重试机制
```
