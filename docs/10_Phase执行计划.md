# 10 — Phase 执行计划（权威执行手册）

> 版本:v1.3 | 状态:Approved(2026-08-21 用户拍板;2026-08-23 增补 Phase 6.5 CI 管线 + D-17;2026-08-23 CI 工具拍板 Jenkins+SCM 轮询;2026-08-31 Phase 9 详细执行计划落地 9-1 完成 + 9-2~9-4 计划) | 说明:**本文是项目执行的唯一权威计划**。
> 与 [06_项目路线图.md](./06_项目路线图.md)、[07_Unity2022落地版最小开发路线图.md](./07_Unity2022落地版最小开发路线图.md) 冲突时**以本文为准**（06/07 为早期讨论稿，技术基线已被 [11_Unity游戏盒子架构方案.md](./11_Unity游戏盒子架构方案.md) v2.2 与本文覆盖）。
> 架构决策见 D-1~D-17（11 文档 + 本文 §3）。

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
| CI | Jenkins 本机部署（Windows 服务）+ SCM 轮询触发，本地 License 免 .ulf，无头测试/构建 | D-17 |

## 3. 决策清单 D-1 ~ D-17（2026-08-21 与 11 文档 D 表一次对齐，变更需用户拍板）

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
| D-17 | CI 管线（Jenkins 本机部署 + SCM 轮询）：Unity 无头测试 + 资产校验 + AAB 构建自动化。**核心价值 = 学习企业级 CI/自动化测试实践 + 面试素材**；v1.0 发布兜底仅为附带收益 | ✅（2026-08-23 用户拍板：Jenkins；Phase 6.5） |

> 注：D-4/D-8/D-9 为 11 文档待拍板项（以 11 文档为准）；D-16 每日挑战细节 Phase 4 定；D-17 CI 管线 Phase 6.5 落地（工具为 Jenkins，非 GitHub Actions）。

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

## 10. Phase 4.5 — 盒子骨架（ModuleFramework + 热更程序集拆分）

> **背景（2026-08-22 拍板）**：11 文档 P0-b（盒子骨架）在 10 文档无对应执行阶段，补齐。
> 中间态（决策 B）：ModuleLoader 按 entryType 反射实例化 + 模块内部场景切换，单场景收敛推迟到 Phase 6。
> v1.0 纯 AOT：热更程序集随包 IL2CPP 编译（D-2），本 Phase 只做程序集边界与入口骨架，不启用解释执行。

| 任务 | 内容 |
|---|---|
| 4.5-1 | ModuleFramework 薄骨架（IGameModule/IModuleLoader/ModuleContext/ModuleCatalog + ModuleLoader 静态注册，全 AOT；Resources 兜底清单 Assets/Resources/Config/ModuleCatalog.asset） |
| 4.5-2 | 程序集拆分 HotUpdate.Sudoku（玩法迁入热更程序集，命名空间 Box.HotUpdate.Sudoku；大厅 Box.Gameplay 不再静态引用玩法类型，依赖方向 AOT → 玩法 单向） |
| 4.5-3 | 数独迁入 IGameModule（SudokuModule 参数化入口：args="daily" 直开每日挑战，否则难度弹窗；GameContext 收敛，大厅不再持有玩法状态） |
| 4.5-4 | 埋点契约对齐（§8.4 {module_id}.{action}：sudoku.level_start / sudoku.level_complete / sudoku.hint_used） |
| 4.5-5 | link.xml 入口类型保留（Assembly 级 preserve，防 IL2CPP 裁剪导致 Type.GetType 返回 null） |

**验收**：EditMode/PlayMode 全绿；纯 AOT AAB 构建通过；大厅进出数独无回归；v1.0 无热更依赖。

## 11. Phase 5 — 存档系统 + 设置

| 任务 | 内容 |
|---|---|
| 5-1 | AES-GCM 单文件分区存档（D-7）+ v0→v1 迁移器；schema 结构按 §8.1 固化：`box.coins` / `box.signin` / `installedAt` / `lastModuleId` + `modules.<id>` 分区，模块只读写自己的 `modules.<id>`，`box.*` 仅 Shell/Economy 可写（D-5：P0 定死，第 2 玩法上线前禁止改格式） |
| 5-2 | 设置（音效/主题/语言基础） |

