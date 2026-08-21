# 11. Unity 游戏盒子架构方案

> 版本:**v2.2** | 状态:Draft(可执行) | 基线:现有 Unity 数独工程(已按 §1.4 逐项核对真实工程文件)
> 定位:本文是**盒子化架构的唯一权威方案**。MVP 单机闭环见 [07_Unity2022落地版最小开发路线图.md](./07_Unity2022落地版最小开发路线图.md);长期目标架构见 [03_技术架构.md](./03_技术架构.md)(其"盒子相关"结论以本文为准)。
> **v2.2 变更(2026-08-20)**:**D-12 引擎定版 = Unity `6000.3.20f1` 国际版**,§4.9 改写为"定版结论 + HybridCLR Installer 实证 Gate + 12 步副本迁移 checklist";新增 **D-13 minSdk 统一为 24**;§1.4 补包镜像(`packages.unity.cn`)与构建环境体检结论;P-1 工期 2~2.5 周、总工期 13~16 周。
> **v2.1 变更**:D-10 落实(本文脱敏后入库);新增 D-11 + §3.5 `UIKit` 薄层(不引入 GameFramework 类全家桶);新增 D-12 + §4.9;§1.4 补 Unity IAP 未安装、AdMob 8.7.0。

## v2.0 变更摘要(相对 v1.4)

| 类别 | v2.0 做了什么 |
|---|---|
| 结构修复 | 修掉 §4.2 后多余的代码围栏(v1.4 中 §4.3~§5.1 约 120 行被渲染成代码块) |
| **基线校准** | 新增 §1.4「工程现状核对表」:Addressables / Remote Config / VContainer / UniTask / R3 **均未安装**;ARM64、AAB、targetSdk、stripping **均未配**;构建脚本产出 APK;存档实为 PlayerPrefs。P0 工作量与前置门槛按此重算 |
| **决策补齐** | 新增 §0 决策台账,把 v1.4 悬空的 4 个架构分叉拍死:热更 dll 唯一下发通道(D-1)、v1.0 构建模式全 AOT(D-2)、Trait 分层归属(D-3)、统一货币(D-5) |
| 矛盾清理 | RC 生效时延("秒级"→按节流)、热更 dll 引用规则、ModuleManifest 三版本号、路线图工期算术、SudokuCore 测试归属、SandBox/SandCrush 命名、数独广告位(数独无失败态) |
| 合规纠正 | **Play Asset Delivery 不是热更通道**(asset pack 随 AAB 版本走,更新仍需发版),v1.4 把它当成下发方案是错的,§9.2 已改写 |
| 新增章节 | §7.2 跨玩法统一经济、§11 体积/内存/启动预算重算、§12.3 数据门控(止损线)、§3.4 玩法脚手架、§8.4 埋点与配置命名契约、§16 文档治理 |
| 脱敏 | 删除竞品 Trait 类名清单与数量、"调研发现的关卡格式"等表述,改为按能力族描述;原始调研材料仅存 `_private/`(gitignored) |

---

## 0. 决策台账(先看这张表)

已决项是本文后续章节的前提;待拍板项标注了推荐值与影响面。

| # | 决策 | 结论 | 理由 / 影响 |
|---|---|---|---|
| **D-1** | 热更 dll / metadata 的下发与缓存通道 | **✅ 已决:Addressables Remote Catalog 单通道**(dll 与 metadata 作为二进制资产放各自远程组,缓存交给 Addressables) | v1.4 同时写了 Addressables、自管 `persistentDataPath`、PAD 三套,互不兼容。单通道 = 一套版本号、一套缓存、一套回滚。**禁止**再自建 dll 缓存目录 |
| **D-2** | v1.0 上架版的代码构建模式 | **✅ 已决:程序集照 §4.4 拆分,但 v1.0 关闭解释执行,热更程序集随主包一起 AOT 编译** | v1.4 让 v1.0 也走"包内 dll + 解释执行",白付性能与加载复杂度成本却拿不到任何热更收益。v2.0 让 v1.0 = 纯 AOT 自包含包(零合规风险、零性能损失),v1.1 才切解释执行 + 远程下发。**同一套 asmdef 两种出包模式**,代码零改动 |
| **D-3** | Trait 系统的 AOT/热更归属 | **✅ 已决:`ITraitService` / `TraitContext` / `TraitRegistry` / RC 拉取全在 AOT;热更侧只放 `Trait` 子类** | v1.4 把 `TraitService` 放热更 dll,而 AOT 侧的大厅、AdsService、低端机降级都要读开关 —— AOT **不能**引用热更程序集,原设计跑不通。同时改掉 `static class`,统一走 DI 以便 mock |
| **D-4** | v1.0 玩法数量 | **⚠️ 推荐:v1.0 = 大厅 + 数独 + 井字棋(2 玩法)**,第 3 个玩法进数据门控(§12.3) | 井字棋足够验证模块框架(加载/卸载/交叉导量/存档分区),第 3 个玩法主要是内容成本。把它挪到上架后按数据决定,砍掉 3 周关键路径 |
| **D-5** | 跨玩法经济 | **✅ 已决:全盒子只有一种货币 `box.coins`**,玩法内不发行独立货币 | 盒子留存的核心是"在 A 玩法赚的能在 B 玩法花";存档 schema 一旦分裂再合并代价极高,必须 P0 定死 |
| **D-6** | 配置通道 | **✅ 已决:四分管(§5.2)**,Remote Config 只放少量紧急开关,业务配置走 CDN JSON | RC 生产环境有 fetch 节流,不适合承载"频繁改的结构化配置" |
| **D-7** | 存档形态 | **✅ 已决:按模块分区的单文件(AES-GCM + HMAC),PlayerPrefs 仅存偏好;提供 v0→v1 迁移器** | 现状是 PlayerPrefs + `JsonUtility`(§1.4),必须写迁移,不能直接换格式丢用户数据 |
| **D-8** | HybridCLR 版本 | ⚠️ 待拍板:社区版起步(推荐)还是商业版 | 先按社区版做,授权条款与性能口径按 §4.8 在 P0 实测/复核后再决定 |
| **D-9** | 广告聚合 | ⚠️ 待拍板:AdMob 单栈起步(推荐) | 架构留 `IAdsService` 换实现位,eCPM 不佳再上聚合 |
| **D-10** | 本文是否入库 | **✅ 已决:v2.0 脱敏后入库**(`.gitignore` 已移除 `docs/11_…`、`docs/07_…`) | 架构演进必须有 diff 与评审留痕;原始调研材料与历史备份继续只放 `_private/` |
| **D-11** | 是否引入第三方 UI/游戏框架(GameFramework 一类) | **✅ 已决:不引入全家桶框架,自研薄层 `UIKit`(全 AOT),见 §3.5** | 本方案已经定了 Addressables(资源)+ VContainer(DI)+ ModuleFramework(模块)+ HybridCLR(代码),GameFramework 这类框架自带资源/流程/实体/UI/配置全套,重叠冲突大于收益;真正缺的只是"界面栈 + 弹窗互斥 + 生命周期",600~900 行可控代码即可,且基类留 AOT 才能与热更边界对齐 |
| **D-12** | 引擎版本 | **✅ 已决:Unity `6000.3.20f1`(国际版)**,2026-08-20 定版;旧基线 `2022.3.50f1c1`(中国版 c1)在验收通过前保留。迁移必须在**工程副本**上做,详见 §4.9 | 出海项目用国际版(插件/文档/政策一致,已核实工程无中国版专属依赖);2022 LTS 已过支持窗口,而 Play 的 targetSdk 每年上调。工程现在只有 4 个 asmdef / 2 个场景,**这是迁移成本最低的时刻**。HybridCLR 支持范围为 `6000.x.y`,但**以 Installer 实证为准**(§4.9 Gate 1) |
| **D-13** | minSdk | **✅ 已决:定版为 25**(Android 7.1)。2026-08-21 Phase 1 实测触发"以引擎为准"条款:Unity 6000.3 最低支持 API = 25,设 24 已 obsolete(CS0618,构建强制回写 25,下个 release 变 error);已回写 03/10/本文 | 覆盖率损失可忽略,且多数广告/分析 SDK 的下限在上移;设置脚本 `ProjectSetup.cs` 直接写 25 |

---

## 1. 目标与边界

### 1.1 对齐什么(商业/运营架构)

盒子形态的本质是 **一个包、N 个玩法、一个运营中枢**:

| 对齐项 | 行业做法 | 借鉴价值 |
|---|---|---|
| 盒子形态 | 大厅 + 多款轻量玩法,交叉导量 | ★★★ 核心 |
| 新玩法下发 | 热更新动态资源包,不换包 | ★★★ 核心 |
| 运营开关(Trait) | 大量服务端可控开关,灰度/A-B/按国家 | ★★★ 核心 |
| IAA 变现 | 多聚合 + 激励/插屏 + 广告保护 | ★★★ 核心 |
| 留存增长 | 每日签到、回归礼包、流失预防 | ★★ |
| 安全防护 | 多层加密 + 原生加固 | ★(不对齐,见 §9) |

### 1.2 不对齐什么

- **引擎**:**Unity `6000.3.20f1`(国际版,D-12)** + UGUI + TMP(对齐 [03_技术架构.md](./03_技术架构.md);Unity 6 中 TMP 已并入 `com.unity.ugui` 2.x)
- **代码热更**:C# 不能直接热更脚本 → 用 **HybridCLR**(IL2CPP + 解释执行热更程序集),v1.1 起启用,见 §4.4
- **安全体系**:不做重型防破解(IAA 小游戏投产比极低),做"够用"即可,见 §9
- **多聚合双栈**:起步 AdMob 单栈,架构留换聚合的口子

