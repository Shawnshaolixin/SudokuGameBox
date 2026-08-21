# 10 — Phase 执行计划（权威执行手册）

> 版本:v1.0 | 状态:Approved(2026-08-21 用户拍板) | 说明:**本文是项目执行的唯一权威计划**。
> 与 [06_项目路线图.md](./06_项目路线图.md)、[07_Unity2022落地版最小开发路线图.md](./07_Unity2022落地版最小开发路线图.md) 冲突时**以本文为准**（06/07 为早期讨论稿，技术基线已被 [11_Unity游戏盒子架构方案.md](./11_Unity游戏盒子架构方案.md) v2.2 与本文覆盖）。
> 架构决策见 D-1~D-13（11 文档 + 本文 §3）。

---

## 1. 执行纪律（每 Phase 必须遵守）

```
分析 → 计划 → 执行 → 编译 → 测试 → 检查 Git Diff → 总结 → 用户确认 → 进入下一 Phase
```

1. **每 Phase 结束必须向用户汇报**（做了什么 / 验证了什么 / 下一步是什么），用户确认后才进入下一 Phase。
2. **不可逆操作（删除、替换、提权、外部发布）必须提前说明并获授权**。
3. **红线（逐字遵守，见 §4）**。
4. 工具分工：优先 Unity CLI 无头/BatchMode；仅编辑器特有操作（场景操作、可视化检查）才用 Unity MCP；不安装第三方 MCP Server。
5. 禁止无限埋头执行——每 Phase 有明确停止点（验收项全部完成或阻塞上报）。

## 2. 技术基线（速查）

| 项 | 值 | 决策 |
|---|---|---|
| Unity | 6000.3.20f1 国际版 | D-12 |
| 平台 | Android / IL2CPP / ARM64 / AAB | D-12 |
| minSdk | **25**（2026-08-21 Phase 1 实测：Unity 6000.3 最低支持 API=25，"以引擎为准"条款触发，03/11 已回写） | D-13 |
| 热更框架 | HybridCLR 社区版（v1.1 启用） | D-1 |
| 资源 | Addressables（v1.1 Remote Catalog 远程下发，禁止自建 dll 缓存） | D-1 |
| 架构 | Trait 全 AOT + 热更侧 Trait 子类；UIKit 自研薄层 600~900 行全 AOT | D-3/D-11 |
| 存档 | AES-GCM 单文件分区存档 + v0→v1 迁移器 | D-7 |
| 预算 | 首包 ≤60MB / 新玩法 ≤8MB / 内存 ≤200MB / 冷启动 ≤2.5s | D-14 |
| 权限 | SudokuCore 永久留 AOT，禁止进热更程序集 | 红线 |

## 3. 决策清单 D-1 ~ D-16（2026-08-21 与 11 文档 D 表一次对齐，变更需用户拍板）

| # | 决策 | 状态 |
|---|---|---|
| D-1 | 热更通道：HybridCLR 社区版 + Addressables Remote Catalog 远程下发（禁止自建 dll 缓存） | ✅ |
| D-2 | v1.0 纯 AOT 自包含（无热更依赖），v1.1 才启用解释执行 | ✅ |
| D-3 | Trait 框架全 AOT，热更侧只放 Trait 子类 | ✅ |
| D-4 | v1.0 玩法数量：数独 + 井字棋（第 3 玩法数据门控，11 文档 §12.3） | ⚠️ 推荐 2 玩法，待拍板 |
| D-5 | 单货币 box.coins | ✅ |
| D-6 | 配置四分管（数据/规则/表现/运行期） | ✅ |
| D-7 | AES-GCM 单文件分区存档 + v0→v1 迁移器 | ✅ |
| D-8 | HybridCLR 版本：社区版起步 | ⚠️ 推荐社区版，待拍板 |
| D-9 | 广告聚合：AdMob 单栈起步 | ⚠️ 推荐单栈，待拍板 |
| D-10 | 11 文档 v2.0 脱敏后入库（.gitignore 已移除 docs/11_…、docs/07_…） | ✅ |
| D-11 | 自研 UIKit 薄层（600~900 行全 AOT），不引入全家桶框架 | ✅ |
| D-12 | Unity 6000.3.20f1 国际版 + Android/IL2CPP/ARM64/AAB | ✅ |
| D-13 | minSdk **25**（引擎下限，2026-08-21 实测触发"以引擎为准"） | ✅ |
| D-14 | 预算线：首包 ≤60MB / 新玩法 ≤8MB / 内存 ≤200MB / 冷启动 ≤2.5s（11 文档 §11） | ✅ |
| D-15 | **UI 动画自研补间（UniTask 扩展 BoxTween），不引入第三方动画库（DOTween/PrimeTween）** | ✅（2026-08-21 用户拍板） |
| D-16 | 每日挑战细化 | ⚠️ Phase 4 细化 |