**验收**：存档加密落盘、迁移器可升版本、异常存档可恢复。

## 12. Phase 6 — Addressables 资源管线 + 首包瘦身

| 任务 | 内容 |
|---|---|
| 6-1 | Addressables 接入（v1.0 本地 catalog，v1.1 远程） |
| 6-2 | 资源分组策略 + 首包 ≤60MB 预算落地 |
| 6-3 | 字体子集（字体子集字符集.txt）接入 |

**验收**：全新安装首包 ≤60MB，资源加载无长帧。

## 13. Phase 6.5 — CI 管线（企业级自动化）

> **背景（2026-08-23 拍板）**：10 文档原计划无 CI 阶段。本 Phase **核心价值 = 学习企业级 CI/自动化测试实践 + 面试素材**（"每提交自动回归 + 资产命名契约进 CI"是休闲游戏岗位认可的企业实践，且 **Jenkins 是国内游戏公司 CI 标配**，面试按此逻辑讲）；v1.0 发布兜底仅为附带收益（D-17）。
> **CI 工具（2026-08-23 拍板）**：**Jenkins 本机部署**（Windows 服务），不用 GitHub Actions——本地已激活 Unity 免 .ulf 流程、无分钟额度、国内网络零依赖、国内求职对口度更高。

**目标**：本机 Jenkins 跑通 Unity 无头编译 + 测试 + 资产校验 + AAB 构建，失败阻止交付/产物归档。

| 任务 | 内容 |
|---|---|
| 6.5-1 | 首个 Jenkins job：Unity 6000.3.20f1 无头编译 + EditMode/PlayMode 测试自动跑（BatchMode 直接调本地 Unity，复用本地已激活 License） |
| 6.5-2 | CI 资产校验 job：Assets/Art/ 下 PNG 命名约定白名单检查（_btn/_panel/_icon/_particle/_bg）+ 纹理尺寸上限审计，违反即失败（纯 Python，零 Unity 依赖） |
| 6.5-3 | 构建 job：复用 BuildScript，CLI 产出 AAB，归档到 Jenkins 工作区 |

**验收**：提交/推送后自动跑测试 + 资产校验（SCM 轮询触发）；构建 job 手动触发或 main 触发，产出 AAB 归档；失败显示在 Jenkins 构建历史中并阻止交付。

**触发方式（2026-08-23 拍板）**：**SCM 轮询**（建议 1~5 分钟/次），**不用 GitHub Webhook**——本机无公网 IP，GitHub 无法回调本地 Jenkins；轮询指向本地 Git 仓库路径，检测变更自动触发，零网络依赖（红线 9：远程 push 仍由用户自理）。
>
> ⚠️ **本地 Jenkins 的限制（诚实记录）**：无法接入 PR 状态检查（GitHub 回调不进来），"失败阻止合并"改为"失败阻止交付/产物归档"；CI 结果在 Jenkins 页面查看，不上 GitHub。面试讲这个故事时说明理由（无公网 IP → SCM 轮询），反而是体现对 CI 原理理解的加分点。

**成本纪律**（CI 又贵又折腾，必须克制）：
- 换 Jenkins 后**没有 GitHub 分钟额度问题**，但本机维护成本替代之：PC 需保持开机、构建期间占用 CPU（可错开日常使用时段）
- **6.5-2 资产校验用纯 Python**（读 PNG 头做尺寸审计 + 文件名白名单正则），不启动 Unity——唯一廉价的常驻 job
- 6.5-1 测试 job 仅在提交 main / 手动触发时跑；6.5-3 构建 job 手动触发为主，避免每次提交都构建
- **免 .ulf 流程**：复用本地已激活 Unity（6000.3.20f1），BatchMode 直接跑，无需云端 License 激活
- **范围克制**：只做 6.5-1/2/3，不扩展多平台矩阵 / 缓存深度调优 / 自动化截图对比等锦上添花项（不符合目的导向）

**前置（环境事实）**：
- 工具分工：全部 Unity CLI 无头/BatchMode，符合 §1 纪律
- Jenkins 部署前置：JDK + Jenkins Windows 服务 + Git 插件；SCM 指向本地仓库路径
- 远程推送由用户自理（红线 9），Jenkins 轮询本地变更即触发
- 6.5-3 复用现有 `Assets/Editor/BuildScript.cs`（双入口已具备）
- 测试报告：Jenkins 解析 Unity 的 `TestResults/*.xml` 展示趋势（沿用现有 EditMode/PlayMode XML 产出）