### 1.3 范围决策

- **玩法数量**:v1.0 = 大厅 + 数独 + 井字棋(D-4);第 3 个玩法按 §12.3 数据门控
- **发布策略**:**v1.0 上架版 = 自包含纯 AOT 盒子**。远程热更/远程配置管线在 P0 就建好接口与构建脚本,但**不参与 v1.0 出包**(D-2);v1.1 起启用远程资源、远程 dll
- **新玩法上线方式**:玩法逻辑从 P0 就写在"热更程序集"的 asmdef 里(按玩法拆,见 §4.4),v1.0 随包 AOT 编译,v1.1 起改为远程下发 + 解释执行
- **后端**:无自建后端(Firebase Remote Config + 静态 JSON/CDN),不养服务器;后续按收益上轻量 Serverless,第一个大概率是 SSV 接收端点

### 1.4 工程现状核对表(v2.0 新增,已逐项验证)

> 这张表是 §12 工期估算的依据。v1.4 的多处"沿用现有架构"表述与真实工程不符,导致 P0 被严重低估。

| 项 | v1.4 的假设 | **真实工程** | 证据 |
|---|---|---|---|
| Addressables | §3/§4 全章依赖 | **未安装** | `Sudoku/Packages/manifest.json` 无 `com.unity.addressables` |
| Firebase Remote Config | §4.2/§5 依赖 | **未安装**(只有 App / Analytics / Crashlytics) | `Sudoku/Assets/Firebase` 下仅 3 套 dll 与对应 m2repository 包 |
| VContainer / UniTask / R3 | §2/§3.2 直接使用 | **三者均未安装** | 全工程唯一提及是 `SudokuGameController.cs` 的 TODO 注释 |
| 程序集分层 | "Core / Gameplay / Services 分层" | 仅 4 个 asmdef,**Services 在 `Sudoku.Gameplay` 内,无独立程序集** | `Sudoku.Core` / `Sudoku.Gameplay` / `Sudoku.Gameplay.Editor` / `Sudoku.Core.Tests` |
| Scripting Backend | 前置门槛已列 | **Mono**(`scriptingBackend: {}`) | `ProjectSettings.asset` |
| **ARM64** | 未提 | **仅 ARMv7**(`AndroidTargetArchitectures: 1`)→ **Play 上架硬阻塞** | 同上 |
| **AAB** | §9.1 当作已满足 | 构建脚本产出 `Build/Sudoku.apk`,未设 `buildAppBundle` | `Assets/Features/Gameplay/Editor/BuildScript.cs` |
| targetSdk / stripping | 未提 | `AndroidTargetSdkVersion: 0`(Automatic)、`managedStrippingLevel` 空 | `ProjectSettings.asset` |
| minSdk | 03 文档写 24 | 实际 **23** → **D-13 定版 25**(P-1 已落:Unity 6000.3 引擎下限 25) | 同上 |
| 场景 | "MainMenu 演化为大厅" | `App/Scenes/Menu.unity`、`App/Scenes/Gameplay.unity`(+ 模板 `SampleScene`) | 工程扫描 |
| 资源加载 | 假定可直接切 Addressables | 现走 `Resources/`(`Resources/Art`、`Resources/Audio`) | 工程扫描 |
| 存档 | "现项目存档为单一数独进度(加密 JSON)" | 实际 **PlayerPrefs + `JsonUtility`**,无加密、无 `ISaveService` | `GameStatistics.cs`、`SettingsService.cs` |
| 测试 | P0 要 PlayMode 测试 | 只有 Editor-only 的 `Tests/EditMode`(`Sudoku.Core.Tests`),**无 PlayMode 测试程序集** | `Sudoku.Core.Tests.asmdef`(`includePlatforms: ["Editor"]`) |
| Unity 版本 | "Unity 2022 LTS" | 现为 **2022.3.50f1c1**(中国版 c1 分支)→ **目标 `6000.3.20f1` 国际版**(D-12,P-1 迁移) | `ProjectSettings/ProjectVersion.txt` |
| 包镜像 | 未提 | `packages-lock.json` 的 registry 指向 `https://packages.unity.cn`(换国际版时删除该文件重解析) | `Sudoku/Packages/packages-lock.json` |
| 构建环境 | 未提 | ✅ 已就绪:Android 模块齐全(SDK 到 `android-35`、build-tools 34.0.0、NDK 23.1.7779620、OpenJDK)、EDM4U **1.2.188** 已 Resolve、bundletool 1.16.0 可用;**无自定义 keystore**(现为 debug 签名,不能上 Play) | 本机扫描 / `ProjectSettings.asset` |
| **Unity IAP** | 03/07 列为 P0 已接 | **未安装**:`manifest.json` 无 `com.unity.purchasing`,`UnityIapService.cs` 整体包在 `#if SUDOKU_IAP` 内,而 Android define 只有 `SUDOKU_ADMOB;SUDOKU_FIREBASE` → **去广告内购实际未接通** | `ProjectSettings.asset` / `UnityIapService.cs` |
| AdMob 插件 | — | GoogleMobileAds **8.7.0**(升引擎/targetSdk 时需同步核版本) | `Assets/GoogleMobileAds/GoogleMobileAds_version-8.7.0_manifest.txt` |

**由此产生的硬前置(§12 的 P-1)**:引擎版本决策与迁移(D-12);ARM64 + IL2CPP + AAB + targetSdk + stripping 一次配齐并出真机包;Addressables / UniTask / VContainer(+ Unity IAP)安装并验证;拆 Services 程序集;建 PlayMode 测试程序集;核对 HybridCLR / Addressables / AdMob 8.7.0 / Firebase 13.15 对目标 Unity 版本的支持。

---

## 2. 总架构

```text
┌──────────────────────────────────────────────────────────────┐
│                     App Shell(常驻, 全 AOT)                  │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │  Home 大厅                                               │ │
│  │  游戏入口网格 / 签到 / 每日挑战 / 设置 / 交叉推荐位       │ │
│  └──────────────────────────────────────────────────────────┘ │
│        ▲  ModuleFramework(模块生命周期管理, 全 AOT)          │
│        │  进入 → LoadModule(Addressables) → PushUIStack      │
│        │  退出 → UnloadModule → 回收内存 → 回大厅            │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐       ┌─────────┐      │
│  │ Sudoku  │ │TicTacToe│ │ SandCrush│  ...  │ 新玩法  │      │
│  │ Module  │ │ Module  │ │ Module   │       │(热更)   │      │
│  └─────────┘ └─────────┘ └──────────┘       └─────────┘      │
│    每个玩法 = 独立程序集 + 独立 Addressables Group           │
└──────────────────────────────────────────────────────────────┘
        │ 依赖(接口)
┌───────▼──────────────────────────────────────────────────────┐
│  Shared Services(跨玩法共享, 独立 asmdef, 全 AOT)            │
│  IAdsService │ IIapService │ ISaveService │ IAnalyticsService │
│  ITraitService │ IAudioService │ ILocalization │ IEconomy     │
└──────────────────────────────────────────────────────────────┘
        │ 依赖
┌───────▼──────────────────────────────────────────────────────┐
│  Infrastructure(SDK 适配层, 全 AOT)                          │
│  AdMob(聚合-ready) │ Firebase(AC+RC) │ GPGS │ Addressables    │
└──────────────────────────────────────────────────────────────┘
```

分层原则沿用 [03_技术架构.md](./03_技术架构.md):VContainer DI + UniTask(R3 可选,见 §12 备注);玩法模块与共享服务只通过接口交互,**模块之间不允许直接引用**。

---

## 3. 玩法模块化

### 3.1 ModuleManifest —— 模块清单(唯一的"游戏注册表")

三套版本号独立升级(§4.5),清单是代码/内容/配置解耦的落点:

```jsonc
// 远程 CDN JSON 下发,或包内 ModuleCatalog(ScriptableObject)兜底
{
  "manifestVersion": 1,
  "modules": [
    {
      "id": "sudoku",
      "displayNameKey": "module.sudoku.name",   // 走本地化 key,不写死文案
      "entryType": "Sudoku.HotUpdate.SudokuModule",  // 热更程序集内的入口类型全名
      "entryAsset": "Modules/Sudoku/SudokuEntry",    // Addressables key
      "icon": "Modules/Sudoku/icon",
      "codeVersion":    "1.3.0",   // 热更 dll 版本
      "contentVersion": "1.3.2",   // Addressables 内容版本
      "configVersion":  "7",       // 该玩法的配置版本
      "codeHash": "sha256:...",    // dll 完整性校验
      "sortOrder": 0,
      "badge": { "type": "new" },
      "enabled": true,
      "minAppVersion": "1.0.0",    // 低于此版本的客户端隐藏该模块
      "minSdkInt": 23              // Android API level 下限(v1.4 的 minOs 语义未定义,已明确)
    },
    {
      "id": "tictactoe",
      "displayNameKey": "module.tictactoe.name",
      "entryType": "TicTacToe.HotUpdate.TicTacToeModule",
      "entryAsset": "Modules/TicTacToe/TicTacToeEntry",
      "icon": "Modules/TicTacToe/icon",
      "codeVersion": "1.0.0", "contentVersion": "1.0.0", "configVersion": "1",
      "sortOrder": 1, "enabled": true, "minAppVersion": "1.0.0"
    }
  ]
}
```