> 注：D-4/D-8/D-9 为 11 文档待拍板项（以 11 文档为准）；D-16 每日挑战细节 Phase 4 定。

## 4. 红线（逐字保留，任何情况下不得违反）

1. 不擅自改架构方案（改动必须经用户同意）
2. 不引入新大型框架（D-11 已定：自研 UIKit 薄层）
3. 不替换文档已定技术方案
4. **不把 SudokuCore 放进热更程序集**
5. 不破坏 AOT/HotUpdate 边界
6. 不删除已有功能
7. 不随意修改 ProjectSettings / Packages（改动需说明理由）
8. 不删除 Library / Assets / ProjectSettings 等重要目录
9. **不执行 git push**（远程由用户自理）
10. 不可逆操作提前说明并获授权

## 5. Phase 0 — 环境基线（进行中）

**目标**：环境可用性闭环 + 引擎技术验证（Gate 1 提前执行）+ 正式工程骨架就位。

### 5.1 任务与状态

| 任务 | 内容 | 状态 |
|---|---|---|
| 0-1 | 审查已有 git 仓库与 .gitignore，本地 init + 首提交 | ✅ 已提交 0e99708 |
| 0-2 | 补装 Android NDK（Unity 6000.3.20f1 自带 NDK 目录为空） | ✅ r27c 落地 + Preferences 指向（见 §5.3） |
| 0-3 | GameBox 正式工程 Unity CLI 无头编译验证 | ✅ 通过 |
| 0-4 | **Gate 1**：独立空工程验证 HybridCLR 全流程 | ✅ 通过（见 §5.3） |
| 0-5 | 验收小结 + Gate 1 报告 + 用户确认 | ✅ 已完成（2026-08-21 用户核对证据后确认，Phase 0 关闭） |

> **Phase 0 收尾拍板（2026-08-21）**：`com.unity.ai.assistant` / `com.unity.ai.inference` **保留不精简**（用户确认要使用，11 文档 §4.9 第 10 步"确认不用则移除"针对此二包作废）。

### 5.2 Gate 1 验证链（HybridCLR v8.14.1 + Unity 6000.3.20f1 + IL2CPP + .NET Standard 2.1）

```
Gate1 空工程初始化（HybridCLR_Gate1）
→ hybridclr_unity v8.14.1 安装（gitee tags: hybridclr v8.13.0 + il2cpp_plus v6000.3.x-8.14.0，InstallFromLocal）
→ GenerateAll（切 Android 目标后执行）
→ IL2CPP 构建验证（buildScriptsOnly 裁剪 + 完整 AAB 构建）
```

### 5.3 Gate 1 实测结论（2026-08-21，最终版）

- **安装** ✅ Installer 全流程走通（InstallFromLocal 绕过 GitHub clone）
- **分支修正** ⚠️ 已踩坑：6000.3.x 的 hybridclr C++ 必须用 **gitee `6000.3.x` 分支**（GitHub 的 `v6000.3.x-8.13.0` 分支名在 gitee 不存在；误用通用 `v8.13.0` tag 会导致 il2cpp API 不兼容 → C++ 编译错误 `no member named 'GetFieldDefinitionFromTypeDefAndFieldIndex'`）。il2cpp_plus 用 gitee tag `v6000.3.x-8.14.0` 正确
- **GenerateAll** ✅ 通过（产出 13 个裁剪 AOT dll + HotUpdateDlls + hybridclr 源码注入 il2cpp）
- **完整 IL2CPP 构建** ✅ `result=Succeeded totalErrors=0`（AAB 12.5MB，libil2cpp.so 9.4MB 含 46 处 HybridCLR 符号——解释器引擎已进产物）
- **NDK 排障链终版**（环境事实，记录备查）：
  - Unity 6000.3.20f1 要求 **NDK r27c（27.2.12479018）精确匹配**（`AndroidNDKRoot.k_PackageRevision`，VersionRequirementType.Exact；AGP 层还有 CXX1100 硬校验，编辑器后门绕不过 gradle）
  - 正确 Preference 键：**`AndroidNdkRootR27C`**（非 AndroidNdkRoot）
  - **必须 `NdkUseEmbedded=false`**：否则 GetRootDirectory 永远走 AndroidPlayer/NDK 默认路径，Preferences 被跳过
  - 最终落地：**NDK r27c 解压到 `D:\Projects\AI\AndroidNDK\android-ndk-r27c`** + Preferences 指向；旧 r27 + junction（AndroidPlayer/NDK → android-ndk-r27）仍保留（r27c 就位后已不依赖，后续可清理）
  - 官方后门（仅排障期用过，最终方案未启用）：`NdkDisableSettingValidation=true` 跳过 Unity 编辑器层校验