## 14. Phase 7 — 商业化 + 合规（v1.0 发布前置）

| 任务 | 内容 |
|---|---|
| 7-1 | AdMob（激励视频 + 插屏，D-4 细化）+ Unity IAP（去广告等商品） |
| 7-2 | UMP 同意流程 + 隐私政策页 |
| 7-3 | 合规检查（数据安全表单、广告标识符、儿童保护） |

**验收**：商店合规清单全部通过。

## 15. Phase 8 — v1.0 发布

| 任务 | 内容 |
|---|---|
| 8-1 | AAB 最终构建 + 真机冒烟 |
| 8-2 | 商店素材（图标/截图/描述）+ 测试（internal/closed track） |
| 8-3 | 提交 Google Play（用户操作远程与账号） |

**验收**：AAB 过 Play 预检，internal track 可安装运行。

## 16. Phase 9 — v1.1 HybridCLR 热更接入（正式工程落地）

> 状态：**9-1 ✅ 完成（2026-08-31，dev/phase9 分支）**；9-2~9-4 待执行（详细计划见下）
> 分支：`dev/phase9`（基于 main de5d2dc）；红线 6（v1.0 构建链不破坏）与红线 9（不 push）全程适用

| 任务 | 内容 | 状态 |
|---|---|---|
| 9-1 | 将 Gate 1 结论落地正式工程（安装 hybridclr_unity v8.14.1 + GenerateAll 集成构建链） | ✅ 2026-08-31 |
| 9-2 | HotUpdate.Core + 热更程序集边界搭建（玩法 dll 禁止互引，只引 HotUpdate.Core + AOT 接口） | 待执行 |
| 9-3 | 启动链路接入热更下载（无强制网络等待，D-10 细化） | 待执行 |
| 9-4 | Addressables Remote Catalog 远程下发（D-1） | 待执行 |

**验收**：AOT 构建 + 热更 dll 加载运行闭环；首包体积增量符合预算。

### 16.1 关键机制结论（9-1 从 v8.14.1 包源码实证，后续任务直接引用）

1. **D-2 构建开关 = `ProjectSettings/HybridCLRSettings.asset` 的 `enable` 字段**（包默认 `true`！）：
   `FilterHotFixAssemblies`（IFilterBuildAssemblies）在 enable=true 时把名单内程序集从主包过滤，
   `CheckSettings`（IPreprocessBuild）强制 IL2CPP 并指向 hybridclr 运行时。
   → v1.0 = enable=false（Filter 不介入、原版 il2cpp）；**首次创建该资产必须显式置 false 并入库**（已做，97533e4）。
2. **官方禁止 AOT 程序集引用热更程序集** → 热更 asmdef 应 `autoReferenced=false`。
   当前 `Box.HotUpdate.Sudoku` 是 autoReferenced=true（v1.0 靠隐式引用进包），v1.1 模式必然悬垂引用。
   **解法 = 模式条件桥（9-2）**：AOT 侧 `Box.ModuleBridge`（`defineConstraints:["!HYBRIDCLR_UNITY"]`）——
   v1.0 无符号时桥编译、Sudoku 进主包；v1.1 构建临时加 `HYBRIDCLR_UNITY` 符号时桥被排除、过滤干净。
3. **本地包内 + 远程覆盖的正确模型 = Addressables Content Update**（同一 GUID 从本地组迁远程组，
   `UpdateCatalogs` 后同 key 自动解析到新 bundle），不是本地远程各放一份。

### 16.2 9-1 安装 + GenerateAll 集成构建链（✅ 2026-08-31，验收证据）

- **安装配方**（Phase9HybridCLRInstall.cs，可重跑）：gitee clone `hybridclr` **分支 `6000.3.x`**
  （⚠️ v8.13.0 tag 装得上但 C++ 与 6000.3.20f1 不兼容）+ `il2cpp_plus` **tag `v6000.3.x-8.14.0`**
  → Move 进 libil2cpp → `new InstallerController().InstallFromLocal()` → `HasInstalledHybridCLR()` 验证。