- **静态部分**:`ModuleCatalog`(ScriptableObject,编辑器维护,随包走,离线兜底)
- **动态部分**:远程 JSON 下发 `module_overrides`,覆盖 `enabled / badge / sortOrder / *Version` —— 这就是"上新玩法、下架旧玩法、给谁推什么"的入口,**改配置不改包**
- 客户端对清单做**向前兼容**:未知字段忽略、未知模块忽略、`minAppVersion` 不满足即隐藏(老版本客户端不会因为新字段崩)

### 3.2 ModuleFramework 核心接口(可注入,非 static)

```csharp
public interface IGameModule
{
    string Id { get; }
    UniTask OnEnter(ModuleContext ctx);   // ctx: 共享服务句柄(接口)
    UniTask OnExit();
}

// v1.4 用 static class,与 VContainer DI、PlayMode 测试注入冲突 → v2.0 改接口
public interface IModuleLoader
{
    UniTask<bool> EnterAsync(string moduleId, CancellationToken ct = default);
    UniTask ExitAsync(string moduleId, CancellationToken ct = default);
    ModuleLoadState GetState(string moduleId);
}

public sealed class ModuleLoader : IModuleLoader   // AOT 侧实现,由 VContainer 注册
{
    // 进入: 校验清单 → 确保代码可用(AOT/解释执行两种模式) → 加载入口资源 → 实例化入口类型 → 入 UI 栈
    // 退出: OnExit → 释放该模块 Addressables 句柄 → 引用计数归零 → 回大厅
}
```

- **框架全在 AOT**(`IGameModule` / `IModuleLoader` / `ModuleContext` / `Box.UIKit`);玩法入口类型名写进清单,`ModuleLoader` 按名反射实例化 —— **框架不依赖任何玩法类型**
- 大厅(App Shell)常驻内存;玩法模块进入/退出时 Addressables 全量加载/卸载
- 内存管理:退出时释放句柄 + `Resources.UnloadUnusedAssets()`,保证低端机反复切换不 OOM
- 交叉导量 = 玩法内调 `IModuleLoader.EnterAsync("otherGame")`,模块间零耦合
- **失败即降级**:`EnterAsync` 返回 `false` 时不抛到 UI 层,统一走"入口灰化 + 提示 + 上报"(§4.7)

### 3.3 新玩法开发规范(落地纪律)

1. 独立 asmdef,**只引用 Shared Services 接口程序集 + `HotUpdate.Core`**,不引用其他玩法
2. 入口是单个 prefab + 一个 `IGameModule` 实现;资源全部落在自己的 Addressables Group,**不得引用其他 Group 的资源**
3. 玩法内部自带"回到大厅"入口,且退出路径唯一
4. 埋点事件带模块前缀 `{module_id}.{action}`(§8.4)
5. 存档只读写自己的分区,货币只走 `IEconomyService`(D-5)
6. **不得直接读 Firebase / AdMob 等 SDK 类型**,只用服务接口(否则热更侧会踩 AOT 泛型缺失)

**"轻量"硬约束(防 scope creep)**:新玩法 ≤ 3 个场景 / ≤ 5 个 prefab / ≤ 1 周开发 / 首次进入下载 ≤ 8MB,超出即砍设计,不许拖期。

### 3.4 玩法脚手架(v2.0 新增,决定"一周一玩法"能否成立)

Editor 菜单 `Box/New Module...` 一键生成:

- `HotUpdate/<Name>/` 目录 + asmdef(引用白名单已预设)
- `Modules/<Name>/` 资源目录 + 已标记的 Addressables Group + Profile 变量
- 入口 prefab + `<Name>Module : IGameModule` 模板(含 OnEnter/OnExit 与埋点前缀常量)
- 清单条目草稿写入 `ModuleCatalog`
- PlayMode 测试模板(加载/卸载/内存断言各一条)

没有脚手架,"一周一个玩法"里至少 2 天会耗在样板与 Addressables 配置上。

### 3.5 UI 层方案:自研薄层 `UIKit`(D-11)

#### 为什么不引入 GameFramework 这类全家桶

| 维度 | 说明 |
|---|---|
| 重叠冲突 | GameFramework(UnityGameFramework)自带 **Resource(自研 AssetBundle 管线)/ Procedure(FSM)/ Entity / UI / Event / Config / DataTable / Sound / Network** 全套。本方案已经把资源交给 **Addressables**(热更的地基,§4)、依赖交给 **VContainer**、模块生命周期交给 **ModuleFramework**、代码热更交给 **HybridCLR** —— 引入它等于**同时存在两套资源与两套模块世界观**,§3/§4 要重写 |
| 热更边界 | 框架基类必须落在 AOT 侧;第三方框架内部的反射/泛型用法在解释执行模式下需要额外补 AOT 泛型 metadata,出问题排查成本高、且不受你控制 |
| 迁移与维护 | 它的资源模块与 Addressables Content Update 不是一个模型,想混用要写适配层;引擎升级(D-12)时适配节奏也不由你决定 |
| 收益面 | 本项目真正缺的只有"界面栈 + 弹窗互斥 + 生命周期 + 异步加载",不缺 Entity/Network/DataTable |
| 作品价值 | 项目定位含求职作品(07 文档),**"能讲清自己设计的模块/UI 框架"比"会用某框架"更有说服力** |

**什么情况下应该反过来选框架**:团队已有该框架成熟经验、项目是中重度(大量实体/网络同步/大表配置)、或有多人协作需要强约定。本项目三条都不成立。

> 同理不建议上 MVVM 数据绑定框架(Loxodon 一类):表达式树/反射绑定在 IL2CPP + 解释执行下最容易踩泛型与裁剪坑,收益(省一点样板)远小于风险。可评估的轻量参考实现:UnityScreenNavigator(MIT,页面/模态栈 + Addressables/UniTask 亲和)—— **只借鉴其转场与栈设计,不作为依赖**,若要直接依赖须先验证目标 Unity 版本与 HybridCLR 兼容。

#### `UIKit` 必须提供的能力(交付范围)

全部落在 **AOT 侧**(`Box.UIKit` asmdef),具体界面(View)写在热更侧:

1. **界面栈与路由**:`PushAsync<T>(args)` / `PopAsync()` / `ReplaceAsync<T>()`,与 Android 返回键统一接线(现工程已有返回键逻辑,迁入这里)
2. **层级(Layer)**:`Scene / HUD / Window / Popup / Toast / Loading / Debug` 各自独立 Canvas —— **同时满足 03 文档的"Canvas 静/动分层"性能要求**,避免全屏重建
3. **生命周期**:`OnCreate / OnShow(args) / OnHide / OnDestroy / OnRefresh`,全部 `UniTask` 化、可取消
4. **异步打开**:Addressables 加载 prefab + 加载中占位/超时兜底;失败返回 `false` 不抛到业务层
5. **缓存与卸载**:界面按"常驻/缓存 N 个/立即销毁"三档策略;模块退出时强制清空该模块界面并释放句柄(与 §3.2 的内存回落断言对齐)
6. **弹窗互斥与队列**:全局唯一"抢屏仲裁器" —— 插屏广告、签到弹窗、回归礼包、升级提示排队而非叠加。**盒子最常见的线上事故就是插屏与弹窗互相盖住导致卡死**,这条必须在 P0 就有
7. **UI 埋点自动化**:界面 `OnShow` 自动上报 `{module_id}.ui_show`,带 `active_traits`(§8.4),不靠业务手写
8. **适配**:CanvasScaler 策略 + 安全区(刘海/挖孔)+ 横竖屏约束统一在基类处理

#### 边界纪律

- `UIKit` 只依赖 `Box.Services.Abstractions` + Addressables + UniTask,**不认识任何玩法**
- 热更侧的 View 只继承 AOT 基类、只调接口;禁止在 View 里直接 `Addressables.LoadAssetAsync` 或碰 SDK 类型(§3.3 第 6 条)
- 预计规模 6~8 个文件、600~900 行,**P0-b 内完成**,不额外增加 Phase

---

## 4. 热更新管线

### 4.1 机制对照

| 目标能力 | Unity 实现 |
|---|---|
| 多服务器地址容灾 | Addressables Remote Catalog(Profile 变量可配多 CDN,启动时按可达性选择) |
| 模块化按需下载 | Addressables Group 按需下载 + `GetDownloadSizeAsync` 预算提示 |
| 热更内容优先于包内 | Content Update:`BuildContentUpdateBundles` 产出的远程组覆盖旧内容 |
| 增量工作空间 | Addressables 自带缓存目录 + catalog hash 比对(**不自建缓存,D-1**) |
| 版本比对事件驱动 | `CheckForCatalogUpdates()` + 远程清单版本号强刷 |

> **Content Update 工程注意**:远程组"覆盖"依赖 Content Update 机制;**本地组引用远程资源会让更新失效**,玩法资源必须完整落在自己的远程组内。catalog hash → bundle 依赖 → 缓存整条链必须真机 + 断网压力测试(列入 §12 P3 验收)。

### 4.2 启动时序(Boot 流程)