- **Gradle** ✅ AGP 9.0.0 要求 Gradle ≥9.1.0（本机 8.6 不满足），已装 `D:\Tools\gradle-9.1.0`，Preferences 键 **`GradlePath`**

### 5.4 产物

- 正式工程：GameBox/（2D URP 模板，无脚本，国际 registry）
- Gate1 验证工程：`D:\Projects\AI\HybridCLR_Gate1\`（独立，不参与正式工程）

## 6. Phase 1 — 基础工程结构

**目标**：正式工程骨架完备，可重复构建。

| 任务 | 内容 |
|---|---|
| 1-1 | 目录骨架（Assets 分层：Core/Gameplay/UI/Config/Resources/Editor…）+ asmdef 划分 |
| 1-2 | Player Settings：IL2CPP + ARM64 + AAB + minSdk 25（引擎下限，2026-08-21 实测）+ .NET Standard 2.1 |
| 1-3 | BuildScript 双入口（cli 构建 + 编辑器菜单），P0 基线 AAB 产出 |
| 1-4 | git 提交 + 验收（全新工程 CLI 可构建 AAB） |

**验收**：`unity -batchmode -executeMethod BuildScript.BuildAab` 产出可安装 AAB。

## 7. Phase 2 — SudokuCore 移植（旧代码策略：移植 + 参考重构）

**目标**：SudokuCore（生成器/求解器/难度）全 AOT 移植进正式工程，行为与旧工程一致。

| 任务 | 内容 |
|---|---|
| 2-1 | 从旧工程（Sudoku 2022.3.50f1c1）移植 SudokuCore，Gameplay/Services 按新分层参考重构 |
| 2-2 | 保留"接口 + 桩 + 真实现 + #if 开关"设计 |
| 2-3 | 单元测试：生成/求解/唯一解/难度回归（10 万谜题唯一解/难度回归目标） |

**验收**：旧工程全部测试在新工程通过；SudokuCore 仅依赖 AOT 程序集。

## 8. Phase 3 — UIKit 自研薄层 + 基础 UI

**目标**：600~900 行全 AOT UIKit 薄层（D-11）。

| 任务 | 内容 |
|---|---|
| 3-1 | UIKit 核心（View/Panel/Stack 管理、事件、动画薄封装） |
| 3-2 | 基础控件（按钮/文本/输入/弹窗/进度） |
| 3-3 | 场景框架（主菜单/对局/结算框架搭建） |

**验收**：全部 UI 组件无第三方依赖，编译期全 AOT。

## 9. Phase 4 — 游戏循环（v1.0 P0 功能）

| 任务 | 内容 |
|---|---|
| 4-1 | 主菜单 + 难度选择 |
| 4-2 | 对局流程（选格/填数/笔记/撤销重做/错误检测/计时） |
| 4-3 | 胜利判定 + 结算弹窗 |
| 4-4 | 每日挑战（D-16 细化） |

> **UI 动效（D-15，2026-08-21 拍板）**：Phase 4 起动效一律用自研补间（`BoxTween`，UniTask 扩展，含 EaseOutBack 回弹曲线），覆盖清单：弹窗弹入（scale 0.85→1 + 回弹）、按钮按下反馈（scale 0.95）、数字/笔记填入脉冲、转场（FadeAnimator 已有）、胜利结算弹入+闪烁。不引入 DOTween/PrimeTween。

**验收**：任意难度可生成、可解、可完成；生成 < 200ms。

## 10. Phase 5 — 存档系统 + 设置

| 任务 | 内容 |
|---|---|
| 5-1 | AES-GCM 单文件分区存档（D-7）+ v0→v1 迁移器 |
| 5-2 | 设置（音效/主题/语言基础） |

**验收**：存档加密落盘、迁移器可升版本、异常存档可恢复。

## 11. Phase 6 — Addressables 资源管线 + 首包瘦身

| 任务 | 内容 |
|---|---|
| 6-1 | Addressables 接入（v1.0 本地 catalog，v1.1 远程） |
| 6-2 | 资源分组策略 + 首包 ≤60MB 预算落地 |
| 6-3 | 字体子集（字体子集字符集.txt）接入 |

**验收**：全新安装首包 ≤60MB，资源加载无长帧。

## 12. Phase 7 — 商业化 + 合规（v1.0 发布前置）

| 任务 | 内容 |
|---|---|
| 7-1 | AdMob（激励视频 + 插屏，D-4 细化）+ Unity IAP（去广告等商品） |
| 7-2 | UMP 同意流程 + 隐私政策页 |
| 7-3 | 合规检查（数据安全表单、广告标识符、儿童保护） |

**验收**：商店合规清单全部通过。

## 13. Phase 8 — v1.0 发布

| 任务 | 内容 |
|---|---|
| 8-1 | AAB 最终构建 + 真机冒烟 |
| 8-2 | 商店素材（图标/截图/描述）+ 测试（internal/closed track） |
| 8-3 | 提交 Google Play（用户操作远程与账号） |

**验收**：AAB 过 Play 预检，internal track 可安装运行。

## 14. Phase 9 — v1.1 HybridCLR 热更接入（正式工程落地）

| 任务 | 内容 |
|---|---|
| 9-1 | 将 Gate 1 结论落地正式工程（安装 hybridclr_unity v8.14.1 + GenerateAll 集成构建链） |
| 9-2 | HotUpdate.Core + 热更程序集边界搭建（玩法 dll 禁止互引，只引 HotUpdate.Core + AOT 接口） |
| 9-3 | 启动链路接入热更下载（无强制网络等待，D-10 细化） |
| 9-4 | Addressables Remote Catalog 远程下发（D-1） |

**验收**：AOT 构建 + 热更 dll 加载运行闭环；首包体积增量符合预算。

## 15. Phase 10 — 热更内容管线

| 任务 | 内容 |
|---|---|
| 10-1 | 新玩法包构建/打包/上传流程 |
| 10-2 | 版本管理 + 回滚机制 |
| 10-3 | 灰度发布能力 |

**验收**：新玩法 ≤8MB 增量下发，客户端可热更运行。

## 16. Phase 11 — 运营期

| 任务 | 内容 |
|---|---|
| 11-1 | 数据埋点 + 监控（崩溃/留存/收入） |
| 11-2 | 更新节奏（bugfix 热更 / 内容包 / 大版本） |
| 11-3 | 回归 v1.0 商业闭环数据优化 |

**验收**：D1≥35%、D7≥12% 目标跟踪（沿用 06 指标，数值以 04 文档为准）。

---

## 附录 A：环境事实（2026-08-21）

- Unity CLI 1.0.0-beta.5：`C:\Users\slx97\AppData\Local\Unity\bin\unity.exe`
- Unity MCP 官方版已连接（仅编辑器特有能力使用）
- 已装编辑器：2020.3.43f1c1 / 2021.3.1f1c1 / 2022.3.50f1c1 / 6000.3.20f1
- Android SDK：Unity 自带（android-34/35/36，build-tools 36.0.0，OpenJDK 17）
- **NDK r27c（27.2.12479018）**：`D:\Projects\AI\AndroidNDK\android-ndk-r27c`（Preferences `AndroidNdkRootR27C` 指向 + `NdkUseEmbedded=false`）
- NDK r27（旧）：`D:\Projects\AI\AndroidNDK\android-ndk-r27` + AndroidPlayer/NDK junction（保留未清理，r27c 就位后不依赖）
- **Gradle 9.1.0**：`D:\Tools\gradle-9.1.0`（Preferences `GradlePath` 指向；旧 8.6 在 D:\Tools\gradle-8.6 保留）
- Gate1 工程：`D:\Projects\AI\HybridCLR_Gate1\`（验证专用，不入正式工程；含全部排障脚本与日志）
- Unity 6000.3 的 NDK 检测链（反编译结论）：`AndroidNDKTools.GetInstance` → `AndroidNDKRoot.GetRootDirectory`（UseEmbedded → Preferences `AndroidNdkRootR27C` → env）→ `Validate`（source.properties 精确版本比对）→ 失败置 `s_Instance=null` 并抛 "Android NDK not found or invalid"