- **GenerateAll 六步首跑成功**：HotUpdateDlls/Android（含 Box.HotUpdate.Sudoku.dll）、
  AssembliesPostIl2CppStrip（110 个剥离 dll，无热更程序集 ✓）、generated/（MethodBridge/AssemblyManifest/UnityVersion）、
  AOTGenericReferences.cs 审查通过（PatchedAOTAssemblyList = Box.UI/System.Core/UniTask/UnityEngine.CoreModule/mscorlib，泛型面以 UniTask 状态机 + Stack\<GameSession.Move\> 为主）。
- **v1.1 中间态**（免签名 APK `Rovilo-debug-v12-20260831-0048.apk`，69,200,768 B）：`[FilterHotFixAssemblies]` 过滤日志出现；
  libil2cpp.so 含 **38 处 "HybridCLR" 字符串** + LoadMetadataForAOTAssembly/RuntimeApi 符号（Gate1 标准 ~46 处，数量级一致）。
- **v1.0 回归**（`Rovilo-debug-v12-20260831-0101.apk`，68,139,128 B）：原入口出包成功、
  libil2cpp.so **0 处 HybridCLR 符号**、GameplayView 类型在 global-metadata.dat、classes.dex 与旧基线逐字节同大小。
- **体积口径（重要）**：HybridCLR 真实成本 = v1.1 − v1.0（同日同代码）= **+1,061,640 B ≈ +1.01MB**；
  v1.0 相对未安装 HybridCLR 的构建增量为 0（enable=false 走原版 il2cpp）。
  旧 60.1MB 基线（Rovilo.apk，v11 时代）早于 main 玩法动画三连提交（8-30 22:00+），
  新增的 20.5MB bin/Data 内容文件两包都有、与 HybridCLR 无关——**不要用旧基线比体积**。
- **构建入口**（BuildScript.cs，双阶段 CLI）：
  `PrepareV11`（切 Android + NDK r27c EditorPrefs + enable=true + 名单 + HYBRIDCLR_UNITY 符号）→
  `BuildV11`（GenerateAll → 复位 exportAsGoogleAndroidProject=false → Addressables → 构建 → finally 恢复 enable=false + 移除符号）；
  中间态验证用 `BuildV11Apk`（免签名）。AAB 完整构建需 BOX_KEYSTORE_PASS（尚未跑，见 16.6 阻塞项）。
- **提交**：fc8aee3 / 97533e4 / 89d579a / 8eb65b9。

### 16.3 9-2 边界 + 模式条件桥（待执行）

**目标**：`Box.HotUpdate.Core` 成立、Sudoku 引用它；模式桥消除 v1.1 悬垂引用；v1.0 完整回归。

| 文件 | 动作 |
|---|---|
| `GameBox/Assets/HotUpdate/Core/Box.HotUpdate.Core.asmdef`（新） | 热更公共基座，`autoReferenced=false` |
| `GameBox/Assets/HotUpdate/Core/HotUpdateVersion.cs`（新） | `codeVersion` 常量 |
| `GameBox/Assets/HotUpdate/Sudoku/Box.HotUpdate.Sudoku.asmdef` | `autoReferenced: true→false` + references 加 `Box.HotUpdate.Core` |
| `GameBox/Assets/Core/ModuleBridge/Box.ModuleBridge.asmdef`（新） | `autoReferenced=true` + `defineConstraints:["!HYBRIDCLR_UNITY"]` + 引用 Sudoku，空标记类防优化 |
| `GameBox/Assets/Editor/Phase9HybridCLRSetup.cs` | 名单写入 `["Box.HotUpdate.Core", "Box.HotUpdate.Sudoku"]` |
| 测试 asmdef ×2 | references 补 `Box.HotUpdate.Core` |

要点：Core 内容量控制（能不放就不放，边界越小 AOT 泛型面越小）；玩法 15 个源文件零改动；
link.xml 双模式语义共存；grep 复查 Sudoku 无具体 SDK 实现引用（现走 ServiceLocator 已合规）。
验证：① 编译 + CI-1 全绿 ② v1.0 完整回归（出包、无符号、体积不变）③ v1.1 过滤日志 `[FilterHotFixAssemblies] filter assembly:...`（不需真机）。
**风险与备选**：桥方案是全计划最非常规处，若 defineConstraints 组合失效（v1.1 报悬垂引用）→
构建脚本临时翻转 asmdef 文件后还原；30 分钟不通即切换并汇报。