```text
冷启动
  │
  ├─ 1. 极简启动场景(Logo)← 首包只保证这个场景秒开(预算见 §11)
  ├─ 2. 初始化:Firebase → RC/CDN 配置(带超时,失败不阻断)→ Trait 本地缓存
  ├─ 3. 代码就绪(两种模式,由构建期决定):
  │      3a. v1.0 纯 AOT 模式:无操作,玩法程序集已在包内 → 直接就绪
  │      3b. v1.1 解释执行模式:LoadMetadataForAOTAssemblies → Assembly.Load(dll bytes)
  │      3c. 校验 codeHash;失败 → 回滚上一版 → 仍失败则该模块入口灰化 + 上报
  ├─ 4. 加载 ModuleManifest(远程 → 本地缓存 → 包内 Catalog 三级兜底)→ 渲染大厅入口网格
  └─ 5. 进入大厅(大厅资源与代码必须在首包内,保证无网可进)
```

**关键纪律**:第 2、3、4 步的任何网络失败都只降级、不阻断;唯一允许阻断启动的是"包内资源损坏"。启动路径上**没有任何强制等待网络的环节**。

### 4.3 热更的内容

1. **新玩法模块**:整个 Group 不在首包 → 上新玩法 = 远程 bundle + 清单下发
2. **玩法逻辑(代码)**:热更程序集 dll —— 修 bug、加玩法、改 Trait 逻辑(v1.1 起)
3. **关卡/皮肤/美术**:新关卡包(§8.3)、皮肤 bundle、图集增量
4. **Trait / 业务配置**:走 RC / CDN JSON,不走 Addressables

### 4.4 代码热更(HybridCLR):两阶段启用

**程序集拆分从 P0 第一天就做**,但**是否启用解释执行由构建期开关决定**(D-2):

| 出包模式 | 用于 | 热更程序集怎么处理 | 收益 / 代价 |
|---|---|---|---|
| **纯 AOT(v1.0)** | Google Play 首版 | 与主包一起 IL2CPP 编译进包 | 零解释执行开销、零包内 dll 加载逻辑、零下载、**零合规风险** |
| **解释执行 + 远程(v1.1+)** | 上架后迭代 | dll/metadata 作为远程资产,运行时 `Assembly.Load` | 拿到不发版上新玩法/修 bug 的能力,代价见 §4.8 |

同一套 asmdef、同一套代码,只切构建配置 —— 这是 v2.0 相对 v1.4 最重要的改动:**v1.0 不为热更付任何运行时成本**。

#### 程序集拆分

| AOT 程序集(始终进包) | 热更程序集(v1.0 进包 / v1.1 起远程) |
|---|---|
| 引擎 / Unity API / 第三方 SDK | `HotUpdate.Core.dll`(玩法公共基座) |
| App Shell(Boot / 大厅) | `HotUpdate.Sudoku.dll` |
| `ModuleFramework` + `Box.UIKit`(IGameModule / IModuleLoader / ModuleContext / UIRouter / PopupArbiter) | `HotUpdate.TicTacToe.dll` |
| Services **接口 + 实现** + SDK 适配 | `HotUpdate.SandCrush.dll`(数据门控后再做) |
| `TraitService` / `TraitRegistry` / `TraitContext`(D-3) | `HotUpdate.Traits.dll`(只有 `Trait` 子类) |
| `SudokuCore`(纯算法,性能敏感,永久留 AOT) | |

- **一玩法一 dll**:改数独只下发数独 dll
- **引用规则(v1.4 措辞矛盾,已修正)**:玩法 dll **之间**禁止互相引用;**允许且只允许**引用 `HotUpdate.Core` 与 AOT 侧的接口程序集;禁止引用 AOT 具体实现类
- 构建流程:`HybridCLR → Generate/All`(编译热更 dll + `AotGenericReferences` + 补充 metadata)→ 产物按 D-1 进 Addressables 远程组
- **大厅与所有 Services 留 AOT** —— 无网也能进大厅、玩已缓存玩法

### 4.5 版本解耦与回滚

| 维度 | 版本号 | 变更原因 | 更新通道 |
|---|---|---|---|
| 代码 | `codeVersion` + `codeHash` | 修 bug、新玩法 | 热更 dll(v1.1+) |
| 内容 | `contentVersion` | UI / 美术 / 关卡 | Addressables |
| 配置 | `configVersion` | 运营参数 / 开关 | RC / CDN |

- 清单(§3.1)带三套版本号,各自独立升级 —— 改一个广告参数**绝不触发 dll 重下**
- 客户端保留**上一版** dll 与 catalog:哈希校验失败、`Assembly.Load` 抛异常、或**首帧后 5 秒内崩溃**(启动哨兵标记)→ 回滚上一版并上报 Crashlytics
- 连续两版都失败 → 进入"安全模式":只加载 AOT 大厅 + 包内玩法,远程链路当次禁用

### 4.6 离线降级策略(v1.1 起生效)

**先澄清**:v1.0 纯 AOT 自包含,无网零依赖,不存在"无网不能玩"。真正的空白在 v1.1 启用远程 dll 之后:

| 场景 | 行为 |
|---|---|
| 首装 + 在线 | 下载 dll/bundle → Addressables 缓存 → 之后启动优先读缓存,不走网络 |
| 缓存命中 + 无网 | 直接从缓存加载 → 该玩法可玩,远程链路整体跳过 |
| 缓存未命中 + 无网 | **该玩法入口灰化并标注"需联网下载"**;大厅可进,已缓存玩法可玩 |
| 校验失败 | 清该模块缓存 → 回滚上一版(§4.5)→ 仍失败则灰化 + 上报 |

**产品预期写死**:新玩法"首次进入必须在线"(热更上新的固有属性);**一旦玩过就永久离线可玩**;大厅与数独(首包内)永远不依赖网络。

### 4.7 热更发布管线(CI)

- **`addressables_content_state.bin` 必须入库**:Content Update 依赖上一次构建的 state 文件,丢失 = 无法增量更新(注意:本文件位于 `Sudoku/` 子工程,确认未被 `.gitignore` 的 `[Bb]uild/` 等规则误伤)
- 构建脚本固化:全量构建(`BuildPlayer` + 全量 bundle,产出 **AAB**)→ 后续只做 `BuildContentUpdateBundles`(增量)→ 上传 catalog + 差量 bundle + dll + 清单
- **真机验收 checklist(P3 硬性)**:① 首次安装→热更→新玩法可玩;② 断网冷启动→大厅+已缓存玩法可玩;③ 坏 dll 下发→回滚成功且入口灰化;④ 低端机(入门 Android 档)切换玩法内存曲线正常;⑤ 移动网/WiFi 切换中热更不崩;⑥ 弱网(限速 50KB/s)下载可中断可续
- Android 注意:StreamingAssets 路径与 Editor 不同;HybridCLR metadata 一律走 Addressables 而非裸文件读(D-1 的附带收益)

### 4.8 代价与纪律(需在 P0 用 Profiler 实测)

