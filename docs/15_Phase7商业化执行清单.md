# 15. Phase 7 — 商业化执行清单（Unity 6 时点）

> 版本：v1.0（2026-08-23）
> 目的：把 [08_阶段C_商业化接入指南.md](./08_阶段C_商业化接入指南.md) 的流程落地为**可勾选执行清单**，
> 并修正 SDK 版本到 **Unity 6（6000.3.20f1，D-12）** 时点的官方主线。
> 关联：[10_Phase执行计划.md](./10_Phase执行计划.md) §14、[11_Unity游戏盒子架构方案.md](./11_Unity游戏盒子架构方案.md) 决策台账。

---

## 0. 关键变化总览（相比 08 文档的更新）

08 文档写作时（2024 年前后）AdMob Unity 插件主线是 v8.x。**2026-08 官方最新已是 v11.x**，
且插件内置了 UMP 与 EDM4U，无需再单独安装。Unity IAP 仍为 v4.x 主线（Unity 6 Verified 版本）。

| 项 | 08 文档（旧） | **Unity 6 时点定版（新）** | 说明 |
|---|---|---|---|
| Google Mobile Ads Unity 插件 | v8.x | **v11.x（当前 v11.4.0，2026-08）** | 官方最新主线；内置 UMP(Android 4.0/iOS 3.1) 与 EDM4U 1.2.188；Android Next-Gen SDK 1.x |
| Google User Messaging Platform | 单独提 | **随 AdMob 插件内置，无需单独安装** | 08 文档 §4.4 的 UMP 代码改为插件自带 API |
| Unity IAP（com.unity.purchasing） | v4.x（如 4.12.x） | **保持 v4.x（4.12.x，Unity 6 Verified）** | Unity 官方 Registry 直接安装；v5 仍是新主线的过渡，暂不引入 |
| External Dependency Manager | 手动装 | **随 AdMob 插件自带（1.2.188）** | 导入后运行 `Assets → External Dependency Manager → Android → Resolve` 即可 |
| Firebase | v12.x | **Phase 11 再核对，本次不引入** | 链路③埋点留运营期（08 文档 §6） |