### 16.4 9-3 启动链路接入热更下载（待执行，核心新工作）

**目标**：AOT 侧热更引导组件——无强制网络等待、失败静默降级、D-1 单通道、加载成功刷新 ModuleLoader。

| 文件 | 动作 |
|---|---|
| `GameBox/Assets/Core/HotUpdate/HotUpdateService.cs`（新，Box.Gameplay 内） | 热更引导核心 |
| `GameBox/Assets/Core/HotUpdate/ModuleOverrides.cs`（新） | 远程清单 JSON 模型 |
| `GameBox/Assets/ModuleFramework/ModuleLoader.cs` | 加 `Refresh(IReadOnlyList<ModuleEntry>)` |
| `GameBox/Assets/Gameplay/AppBootstrap.cs` | `Boot()` 第 6-7 步之间插 `HotUpdateService.Begin(ui)`（不 await 不阻塞） |

要点：**全方法体无条件编译 + HybridCLR.RuntimeApi 走反射**（`Type.GetType("HybridCLR.RuntimeApi, HybridCLR.Runtime")`）：
v1.0 主包无此类型 → null → 整链静默跳过；禁止直接引用（v1.0 会被 IL2CPP 裁剪 → MissingMethod 崩溃）。
流程：反射探测 → `Addressables.CheckForCatalogUpdates/UpdateCatalogs`（套 `UniTask.Timeout(5s)`）→
`LoadAssetAsync<TextAsset>` 加载 dll/metadata → 反射 `LoadMetadataForAOTAssembly(bytes, HomologousImageMode.Consistent)` →
`Assembly.Load` → 加载 module_overrides JSON → `ModuleLoader.Refresh` 全量替换（= 最简单回滚）。
任一步失败静默降级用包内版本；Assembly.Load 失败 → `ClearDependencyCacheAsync` 清缓存下轮重试。
热更侧首版控泛型面（多用非泛型 UniTask/`Forget()`）；AOT 泛型缺口用 `[GenericMethodInstantiation]` 补齐。
验证：① EditMode 新用例全绿（下载层抽象 `IHotUpdateContentSource` 注入 mock——Editor 是 Mono 且同名程序集已加载，**不能真 Assembly.Load**）
② 编辑器 Play 冒烟：无 HybridCLR 启动 ≤2.5s、数独可玩（降级路径）③ **v1.1 真机闭环**（唯一真机项）：在线首启下载 → 数独可玩；断网冷启动 → 包内兜底；服务器停机 → 启动不卡。

### 16.5 9-4 Addressables Remote Catalog 远程下发（待执行）

**目标**：远程 catalog 落地、dll/metadata/overrides 远程化、module_overrides 生成、本机部署脚本、content_state.bin 入库。

| 文件 | 动作 |
|---|---|
| `AddressableAssetSettings.asset` | `m_BuildRemoteCatalog: 0→1` + Profile 新变量 `RemoteHostURL`（开发值 `http://127.0.0.1:8000`）+ Remote.BuildPath/LoadPath |
| `AssetGroups/HotUpdate_Local.asset`（新） | dll/metadata/overrides 组（Local + ContentUpdateGroupSchema"可变更"） |
| `Phase9HybridCLRSetup.cs` | `GenerateContent()`：HotUpdateDlls 白名单拷贝（**只拷名单内两个 dll + metadata，勿整目录**）+ overrides 模板 |
| `GameBox/Assets/Editor/Phase9Publish.cs`（新） | Content Update 增量构建封装 |
| `tools/deploy_remote.ps1`（新） | 拷贝 ServerData + overrides 到 `_deploy_remote/`（gitignore）+ python http.server |
| `.gitignore` | 否定规则放行 `addressables_content_state.bin`（**必须入库**，丢失=无法 Content Update）+ 忽略部署目录 |

要点：module_overrides JSON 与 `ModuleEntry` 字段一一对应，从 `ModuleCatalog.asset` 序列化模板；
更新流程 = 改代码 → GenerateAll → GenerateContent → BuildPlayerContent（产 ServerData）→ deploy → 真机验证"远程新 dll 覆盖包内旧 dll"；
Content Update 增量验证放后半（先全量验证同 key 覆盖）。真机访问本机 HTTP：防火墙放行 + 同网段，失败先 `curl` 自测。
验证：① BuildPlayerContent 产出 ServerData/ ② 真机在线更新闭环 ③ 断网冷启动包内可玩（回归）④ content_state.bin 入库。