- **性能**:解释执行相对原生 AOT 的差距**按场景差异极大**,纯计算热循环远差于 UI/调度逻辑。v1.4 引用的"30-50%"是笼统口径,**不作为决策依据**;以官方 [Execution Performance](https://www.hybridclr.cn/en/docs/8.5.0/basic/performance) 为参考,并在 P0 用本项目真实场景(棋盘刷新 / 动画 / 输入)实测。纪律:**`SudokuCore` 等高频算法永久留 AOT**,热更侧只放调度与 UI 逻辑
- **泛型陷阱**:AOT 侧泛型实例化需补充 metadata(`AotGenericReferences`);热更代码避免发明 AOT 侧不存在的泛型组合;**热更侧不直接碰 SDK 的 `Task<T>` 等泛型**(§3.3 第 6 条)
- **裁剪/反射**:按 HybridCLR 文档配置 `link.xml` 与 Managed Stripping(现工程 `managedStrippingLevel` 为空,P-1 一并设定)
- **授权**:社区版免费(含商用),商业版为性能与加密增强。**具体条款以当前 [LICENSE](https://github.com/focus-creative-games/HybridCLR) 与[商业版说明](https://www.hybridclr.cn/en/docs/8.5.0/business/intro)为准**,D-8 拍板前复核一次
- **版本支持**:HybridCLR 的支持版本以官方[支持的 Unity 版本与平台](https://www.hybridclr.cn/en/docs/8.5.0/basic/supportedplatformanduniyversion)为准;它是 §4.9 版本决策的**第一约束**

### 4.9 引擎版本决策与迁移(D-12,已定版)

#### 定版结论

| 项 | 值 |
|---|---|
| **目标版本** | **Unity `6000.3.20f1`(国际版)** |
| 定版日期 | 2026-08-20 |
| 旧基线 | `2022.3.50f1c1`(Unity 中国版 c1 分支)—— 迁移完成前保留可用 |
| 换国际版依据 | 已核实工程**无中国版专属依赖**:`manifest.json` 全为标准包、`Assets` 下无 China/Tuanjie 资产;唯一残留是 `packages-lock.json` 的 registry 指向 `https://packages.unity.cn`(迁移时删除该文件重解析) |
| HybridCLR | 官方支持范围写作 `6000.x.y`,涵盖本版本;**但以 Installer 实证为准**(见下方 Gate 1) |
| 待确认 | ① `6000.3` 是否带 LTS 标记(下载归档徽标);② Play 当前强制 targetSdk 该版本可达(应为可选 35/36) |

**为什么现在迁**:工程当前只有 4 个 asmdef / 2 个场景 / 无 Addressables 无 HybridCLR —— 这是迁移成本最低的时刻。等 P0 建完热更管线再迁,要连带重验 catalog/dll 整条链。

**升级不可逆**:一旦用 6.x 打开并保存,序列化文件、包版本、TMP 资源全部升级,回不到 2022.3。**必须在工程副本上做**(`Sudoku` → `Sudoku_u6`),主工程保持 `2022.3.50f1c1` 可用直到验收通过。

#### Gate 1:HybridCLR 实证(30 分钟,先做这个再动主工程)

支持"列表"不足以保证成功 —— HybridCLR 需要为每个 Unity 补丁提供 **il2cpp 源码适配**,真实失败模式是"**装的 hybridclr_unity 包比该 Unity 补丁的发布时间更早**,Installer 找不到对应 il2cpp 版本"。所以用实证代替查表:

1. Hub 装 `6000.3.20f1` 国际版(多版本共存,不影响现有工程)
2. **新建空工程**(不要用主工程)
3. 装**最新版** `hybridclr_unity`(走 git URL 取最新 tag / master,不要装旧版本号)
4. `HybridCLR → Installer → Install`(init local il2cpp)
5. 跑一次 `Generate/All`
6. 前置项:Scripting Backend = **IL2CPP**、Api Compatibility Level = **.NET Standard 2.1**

- ✅ 装完 + Generate 无错 → Gate 1 通过,按下方 checklist 迁主工程
- ❌ 失败 → 退到 HybridCLR 实证可用的最高 Unity 6 家族最新补丁(**不要**赌"以后会支持",到 P3 才发现要换引擎的代价高一个数量级)

#### 迁移 checklist(P-1 执行,顺序不可乱)

1. **先提交 + 打 tag**(如 `pre-u6-migration`),再 `Sudoku` → `Sudoku_u6` 复制副本;**主工程不动**
2. 副本里删除 `Library/`、`Temp/`、`obj/`,以及 **`Packages/packages-lock.json`**(强制按国际 registry 重新解析)
3. 用 `6000.3.20f1` 打开副本,让 API Updater 跑完
4. **TMP 迁移(本项目最大返工点)**:`com.unity.textmeshpro 3.0.7` 在 Unity 6 已并入 `com.unity.ugui 2.x`。走完 TMP 资源升级向导后,**必须重验字体** —— 本项目做过字体子集(`docs/字体子集字符集.txt`),典型故障是**丢字**或**材质变紫**;必要时用同一字符集重新生成 SDF 资产
5. **删掉 `Assets/Plugins/Android` 下的 2022 时代 Gradle 模板**(`baseProjectTemplate.gradle` / `mainTemplate.gradle` / `settingsTemplate.gradle` / `gradleTemplate.properties`)→ 新版本重新生成 → **EDM4U 重新 Resolve**(现装 1.2.188,较新)→ 再把 AdMob 需要的 manifest/metadata 改动重打一遍。Unity 6 走 AGP 8 / JDK 17,旧模板必然冲突
6. 核 SDK:Firebase Unity 13.15、**GoogleMobileAds 8.7.0**(最可能需要升)、Unity IAP 是否声明支持 Unity 6.x;不兼容就升插件,别改引擎
7. 保留 `scriptingDefineSymbols`(`SUDOKU_ADMOB;SUDOKU_FIREBASE`,接 IAP 后加 `SUDOKU_IAP`)
8. 重配 Player Settings:**IL2CPP + ARM64 + targetSdk 35 + managedStrippingLevel(Medium)+ minSdk 25**(§1.4 的债务一次性清掉;✅ 2026-08-21 已落地:Unity 6 最低支持 API = 25,minSdk 定版 25,03 文档已回写)
9. `BuildScript.cs` 拆成两个入口:`BuildAndroidApk`(本地测试)与 `BuildAndroidAab`(上架,`EditorUserBuildSettings.buildAppBundle = true`)
10. 精简包:确认不用则移除 `com.unity.visualscripting`、`com.unity.collab-proxy`;**`com.unity.ai.assistant` / `com.unity.ai.inference` 已拍板保留(2026-08-21),不精简**
11. **验收(全过才切主工程)**:EditMode 测试全绿 + IL2CPP+ARM64 真机包跑通"启动→单局→激励广告→返回" + **字体/UI 显示正常** + §11 五项基线重测
12. 验收通过后:副本转正(或把主工程按同样步骤升一次),回填本节"待确认"两项,同步 03 / 07 / README 的引擎表述

#### 未来再升级时复用的排除法

| 步 | 查什么 | 硬约束 |
|---|---|---|
| 1 | HybridCLR [支持版本页](https://www.hybridclr.cn/en/docs/8.5.0/basic/supportedplatformanduniyversion) + **Installer 实证** | 装不上 = §4.4 整章作废 |
| 2 | Play [目标 API 级别要求](https://developer.android.com/google/play/requirements/target-sdk) | 必须能出当前强制 targetSdk 的 AAB |
| 3 | Unity 下载归档 | 取 ①∩② 内、**当前 LTS 家族**的最新补丁 |
| 4 | 插件 release notes | Addressables / Firebase / AdMob / IAP 均声明支持 |
| 5 | Addressables 大版本 | 跨 1.x↔2.x 时重验 Content Update 整条链(§4.7) |

---

## 5. Trait 运营开关系统

### 5.1 三层设计(ScriptableObject 只占一层)

| 层 | 载体 | 位置 | 更新方式 |
|---|---|---|---|
| ① 逻辑层 | `Trait` 子类(C#) | **热更程序集** | 热更 dll(v1.1+);v1.0 随包 |
| ② 默认配置层 | `TraitProfile`(ScriptableObject) | 包内(AOT) | 出包才变(**离线兜底,非权威**) |
| ③ 运行时配置层 | JSON | RC / CDN | 拉取后生效,**唯一权威** |
| ④ 运行时框架 | `ITraitService` / `TraitRegistry` / `TraitContext` | **AOT**(D-3) | 随包 |

**为什么框架必须在 AOT**:大厅、`AdsService`、低端机降级等 AOT 代码都要读开关,而 AOT **不能**引用热更程序集。v1.4 把 `TraitService` 放热更 dll 是设计错误。正确形状:**AOT 提供服务与注册表,热更只提供 Trait 子类**。

**禁止把服务端下发的配置存成 SO** —— "改 JSON = 改运营策略"才是这套体系的意义;SO 承载运行期配置 = 每次调策略都要出包。

SO 只做两件辅助事:

1. `TraitProfile` 默认值资产:离线兜底,Inspector 可见可调
2. `TraitCatalog` 目录清单:编辑器里管理"有哪些 Trait、属于哪族、灰度实验定义"

### 5.2 配置通道四分管

| 通道 | 职责 | 内容 | 时延特征 |
|---|---|---|---|
| Remote Config | 少量紧急开关 | 总开关、广告熔断 | **受 fetch 节流限制**(生产默认最小拉取间隔较长),不适合频繁改 |
| CDN JSON | 结构化业务配置 | 模块清单、Trait 参数、奖励表、活动 | 启动拉取 + TTL,可做到"分钟级",是业务配置主通道 |
| Addressables | 资源 | 关卡包、皮肤、图集 | 按需下载 |
| HybridCLR | 代码 | 玩法 / Trait dll | 冷启动生效 |

> **v1.4 错误已修正**:v1.4 代码注释写 RC"秒级生效",表格又写"下次拉取生效"。事实是 RC 有最小拉取间隔与节流,**不存在秒级**;要更快必须靠 CDN JSON 的短 TTL,且仍然是"下次启动/下次拉取"生效。运营预期按此对齐。

**读取优先级(写死,后者覆盖前者)**:
`① 代码默认值 → ② 包内 TraitProfile → ③ 本地缓存的上次远程配置 → ④ 本次在线远程配置`
即在线配置优先级最高,代码默认值最低。任何"以哪个为准"的争议按这条链解决。

**容灾**:配置拉取失败 → 用 ③(无缓存则 ①)+ Crashlytics non-fatal 上报;**绝不让配置拉取失败阻断启动**。

### 5.3 参考实现

```csharp
// —— AOT 侧:服务与注册表(可注入、可 mock)——
public interface ITraitService
{
    bool IsEnabled(string traitId);
    T GetParam<T>(string traitId, string key, T fallback);
    IReadOnlyList<string> ActiveTraitIds { get; }   // 埋点归因用(§8.4)
}

public sealed class TraitService : ITraitService     // AOT 实现, VContainer 注册
{
    // 内部按 §5.2 的四级链取值; 拉取、缓存、容灾都在这里
}

public sealed class TraitRegistry                    // AOT
{
    public void ScanAssembly(Assembly asm);          // 扫描 [Trait("id")] 注册
    public Trait Find(string id);                    // 未注册 → null, 开关静默失效
    public void ApplyAllEnabled(TraitContext ctx);   // Boot 后统一 Apply
}

public abstract class Trait                          // AOT 基类
{
    public abstract string Id { get; }
    public abstract void Apply(TraitContext ctx);     // ctx 携带服务接口 + 参数读取
}

// —— 热更侧:只放 Trait 子类 ——
[Trait("ads.interstitial_protect")]
public sealed class InterstitialProtectTrait : Trait
{
    public override string Id => "ads.interstitial_protect";
    public override void Apply(TraitContext ctx) =>
        ctx.Ads.SetInterstitialCooldownSec(ctx.GetParam("cooldownSec", 120));
}

// —— 包内默认值(离线兜底, 非权威) ——
[CreateAssetMenu(menuName = "Box/TraitProfile")]
public sealed class TraitProfile : ScriptableObject
{
    public TraitToggle[] defaults;
}
```

远程 JSON 形状:

```jsonc
{ "traits": { "ads.interstitial_protect": { "enabled": true, "params": { "cooldownSec": 120 } } } }
```

**关键纪律**:业务代码一律通过 `ITraitService` 读开关,**不许硬编码**。"改 JSON"解决绝大多数运营需求;需要**新逻辑**时(新计分规则、新广告策略),v1.1 起热更一个 dll 即可新增/修改 Trait 类。

### 5.4 Trait 能力族(落地清单)

按能力族组织,每族先做 1~2 个最有价值的开关:

| 族 | 能力 | 优先级 |
|---|---|---|
| `ads.*` | 插屏频率与冷却、广告保护(刚看过广告不弹)、激励/插屏互换、无广告时间窗 | P1 |
| `difficulty.*` | 难度曲线 A/B、新用户降难度、慢速用户降难度 | P1 |
| `retention.*` | 每日签到、回归礼包与广告减免、流失预防 | P1(签到)/P4 |
| `device.*` | 低端机关特效、降分辨率缩放、降低后台算法开销 | P2 |
| `country.*` | 按国家差异化(奖励、活动、推送时段) | 门控后 |
| `skin.*` | 皮肤切换、UGC 皮肤 | 门控后 |
| `economy.*` | 金币产出/消耗系数、封顶值(D-5 单货币) | P4 |

### 5.5 灰度与 A/B

- RC / CDN 按 **占比 / 国家 / 版本 / 系统** 条件下发 → 天然灰度
- 每个 Trait 的 `params` 即可做多组对照
- 归因:`ITraitService.ActiveTraitIds` 随关键事件上报(§8.4)
- **上线顺序**:`ads.protect`(体验兜底)→ `difficulty.*`(留存 A/B)→ `retention.*`(增长)→ `device.*`(兼容)

---

## 6. 商业化

### 6.1 广告栈

- 起步 **AdMob 单栈**(项目已接,对齐 [08_阶段C_商业化接入指南.md](./08_阶段C_商业化接入指南.md))
- 架构留聚合位:`IAdsService` 后面可换实现(现工程已有该接口,见 [ServiceInterfaces.cs](../Sudoku/Assets/Features/Gameplay/Services/ServiceInterfaces.cs))
- 多聚合冗余竞价是大厂做法,个人项目先单栈,**eCPM 不佳再上聚合**(D-9)

### 6.2 广告位表(v1.4 的数独失败态已修正)

> **修正说明**:数独**没有失败态**(设计是错误检测开关,见 01/07),v1.4 表里的"填错结束→激励复活"不成立,已改为数独真实存在的触点。

| 位置 | 形式 | 触发 | 保护 |
|---|---|---|---|
| 数独·提示耗尽 | 激励视频(+1 次提示) | 免费提示用尽后点提示 | 每日次数上限(`ads.*` Trait) |
| 数独·撤销耗尽 | 激励视频(+N 次撤销) | 撤销额度用尽 | 同上 |
| 数独·结算 | 激励视频(金币翻倍) | 通关结算页 | — |
| 消除类玩法·失败 | 激励视频(复活/续命) | Game Over(仅有失败态的玩法) | 每日复活上限 |
| 大厅/切换 | 插屏 | 返回大厅、玩法切换、结算后 | `interstitial_protect` 冷却 + 行为保护(刚看激励不弹插屏) |
| 签到/回归 | 激励视频(奖励翻倍) | 签到面板 | — |

### 6.3 防刷

- 激励奖励服务端校验(AdMob SSV,需自建接收端点 —— 现阶段延后,用次数限制 + 时间窗兜底)
- 复活/奖励次数进加密存档(§8.1)+ 单日窗口限制
- **明确边界**:本地手段只防"顺手改",挡不住逆向;真金白银口径最终靠 SSV

---

## 7. 留存与增长

### 7.1 机制清单

| 机制 | 落地 |
|---|---|
| 每日签到 | `retention.daily_sign` Trait + 奖励表(CDN JSON),奖励发到 `box.coins` |
| 回归礼包 | 流失检测(N 天未开)→ 回归奖励 + 广告减免 |
| 交叉导量 | 大厅推荐位(`sortOrder` + Trait 干预)+ 玩法内"换游戏"按钮 |
| 每日挑战 | 数独已有,可扩展为"今日推荐玩法" |
| 流失预防推送 | 需推送通道,门控后评估 |

数据口径统一走 Firebase Analytics 自定义事件:`module_enter/exit`、`ad_show/complete`、`signin_*`、`comeback_*`,做 D1/D3/D7 漏斗。

### 7.2 跨玩法统一经济(v2.0 新增,D-5)

盒子的留存杠杆不是"玩法多",而是**"在 A 赚的能在 B 花"**。因此:

- **全盒子只有一种货币 `box.coins`**,任何玩法**不得**发行独立货币
- 统一出口:`IEconomyService`(AOT)
  - `long Balance { get; }`
  - `bool TrySpend(long amount, string sink)` / `void Earn(long amount, string source)`
  - `source/sink` 为枚举式字符串(`sudoku.win`、`tictactoe.win`、`signin.daily`、`sudoku.hint`…),直接进埋点做经济曲线
- 每个玩法只声明"产出速率上限"与"消耗点",系数由 `economy.*` Trait 下发,便于统一调控通胀
- **P0 就要把 `box.coins` 写进存档 schema**(§8.1),否则第 2 个玩法上线时必须改存档格式并写第二个迁移器

---

## 8. 数据与关卡

### 8.1 统一 SaveService(D-7)

**现状**:存档实为 `PlayerPrefs` + `JsonUtility`(§1.4),没有加密也没有 `ISaveService`。所以这不是"升级格式",而是**新建存档层 + 写迁移器**。

```jsonc
{
  "schemaVersion": 1,
  "box": {
    "coins": 350,
    "signin": { "last": "2026-08-19", "streak": 5 },
    "installedAt": "...",
    "lastModuleId": "sudoku"
  },
  "modules": {
    "sudoku":    { "current": "...", "stats": { "played": 42, "best": 312 } },
    "tictactoe": { "wins": 7, "losses": 3 }
  }
}
```

- 载体:`Application.persistentDataPath` 下单文件 + **AES-GCM(或 AES-CBC + HMAC)**;`PlayerPrefs` 只留音量/语言等偏好
- **迁移器 v0→v1**:读旧 `PlayerPrefs` 键(统计、设置、进度)→ 写入新结构 → 标记完成,保留旧键一个版本以便回滚
- 写入策略:原子写(临时文件 + 替换)+ 单份备份,防写入中途被杀进程损坏
- 模块只读写自己的 `modules.<id>` 分区,通过 `ISaveService`;`box.*` 只有 Shell/Economy 能写
- 定位说明:加密**防误改与简单篡改**,不是防作弊核心;真金白银靠 SSV(§6.3)

### 8.2 存档与配置的兼容纪律

- `schemaVersion` 单调递增,客户端**只升不降**;遇到更高版本 → 只读模式 + 提示升级,绝不静默丢数据
- 新增字段一律带默认值;删除字段先保留一个版本

### 8.3 关卡资源格式

- 远程关卡包用**自定义位压缩格式**(如每格 5 bit:1 bit 是否为题面 + 4 bit 数字,9×9 ≈ 51 字节/关),一个 bin 可容纳数百关
- 关卡由 `SudokuCore` 生成器自行产出并校验唯一解,**只用自研格式与自产内容**
- 新关卡包 = 热更资源(§4.3),按难度分包,便于按需下载

### 8.4 埋点与配置命名契约(v2.0 新增)

- 事件名:`{module_id}.{action}`,全小写下划线;模块前缀取自清单 `id`,不许手写字面量(用脚手架生成的常量)
- 公共参数:`app_version`、`code_version`、`content_version`、`config_version`、`active_traits`(截断到 N 个)
- Trait 参数命名:`{族}.{能力}.{参数}`(如 `ads.interstitial_protect.cooldownSec`),与 Trait id 严格同名前缀
- **事件表版本化**:`docs` 下维护事件表并带版本号;新增事件必须先进表再写代码(否则半年后无法做同比)

---

## 9. 安全与合规

### 9.1 安全(够用原则)

| 层面 | 我们的做法 |
|---|---|
| 代码 | IL2CPP(天然第一道)+ 关键逻辑(广告/存档)内联化,不做重型加固 |
| 资源 | 需要时用 Addressables 自定义解密 Provider,密钥藏原生层 |
| 配置 | 运营配置明文无所谓;**奖励相关加 HMAC** |
| 存档 | AES + HMAC + 原子写(§8.1) |
| 预算 | IAA 小游戏防破解投产比极低,做到"增加成本"即止 |

### 9.2 Google Play 上架合规要点

- 分发:**AAB + Play App Signing;必须包含 ARM64**(§1.4:当前仅 ARMv7,是上架硬阻塞);IL2CPP;targetSdk 跟随 Play 最新要求
- 隐私:UMP 同意框架 + 隐私政策 + 数据安全表单 + 广告 SDK 数据披露
- 安全:Play Integrity(可选,门控后评估)

**代码热更的合规边界(风险等级 🔴 HIGH)**

Play"设备和网络滥用"政策禁止从 Play 以外来源下载可执行代码,也禁止用 Play 更新机制以外的方式更新应用自身。HybridCLR 下发的 IL dll 处于**灰色地带**:行业广泛在用(解释器内执行有例外空间),但**无官方豁免**,被拒先例存在。对策分级:

1. **v1.0 纯 AOT 自包含上架(D-2)** —— 首版**根本不下载任何代码**,零风险
2. **v1.1 启用远程 dll 前,用独立空壳包单独提交测试**,验证审核反应,不暴露主包名
3. **预写申诉材料**:论证 IL dll 在解释器内执行、等同脚本资源,非 dex/so 原生可执行代码
4. **风险预案**:准备"一键关闭远程 dll、退回纯 AOT 发版"的构建开关(D-2 已天然具备),被质询时可当天切回

> **v1.4 的错误已纠正**:v1.4 建议"热更 dll 走 Play Asset Delivery 以缩小指控面"。**PAD 不是热更通道** —— asset pack 的内容随 AAB 版本绑定,要换内容仍需上传新版本。PAD 只解决"资源不占主包体积"和"用 Google CDN 分发",**解决不了"不发版上新玩法"**,因此不能作为热更方案;如需减小首包,PAD 可作为**资源**分发的独立议题(门控后评估)。

---

## 10. 工程目录与迁移步骤

### 10.1 目标目录

```text
Assets/
  App/
    Boot/                 # 新增:Boot 流程(初始化链 + 配置/热更检查)
    Scenes/
      BootScene           # 新增:极简启动场景
      HomeScene           # 新增:大厅(现 Menu.unity 演化)
      Gameplay            # 现有,迁入 Modules/Sudoku 后淡出
  ModuleFramework/        # 新增:盒子框架,全 AOT(asmdef: Box.ModuleFramework)
    IGameModule.cs / IModuleLoader.cs / ModuleLoader.cs
    ModuleContext.cs / ModuleCatalog.asset
  UIKit/                  # 新增:UI 薄层,全 AOT(asmdef: Box.UIKit,§3.5)
    IUIService.cs / UIRouter.cs / UIView.cs / UILayer.cs
    PopupArbiter.cs       #   弹窗/插屏抢屏仲裁(P0 必须有)
    SafeAreaFitter.cs
  Home/                   # 新增:大厅 UI(入口网格/签到/推荐位,AOT)
  Services/               # 新增独立程序集(从 Sudoku.Gameplay 拆出,AOT)
    Box.Services.Abstractions.asmdef   # 纯接口, 热更侧唯一可引用的服务程序集
    Box.Services.asmdef                # 实现: Ads/Iap/Save/Analytics/Trait/Audio/Economy
  HotUpdate/              # 新增:热更程序集(v1.0 随包 AOT 编译)
    HotUpdate.Core/
    Sudoku/               #   数独玩法逻辑(从 Features/Gameplay 迁入)
    TicTacToe/
    Traits/               #   只放 Trait 子类
  Modules/                # 新增:玩法资源 + 独立 Addressables Group
    Sudoku/  TicTacToe/
  Core/SudokuCore/        # 已有,保持纯 C#(AOT,性能敏感)
  Infrastructure/         # SDK 适配(AOT)
  Tests/
    EditMode/             # 已有 Sudoku.Core.Tests
    PlayMode/             # 新增:Box.PlayMode.Tests(模块加载/内存/降级)
  Resources/              # 逐步清空:仅保留必须随包的极小兜底
  StreamingAssets/        # 尽量不放热更产物(D-1 走 Addressables)
```

### 10.2 迁移步骤(顺序不可乱)

1. **拆程序集**:`Sudoku.Gameplay` → `Box.Services.Abstractions` + `Box.Services` + `HotUpdate.Sudoku`(先拆接口,再搬实现,每步保持可编译可运行)
2. **装基础设施**:Addressables → UniTask →(VContainer)→ 各自最小示例验证真机包
3. **Resources → Addressables**:`Resources/Art`、`Resources/Audio` 按模块归组;保留一个最小 `Resources` 兜底(启动 UI)
4. **存档层**:新建 `ISaveService` + 迁移器(§8.1),旧 `PlayerPrefs` 读路径保留一个版本
5. **场景重构**:`Menu.unity` → `HomeScene` + `BootScene`;`Gameplay.unity` 的内容变为数独模块入口 prefab
6. **接入框架**:数独改为 `IGameModule`,走 `IModuleLoader` 进出
7. **HybridCLR**:安装 → 配置热更程序集列表 → 先出**纯 AOT 包**验证不回归 → 再验证解释执行模式可跑(不上线)

---

## 11. 预算重算(v2.0 新增)

03 文档的预算是"单机数独"口径,盒子化后必须重新分配,否则第 3 个玩法上线时首包必然超标。

| 指标 | 总预算 | 分配 |
|---|---|---|
| 首包(AAB 下载体积) | ≤ 60MB | Unity + SDK 底座 ~30MB;Shell + 大厅 ≤ 6MB;数独(随包)≤ 8MB;字体子集 ≤ 3MB;音频 ≤ 4MB;HybridCLR metadata + 热更程序集(v1.0 AOT)≤ 3MB;余量 ≥ 6MB |
| 新玩法首次下载 | ≤ 8MB/玩法 | 超出即砍资源(§3.3) |
| 运行内存(低端机) | ≤ 200MB | 大厅常驻 ≤ 60MB;单玩法峰值 ≤ 100MB;切换后回落到大厅基线 ±10MB(§15 用例) |
| 冷启动到大厅可交互 | ≤ 2.5s(中端机) | BootScene ≤ 0.5s;初始化链 ≤ 1s(全部带超时,不等网络);大厅渲染 ≤ 1s |
| 玩法进入时延 | ≤ 1.5s(已缓存) | 超出加 loading 过场动画,但不放弃指标 |

每个指标在 P0 建立 Profiler 基线,之后每个 Phase 出包复测一次;**超标不允许进下一 Phase**。

---

## 12. 路线图

### 12.1 阶段表(工期已对齐,见 §12.2)

| Phase | 内容 | 验收标准 | 工期 |
|---|---|---|---|
| **P-1 前置(硬门槛)** | **引擎迁移到 Unity `6000.3.20f1` 国际版(D-12,§4.9:先做 Gate 1 实证 → 副本迁移 → TMP/Gradle 模板/EDM4U 重做)**;工程债务清理:**ARM64 + IL2CPP + AAB + targetSdk 35 + managedStrippingLevel + minSdk 25(D-13,2026-08-21 已实测定版)** 一次配齐;`BuildScript` 拆 APK/AAB 双入口;开局生成移出主线程;Canvas 静/动分层;Profiler 基线与工作流 | 空工程里 HybridCLR Installer + `Generate/All` 通过;副本在 6000.3.20f1 下 EditMode 全绿、IL2CPP+ARM64 真机包跑通"启动→单局→激励广告→返回"、**字体/UI 无异常**;§11 五项基线有数;§4.9 待确认两项已回填 | 2~2.5 周 |
| **P0-a 基础设施** | 装 Addressables / UniTask /(VContainer)/(Unity IAP 启用 `SUDOKU_IAP`);拆 `Box.Services.Abstractions` + `Box.Services`;`Resources → Addressables` 迁移;建 PlayMode 测试程序集;新存档层 + v0→v1 迁移器 | 真机包功能零回归;旧存档可无损升级;PlayMode 测试可跑 | 1.5 周 |
| **P0-b 盒子骨架** | ModuleFramework(AOT)+ 清单三级兜底 + BootScene/HomeScene;**`UIKit` 薄层(§3.5,含弹窗互斥仲裁)**;数独迁入 `HotUpdate.Sudoku` 并改为 `IGameModule`;HybridCLR 安装 + **纯 AOT 出包模式**打通;解释执行模式本地验证(不上线) | 数独可从大厅进出,内存回落达标;插屏与弹窗不叠加(用例覆盖);纯 AOT 包无回归;解释执行包本地可跑 | 2.5~3 周 |
| **P1 Trait 最小运行时** | AOT 侧 `ITraitService`/`TraitRegistry` + RC/CDN 拉取 + 四级优先链 + 容灾;**≤10 个 Trait**:`ads.interstitial_protect`、`difficulty.new_user`、`retention.daily_sign` | 改远程 JSON 下次启动生效;拉取失败走缓存不阻断;四级优先链有单测 | 1.5 周 |
| **P2 第二玩法** | 井字棋(走 §3.3 规范 + §3.4 脚手架)+ 交叉导量入口 + `IEconomyService`/`box.coins` 打通 | 双玩法可互跳;金币跨玩法可赚可花;切换 20 次无 OOM | 1.5 周 |
| **▶ v1.0 上架** | **自包含纯 AOT 盒子**:无网可完整玩;远程管线代码就位但不参与出包 | Play 上架通过(首版零远程代码风险);§11 全部达标 | 1.5~2 周 |
| **P3 热更启用(v1.1)** | 解释执行 + Remote Catalog + 远程关卡包 + dll 回滚 + 空壳包合规试水(§9.2) | §4.7 六项真机 checklist 全绿 | 3 周 |
| **P4 商业化完整化** | 签到/回归礼包 + 广告保护完整化 + SSV 评估 + `economy.*` 调控 | D1/D7 与经济曲线可在看板读出 | 2 周 |
| **P5(可选)** | 第 3 个玩法 / 聚合 / UGC 皮肤 / 国家策略 / HybridCLR 商业版 | 仅在 §12.3 门控通过后启动 | — |

### 12.2 工期口径(修掉 v1.4 的算术矛盾)

- **P-1 → v1.0 上架合计**:(2~2.5) + 1.5 + (2.5~3) + 1.5 + 1.5 + (1.5~2) = **10.5~12 周(净开发)**
- 加 25% 缓冲(商店往返、材料、合规、返工)→ **13~15 周,合理区间 13~16 周(约 3~4 个月)**
- 说明:P-1 因跨大版本引擎迁移(2022.3 → 6000.3)比原估多 0.5 周;这笔钱现在花最便宜(§4.9)
- v1.4 表格净和是 8~10 周而正文写 12~16 周,口径不一致;v2.0 统一按"净开发 + 明示缓冲"表述
- **前置说明**:R3 不在 P0 关键路径(P0 只需要 UniTask + 事件/DI);需要响应式再引入,避免同时上五套新基础设施

### 12.3 数据门控(v2.0 新增,止损线)

**盒子化的赌注是"多玩法提升留存/LTV",这个假设必须先被数据验证,再继续投人。** v1.0 上架后收集 2~4 周:

| 门 | 条件 | 通过 → | 不通过 → |
|---|---|---|---|
| G1 稳定性 | 崩溃率达标、ANR 正常、留存漏斗数据可读 | 进 P3 | 先修质量,不加内容 |
| G2 盒子假设 | 大厅→第二玩法**进入率 ≥ 15%**,且玩过 ≥2 个玩法的用户 D7 明显高于单玩法用户 | 做第 3 个玩法 + 启用远程热更 | **停止扩玩法**,把资源投回数独深度与买量素材 |
| G3 变现 | 单用户日均广告展示与 eCPM 达到可评估水平 | 进 P4 完整化 | 先调广告位与频控 |

**G2 不通过时明确不做的事**:不做第 3 个玩法、不启用远程 dll(连带避开 §9.2 的 HIGH 风险)、不做国家策略。这条止损线是 v1.4 缺失的最大治理漏洞。

---

## 13. 待拍板决策点(汇总,详见 §0)

1. **D-4 玩法数量**:v1.0 做 2 个玩法(推荐)还是 3 个?
2. **D-8 HybridCLR 版本**:社区版起步(推荐)还是直接商业版?
3. **D-9 广告聚合**:AdMob 单栈(推荐)还是直接上聚合?
4. **Unity IAP**:P-1 一并接通"去广告"(推荐,现状是 `#if SUDOKU_IAP` 未启用)还是推到 P4?
5. **keystore**:P-1 就生成正式 upload key 并纳入密钥管理(推荐)还是先用 debug 签名做本地测试、上架前再建?
6. **SSV**:P4 接受"本地校验 + 频控"过渡(推荐)还是直接搭接收端?
7. **R3**:是否引入(建议 P2 之后再评估;P0 只用 UniTask + DI)

已拍板(见 §0):D-1 下发通道、D-2 v1.0 纯 AOT、D-3 Trait 分层、D-5 单货币、D-6 配置四分管、D-7 存档、D-10 本文入库、D-11 不引入全家桶框架自研 `UIKit`、**D-12 引擎 `6000.3.20f1` 国际版**、**D-13 minSdk 25(2026-08-21 实测定版,原 24 为引擎下限所替代)**。

§4.9 待回填(不阻塞开工):① `6000.3` 的 LTS 标记;② 该版本可选的 targetSdk 是否满足 Play 当前强制值。

---

## 14. 对照总表(能力 → Unity 实现)

| 盒子能力 | Unity 实现 | 章节 |
|---|---|---|
| 大厅 + N 玩法 | HomeScene + `IGameModule` + `ModuleCatalog`/清单 | §3 |
| 动态模块加载 | Addressables Group 按需加载/卸载 | §3.2 |
| 增量热更 | Addressables Remote Catalog + Content Update | §4.1 |
| 上新玩法不发版 | 新玩法 Group + dll 远程下发 + 清单开关 | §4.3 |
| 代码级热更 | HybridCLR,两阶段启用(v1.0 纯 AOT / v1.1 解释执行) | §4.4 |
| 运营开关体系 | AOT 侧 `ITraitService` + 热更 Trait 类 + 远程 JSON + SO 兜底 | §5 |
| 灰度/A-B/按国家 | RC/CDN 条件下发 + `active_traits` 归因 | §5.5 |
| 广告变现 | AdMob(`IAdsService` 留聚合位)+ 广告位表 + `ads.*` 频控 | §6 |
| 留存增长 | `retention.*` + 大厅推荐位 + 统一货币 | §7 |
| 跨玩法经济 | `IEconomyService` 单货币 `box.coins` | §7.2 |
| 关卡下发 | 自研位压缩关卡包 + 按难度分包 | §8.3 |
| 版本解耦 | code/content/config 三版本号 + 回滚 + 安全模式 | §4.5 |
| 离线可玩 | 纯 AOT 首包 + Addressables 缓存 + 入口灰化 | §4.6 |
| 上架合规 | AAB / ARM64 / UMP / 热更分级策略(PAD 不作为热更通道) | §9.2 |
| 质量底线 | 预算表 + PlayMode/EditMode 用例 + 数据门控 | §11 §15 §12.3 |
| UI 栈与抢屏治理 | 自研 `Box.UIKit` 薄层(层级/路由/生命周期/弹窗仲裁,全 AOT) | §3.5 |
| 引擎版本治理 | 国际版 + 当前 LTS 家族最新补丁,按排除法定版 | §4.9 |

---

## 15. 测试策略与质量底线

模块化 + 热更是 bug 温床,**每个 Phase 的关键用例与功能同 PR 交付**。

| 要测的 | 怎么测 | 阶段 |
|---|---|---|
| 存档迁移 v0→v1 | EditMode:构造旧 `PlayerPrefs` 数据 → 迁移 → 断言字段无损 + 幂等(重复迁移不翻倍) | P0-a |
| 模块加载/卸载泄漏 | PlayMode + Memory Profiler:进出 10 次,断言回落到大厅基线 ±10MB | P0-b |
| 模块清单容错 | EditMode:缺字段/多未知字段/`minAppVersion` 过高/整体畸形 JSON → 不崩、走兜底 | P0-b |
| Trait 四级优先链 | EditMode:mock 在线/缓存/包内 SO/代码默认 → 断言"后者覆盖前者" | P1 |
| 配置拉取失败容灾 | mock fetch 失败 → 用缓存值 + non-fatal 上报,**启动不阻断** | P1 |
| 统一货币 | EditMode:并发 Earn/TrySpend、余额不足、跨玩法读写 → 断言不出现负数与丢单 | P2 |
| **弹窗抢屏** | PlayMode:同帧请求"插屏 + 签到弹窗 + 升级提示" → 断言仲裁后串行呈现、无叠加、无输入锁死 | P0-b |
| **UI 栈与返回键** | PlayMode:深层 Push 后连按返回 → 断言逐层 Pop、模块退出唯一路径、无残留 Canvas | P0-b |
| 多玩法切换 OOM | 真机低端机循环切换 20 次 + 峰值内存监控 | P2 |
| `SudokuCore` 回归 | 现有 NUnit(唯一解/难度)保持全绿。**注意:`SudokuCore` 永久留 AOT,不迁热更程序集**(v1.4 §14 表述有误) | 全程 |
| 热更 dll 失败回滚 | 构造坏 dll(改字节/截断)→ 启动 → 断言回滚 + 入口灰化 + 上报 | P3 |
| 无网降级 | 断网 + 清缓存 + 冷启动 → 大厅可进、缓存玩法可玩、未缓存入口灰化 | P3 |
| 弱网/中断 | 限速与中途断网下的下载可续、不产生半包 | P3 |

---

## 16. 文档治理

| 项 | 状态 | 说明 / 剩余动作 |
|---|---|---|
| 本文入库(D-10) | ✅ **已完成** | `.gitignore` 已移除 `docs/11_…` 与 `docs/07_…` 的忽略规则;`_private/` 与 `docs/10_…`(调研原文)继续忽略。**剩余动作:提交这次改动**,此后架构演进都有 diff 与评审留痕 |
| 索引 | ✅ 已完成 | `docs/README.md` 导航补齐 08 / 09 / 11 |
| 决策冲突 | ✅ 已完成 | `docs/README.md` 决策摘要"首期不使用 HybridCLR" → 改为 v1.0 纯 AOT + 程序集拆分口径,并加"盒子化受数据门控约束"一行 |
| 优先级冲突 | ✅ 已完成 | 03 升 v1.2:Addressables 升为盒子路线 P0、补 HybridCLR 行、构建行加 **ARM64(必须)** |
| 07 对齐 | ✅ 已完成 | 07 升 v1.2:"不采用 HybridCLR" → "不启用解释执行/远程下发,但程序集提前拆好" |
| 参数冲突 | ✅ 已决(已落地) | **D-13:minSdk 定版 25**(2026-08-21 实测:引擎下限 25,24 已 obsolete)→ 工程 `ProjectSettings.asset` + 03/10/本文全部回写完成 |
| 引擎表述 | ✅ 已完成 | D-12 定版 `6000.3.20f1` 后,03 / 07 / README 的引擎基线表述已同步;**07 文件名仍含"Unity2022"** —— 抬头已注明"仅为历史标题",迁移验收通过后可考虑改名 |
| 备份 | ✅ | v1.4 原文存 `_private/11_Unity游戏盒子架构方案_v1.4_backup.md` |
| 版本纪律 | — | 本文每次结构性演进升次版本号并在抬头写变更摘要;§4.9 定版后必须回填结论与查验日期 |