> ⚠️ **API 差异提示**：08 文档 §4.4 的代码讲解基于 v8 事件名（`OnUserEarnedReward` 等）。
> v9 起事件签名从 `EventHandler<T>` 改为 `Action<T>`，v11 进一步整合为统一事件回调。
> 因此 7-1 写 `AdMobAdsService.cs` 时**以 v11 官方 API 为准**（可参考
> [官方 Get Started](https://developers.google.com/admob/unity/start) 与插件自带示例），
> 08 文档仅作为"接口与业务逻辑"的参考。

**已核对的外部事实（2026-08-23）**：
- AdMob Unity 插件 GitHub Releases：最新 **v11.4.0**（2026-08 发布，4 天前），前序 v11.3.0 / v11.2.0 / v11.1.0 / v11.0.0（2026-02）。
- v11 使用 GMA Android Next-Gen SDK（1.3.1）+ UMP Android 4.0.0 / iOS 3.1.0 + EDM4U 1.2.188。
- **Next-Gen SDK 要求 `MobileAds.Initialize()` 在主线程调用**（Unity 主线程即可满足）。

---

## 1. 账号相关任务（⚠️ 全部延后，等账号申请下来再做）

> 以下任务依赖 Google 账号，**当前不执行**，仅登记占位，避免遗漏。

| # | 账号任务 | 依赖 | 备注 |
|---|---|---|---|
| A1 | Google Play Console 开发者账号（$25 一次性） | 需信用卡 + Google 账号 | 账号下来后填 Play Console 各表单 |
| A2 | AdMob 账号 + 创建应用 → 创建真实广告位（激励视频 / 插屏）拿真实 ID | A1 | 未拿到前用官方**测试广告位 ID**（见 7-1） |
| A3 | Play Console 创建商品 `remove_ads`（非消耗型，$3.99） | A1 | 商品 ID 必须与代码 `RemoveAdsProductId` 一致 |
| A4 | Play Console 开通结算（创收设置 → 商家账户） | A1 | 内购真实生效必需 |
| A5 | 许可测试账号（License testing） | A1 | 用测试账号真机购买不扣费 |
| A6 | 隐私政策 URL 托管（GitHub Pages） | GitHub 仓库公开 + Pages 开启 | 页面已生成：`docs/privacy-policy.html`（本次提交）；拿到 URL 后替换 SettingsView 占位地址 |
| A7 | keystore 签名配置（Generate + 上传 Play App Signing） | A1（上传动作） | **证书可现在就生成**（第 4 节教程），上传等账号 |

**结论**：账号相关不影响 7-1 代码接线与 7-2 同意流程搭建；7-3 合规表单的**提交动作**需账号，但**清单核对**现在就可以做。

---

## 2. 7-1 AdMob（激励+插屏）+ Unity IAP 接入（代码任务，不依赖账号）

### 2.1 安装 SDK（⚠️ 红线 7：需用户授权改 Packages）

1. 下载 AdMob Unity 插件 **v11.4.0** `.unitypackage`（[GitHub Releases](https://github.com/googleads/googleads-mobile-unity/releases)），双击导入。
2. 确认 `Assets/GoogleMobileAds/` 出现；导入后执行 `Assets → External Dependency Manager → Android → Resolve`。
3. UPM 安装 Unity IAP：`Window → Package Manager → Packages: Unity Registry → In App Purchasing`，选 **4.12.x**。
   - 会自动改 `Packages/manifest.json`（**红线 7**：修改 Packages 需用户在进入 7-1 前确认授权）。
4. 测试安装完 **不定义符号** 先编译一次，确认工程无报错（插件为纯 SDK，不影响现有代码）。

### 2.2 定义编译符号（Android 平台）

`Player Settings → Publishing Settings → Scripting Define Symbols`（Android 行）增加：

```
SUDOKU_ADMOB;SUDOKU_IAP
```

> 08 文档 §9 速查表：`SUDOKU_ADMOB` → 广告真实现；`SUDOKU_IAP` → 内购真实现；
> 都不定义 → 走桩实现（Stub），当前工程默认即此状态。

### 2.3 代码任务清单

| 文件 | 内容 | 说明 |
|---|---|---|
| `GameBox/Assets/Services/Ads/AdMobAdsService.cs`（新建） | 实现 `IAdsService`：`Initialize`、`ShowRewardedAd`、插屏管理、`IsAdsRemoved` | v11 API：`MobileAds.Initialize`（主线程）、`RewardedAd.Load` + 统一事件回调、`InterstitialAd`；UMP：`ConsentInformation.Update` → `ConsentForm.LoadAndShowConsentFormIfRequired`；频控（新用户前 3 局零插屏、局间隔 4~6 分钟、去广告零广告） |
| `GameBox/Assets/Services/Iap/UnityIapService.cs`（新建） | 实现 `IIapService`：`Initialize`、`BuyRemoveAds`、`RestorePurchases`、`PurchaseCompleted` 事件 | v4 API：`ConfigurationBuilder` + `StandardPurchasingModule.Instance(AppStore.GooglePlay)` + `IDetailedStoreListener`；商品 `remove_ads` 非消耗型；启动时收据校验自动恢复 |
| `GameBox/Assets/Services/Analytics/`（本次不建） | — | 链路③ Firebase 留 Phase 11，继续用 `AnalyticsServiceStub` |
| `GameBox/Assets/Gameplay/AppBootstrap.cs`（修改） | 按 `#if SUDOKU_ADMOB / SUDOKU_IAP` 用真实现替换 AdMob/IAP 的 Stub 注册 | 当前 Bootstrap 只传 `AnalyticsServiceStub`，Ads/Iap 由调用方从 `ServiceLocator` 取；需补注册真实现 |
| `GameBox/Assets/Gameplay/StubServices.cs` | 保持不动 | 桩实现保留：未定义符号时仍可跑通流程 |
| 存档接入（改） | "去广告"状态写入 D-7 存档分区（如 `box.commerce`），不再用 Stub 的 PlayerPrefs 键 | 需与存档 Service 约定分区读写接口；D-7 是 AES-GCM 单文件存档 |
| 广告位 ID（占位） | `RewardedAdUnitId` / `InterstitialAdUnitId` / `RemoveAdsProductId` 先用官方测试值 | 测试广告位 ID 见 08 文档 §4.1/§5.1 引用的官方常量：激励 `ca-app-pub-3940256099942544/5224354917`、插屏 `.../1033173712`、横幅 `.../6300978111`（以官方最新文档为准）；真 ID 等 A2/A3 |

### 2.4 验证（自动化，不依赖账号）

- **测试设备注册**：真机第一次加载广告时 Logcat 打印设备 ID，`RequestConfiguration.SetTestDeviceIds` 注册后再跑（08 §4.6，防 AdMob 判违规）。
- **自动化测试**：EditMode/PlayMode 走 `Tests/` 已有的 `FakeServices`（广告/内购 Mock），继续用桩验证业务逻辑（提示→看广告→回奖、购买→去广告）不变。
- 编译：Android IL2CPP 无头编译通过；CI-1（Unity 无头编译 + EditMode 回归）绿。

---

## 3. 7-2 UMP 同意流程 + 隐私政策页

| 任务 | 内容 | 状态 |
|---|---|---|
| 3-1 | UMP 集成进 `AdMobAdsService.Initialize()`（先请求同意再初始化广告） | 7-1 内完成 |
| 3-2 | 隐私政策页面 | ✅ **已生成 `docs/privacy-policy.html`（中英双语）**，本次提交；启用前需替换：应用名/包名、联系邮箱、生效日期（TODO 标记在 HTML 内） |
| 3-3 | 设置页新增「隐私政策」按钮 | 需改 `SettingsView.cs`（绑 `PrivacyButton` → `Application.OpenURL`）与 `SettingsPopup` prefab（加按钮节点 + 本地化文案，FR-17）；URL 先填占位，A6 拿到真实 URL 后替换 |
| 3-4 | 首次启动同意弹窗 | 欧盟地区用户首次启动弹 UMP 同意表单；默认未同意时不初始化广告 |

---

## 4. 7-3 合规检查 + keystore（重点：keystore 教程）

### 4.1 数据安全表单（Play Console，账号下来后提交）

如实勾选要点（涉及广告 SDK）：
- 收集：广告标识符（Advertising ID）、崩溃数据、应用互动（匿名事件）；
- 用途：**广告或营销**、分析、**账号管理（不适用）**；
- 是否共享：SDK 共享（AdMob / Play Billing）；
- 加密传输：是；可否删除：卸载即删（无服务器数据）。
> 对照 [05_合规_发布_测试.md](./05_合规_发布_测试.md) 的完整清单逐项过。

### 4.2 内容分级（IARC 问卷）

- 面向所有年龄的休闲游戏，无用户生成内容 / 社交 / 抽卡，无暴力血腥；
- 有广告但不含"参考赌博"类激励（激励视频看广告获提示属常规变现，如实回答即可）。

### 4.3 keystore 签名（发布硬前置，文档 11 §1.4 明确：无自定义 keystore = debug 签名 = 不能上 Play）

**概念（简单版）**：AAB 提交给 Google Play 必须签名。Google 提供 **Play App Signing**：
你只保留一把 **upload key（上传密钥）**，用它签名 AAB 上传；Google 负责托管真正的签名密钥。
好处：丢了 upload key 还能找 Google 重置，不怕锁死账号。所以**只需生成一把 upload keystore**。

**步骤 1：生成 keystore（Windows PowerShell，Unity 自带 OpenJDK 的 keytool）**

```powershell
# 找 Unity 自带的 keytool（Unity HUB 编辑器安装路径下）：
Get-ChildItem "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Data\Java\bin\keytool.exe"

# 生成（替换公司/姓名/目录等占位值）：
& "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Data\Java\bin\keytool.exe" `
  -genkeypair -v `
  -keystore "D:\Projects\AI\SudokuGameBox\Build\keystore\upload.keystore" `
  -alias "sudoku" `
  -keyalg RSA -keysize 2048 -validity 10000 `
  -dname "CN=SudokuGameBox, OU=Indie, O=SudokuGameBox, L=City, S=State, C=CN"
```

- `-validity 10000` ≈ 27 年，够用；期间会提示设置**密钥库密码**和**密钥密码**，**务必记牢**（建议写进
  `Build/keystore/README.md` 并纳入自己的密码管理工具；**不要上传到 git**）。
- 生成后目录下应有 `upload.keystore`（一个文件）。

**步骤 2：Unity 里配置（Player Settings → Publishing Settings）**

1. 勾选 `Custom Main Keystore`（Player Settings → Publishing Settings → Keystore Manager）。
2. Keystore 选择 `upload.keystore`，Alias 填 `sudoku`，输入密码。
3. 同一页确认 `Project Key` 默认（Play App Signing 模式下 Project Key 保持默认即可，Google 管理）。

**步骤 3：上传 Upload Key（等账号 A1 后）**

Play Console → 你的应用 → **App integrity / Setup → App signing** → 上传 `upload.keystore` 的
**证书公钥**（.cer，可由 keytool 导出，见下）→ 完成 Play App Signing 注册。

```powershell
# 导出公钥证书 .cer（上传到 Play Console 用）：
& "...\keytool.exe" -exportcert -v `
  -keystore "D:\Projects\AI\SudokuGameBox\Build\keystore\upload.keystore" `
  -alias "sudoku" -file "D:\Projects\AI\SudokuGameBox\Build\keystore\upload.cer" `
  -rfc
```

**步骤 4（重要）：密码与文件管理纪律**

- keystore 与密码**绝不允许进 git**（在 `.gitignore` 中忽略 `Build/keystore/`）。
- CI-3（Jenkins AAB 构建）需要签名时，keystore 放 Jenkins 本机受控目录，密码通过
  Jenkins 凭据（Credentials）注入 gradle/Unity CLI 参数，**不写入任何脚本文件**。
- 双备份：keystore 文件 + 密码（密码管理器 / 离线保险柜），丢失 upload key 虽可找 Google 重置，
  但过程麻烦且窗口期内无法更新应用。

> 决策点：keystore 现在就生成（推荐，10 分钟）还是拖到 Phase 8？**建议现在生成**，
> 因为 7-3 验收要含"发行签名可用"，且 CI-3 构建脚本要预留签名参数。

### 4.4 验收清单

- [ ] Android 平台 define = `SUDOKU_ADMOB;SUDOKU_IAP` 且编译无报错
- [ ] 桩实现仍可编译（`##` 不定义符号时工程正常）
- [ ] EditMode 回归 + PlayMode 冒烟（激励→回奖、去广告→零广告路径）全绿
- [ ] 隐私政策页已生成并可托管访问（A6 后填真实 URL）
- [ ] keystore 已生成、Unity 已配置、Build/keystore 已进 .gitignore
- [ ] Data Safety / IARC 问卷核对完成（账号后提交）

---

## 5. 待用户拍板 / 授权

| # | 事项 | 类型 | 说明 |
|---|---|---|---|
| G1 | 安装 `com.unity.purchasing` 4.12.x（改 Packages/manifest.json） | 授权 | 红线 7 要求 |
| G2 | D-4 玩法数量（v1.0 = 数独 + 井字棋）、D-9 AdMob 单栈 | 决策 | 文档 11 已给推荐值，沿用即可 |
| G3 | keystore 现在生成（推荐）vs Phase 8 | 决策 | 见 4.3 步骤 1 |
| G4 | 账号申请进度（A1~A7 均为延后项） | 用户操作 | 账号下来后逐项点亮 |

---

## 6. 提交与分支纪律（AGENTS.md）

- 代码提交中文 message，格式 `type(模块): 描述`，例如：
  - `feat(商业化): AdMob 激励视频与插屏接入(Phase 7 7-1)`
  - `feat(商业化): Unity IAP 去广告内购接入(Phase 7 7-1)`
  - `docs(商业化): 新增隐私政策页与 Phase7 执行清单(15号文档)`
- 不 push（红线 9），由用户授权后自行 `git push`。