### 16.6 收尾勾账（待执行）+ 当前阻塞项

**最终验收**：① v1.1 AAB 真机在线启动 → dll 经 Addressables 加载、Assembly.Load 成功、数独可玩；断网冷启动包内可玩
② 首包 ≤60MB 预算：v1.0 对比 Phase 8 基线增量 ≈ 0；v1.1 = v1.0 + hybridclr 运行时（9-1 实测 +1.01MB）+ 包内 dll/metadata（≤1MB）
③ v1.0 模式回归：出包无 HybridCLR 符号、数独可玩、CI-1 全绿 ④ 红线 9：仓库无远程内容、RemoteHostURL 为开发值
⑤ 文档勾账：本 §16 验收打勾 + 17 文档补 v1.1 构建入口。

**当前阻塞项**：v1.1 AAB 完整构建需环境变量 `BOX_KEYSTORE_PASS`（用户注入，agent 不碰密码明文）；
已用 APK 中间态验证符号等价性，AAB 随时可跑：`PrepareV11 → BuildV11`。

**风险排序**：R1 GenerateAll 在大工程失败（9-1 已通过，风险解除）→ R2 桥方案失效（备选已备，9-2 验证）
→ R3 v1.0 回归破坏（最高优先，每步都回归）→ R4 AOT 泛型缺失（9-1 已审查，9-3 热更代码注意控泛型面）
→ R5 真机闭环只能真机验（9-3 需准备设备）。

## 16.7 Phase 9.5 — 新手引导（v1.x 留存优化，FR-16 P0）

> **2026-08-30 拍板**：排本 Phase（下个迭代，热更接入前首包落地）;步骤配置用 ScriptableObject 数据驱动。
> 背景:FR-16 标 P0 但此前未排期(10 文档无引导条目),方案详见下方,开工时直接执行。

| 任务 | 内容 |
|---|---|
| 9.5-1 | TutorialMask 遮罩高亮组件(全屏遮罩+镂空,挂 UILayer 最高层,参考 FxPool overlay 思路) |
| 9.5-2 | 引导步骤 SO 资产(OnboardingStepAsset:高亮目标/文案 key/等待事件/完成动作,6 步) |
| 9.5-3 | TutorialController 步骤编排(事件驱动状态机,挂 GameplayView,可选钩子零开销) |
| 9.5-4 | OnboardingService 状态管理(box.onboarding 存档分区,已完/可跳过持久化) |
| 9.5-5 | 引导局整合(固定种子 Beginner 谜题、屏蔽全部广告、可跳过;结算弹每日挑战入口) |
| 9.5-6 | 埋点 tutorial_step(step_index, skipped)(04 文档 §埋点契约) + EditMode/PlayMode 测试 |

**验收**：新装用户首局 ≤6 步引导、≤90 秒,全程可跳过、零广告;引导完成状态断电重启不重复;埋点可见。

---

## 17. Phase 10 — 热更内容管线

| 任务 | 内容 |
|---|---|
| 10-1 | 新玩法包构建/打包/上传流程 |
| 10-2 | 版本管理 + 回滚机制 |
| 10-3 | 灰度发布能力 |

**验收**：新玩法 ≤8MB 增量下发，客户端可热更运行。

## 18. Phase 11 — 运营期

| 任务 | 内容 |
|---|---|
| 11-1 | 数据埋点 + 监控（崩溃/留存/收入） |
| 11-2 | 更新节奏（bugfix 热更 / 内容包 / 大版本） |
| 11-3 | 回归 v1.0 商业闭环数据优化 |

**验收**：D1≥35%、D7≥12% 目标跟踪（沿用 06 指标，数值以 04 文档为准）。

> **2026-08 调整**：11-1 数据埋点已提前至封闭测试前落地（Firebase Analytics + Crashlytics 13.15.0 已接入，
> 08 文档 §6），封闭测试期间即可在 Firebase 后台看到新增/活跃用户与崩溃；Phase 11 剩余监控指标跟踪与收入分析。

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
