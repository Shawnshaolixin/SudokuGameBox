# 10 — Phase 执行计划（权威执行手册）

> 版本:v1.3 | 状态:Approved(2026-08-21 用户拍板;2026-08-23 增补 Phase 6.5 CI 管线 + D-17;2026-08-23 CI 工具拍板 Jenkins+SCM 轮询;2026-08-31 Phase 9 详细执行计划落地 9-1 完成 + 9-2~9-4 计划;2026-09-04 增补 §16.8 Phase 9.6 水排序 M1 完成 + §17 10-2 版本化部署骨架落地) | 说明:**本文是项目执行的唯一权威计划**。
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

> 状态：**9-1 ~ 9-4 ✅ 完成（2026-08-31，dev/phase9 分支；9-4 ①②④ 编辑器侧验收，②③ 真机闭环待设备）**
> 分支：`dev/phase9`（基于 main de5d2dc）；红线 6（v1.0 构建链不破坏）与红线 9（不 push）全程适用

| 任务 | 内容 | 状态 |
|---|---|---|
| 9-1 | 将 Gate 1 结论落地正式工程（安装 hybridclr_unity v8.14.1 + GenerateAll 集成构建链） | ✅ 2026-08-31 |
| 9-2 | HotUpdate.Core + 热更程序集边界搭建（玩法 dll 禁止互引，只引 HotUpdate.Core + AOT 接口） | ✅ 2026-08-31 |
| 9-3 | 启动链路接入热更下载（无强制网络等待，D-10 细化） | ✅ 2026-08-31 |
| 9-4 | Addressables Remote Catalog 远程下发（D-1） | ✅ 2026-08-31（真机闭环待设备） |

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

### 16.3 9-2 边界 + 模式条件桥（✅ 2026-08-31，验收证据见下）

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

**验收证据（✅ 2026-08-31）**：
- **提交**：87b259c（边界+桥 14 文件）+ 769f780（GenerateAll 名单落盘 HybridCLRSettings.asset）。
- **验证①**：CLI 编译零错误（`Exiting batchmode successfully now!`，`Box.HotUpdate.Core.dll`/`Box.ModuleBridge.dll` 均编译）；
  EditMode 全量 175/175（9-3 验收时回归，覆盖 9-2 改动）。
- **验证③**：`PrepareV11 → BuildV11Apk`（产物 `Rovilo-debug-v12-20260831-2051.apk`，69,202,156 B），
  过滤日志同时出现 `filter assembly:Box.HotUpdate.Core` 与 `filter assembly:Box.HotUpdate.Sudoku` ✓；
  libil2cpp.so **38 处 "HybridCLR" 字符串** + LoadMetadataForAOTAssembly/RuntimeApi 符号（与 9-1 数量级一致）；
  **桥方案生效**：v1.1（HYBRIDCLR_UNITY）下 `Box.ModuleBridge` 被 defineConstraints 排除编译，全链路零悬垂引用错误；
  BuildV11 finally 正确恢复 enable=false + 移除符号。
- **验证②（v1.0 完整回归）**：未单独跑，计划随 9-3 完成后合并回归（见 16.4 待办）。

### 16.4 9-3 启动链路接入热更下载（✅ ①② 2026-08-31；③ 真机待验）

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

**验收证据（✅ ① ② 2026-08-31）**：
- **提交**：3b7577f（11 文件 +558 行：HotUpdateService/IHotUpdateContentSource/ModuleOverrides + ModuleLoader.Refresh + AppBootstrap 接入 + 6 用例）。
- **设计要点落实**：全反射（`Type.GetType("HybridCLR.RuntimeApi, HybridCLR.Runtime")` 探测，HomologousImageMode 经方法参数类型 `Enum.Parse`，v1.0 零编译期依赖）；
  地址约定 `HotUpdate/Dll/{asm}`、`HotUpdate/Metadata/{asm}`、`HotUpdate/module_overrides`（9-4 配 HotUpdate_Local 组时对应）；
  任一步失败静默降级包内版本；Assembly.Load 失败 → `ClearDependencyCacheAsync` 下轮重试；`Begin()` fire-and-forget 不阻塞启动。
- **验证①**：EditMode 全量 **175/175 全绿**（6 新用例：无运行时跳过 / catalog 失败降级 / 成功装载+清单刷新 / 装载失败清缓存 / metadata 缺失容错 / overrides 无效保底）。
- **验证②**：PlayMode **4/4 全绿**，新增 `Startup_Under_Timeout_With_HotUpdate_Degrade`（启动 ≤2.5s 断言通过）；
  覆盖降级路径：Editor 下 HybridCLR.Runtime 存在 → 热更链路走 Addressables 本地查找 → 无远程资源 → 静默降级包内版本，数独全链路可玩。
- **验证③（真机闭环）**：待 9-4 远程资源就绪 + Android 设备执行（在线首启 / 断网冷启动 / 服务器停机三态）。
- **已知问题**：Android 符号 `SENTIS_ANALYTICS_ENABLED` **三次**被 Unity 批处理进程保存漂移丢失（2026-08-31，已手工恢复）；
  规律：均发生在 `-batchmode` 进程（2 次 `-runTests` PlayMode、1 次编译检查）后，Android defines 末项被截断、Standalone 完好；
  仓库内无 PlayMode 期写入方（仅 3 个 Editor 脚本 -executeMethod 才写）→ 疑为 Unity 6000 批处理符号序列化 bug；
  防御：**每次批处理进程后 `git diff` 复核该文件**，根因排查列为待办（不阻塞 Phase 9）。

### 16.5 9-4 Addressables Remote Catalog 远程下发（✅ ① ④ 2026-08-31；②③ 真机闭环 2026-09-02 ✅ 修复轮闭环：踩坑 9 条全部定位修复，用户真机全流程验收"功能正常"）

**目标**：远程 catalog 落地、dll/metadata/overrides 远程化、module_overrides 生成、本机部署脚本、content_state.bin 入库。

| 文件 | 动作 |
|---|---|
| `AddressableAssetSettings.asset` | ✅ `m_BuildRemoteCatalog: 0→1` + Profile 新变量 `RemoteHostURL`（开发值 `http://127.0.0.1:8000`）+ Remote.BuildPath=`ServerData/[BuildTarget]` / Remote.LoadPath=`{RemoteHostURL}/[BuildTarget]` |
| `AssetGroups/HotUpdate_Local.asset`（新） | ✅ dll/metadata/overrides 组（Bundled + ContentUpdateGroupSchema"可变更"），8 entries（2 dll + 5 metadata + overrides） |
| `Phase9HybridCLRSetup.cs` | ✅ `EnsureRemoteSetup()`（catalog 开关/Profile 变量/组创建，幂等）+ `GenerateContent()`：白名单拷贝（**只拷 2 dll + 5 metadata + overrides 模板，勿整目录**）→ `Assets/RemoteContent/`（gitignore） |
| `GameBox/Assets/Editor/Phase9Publish.cs`（新） | ✅ `BuildAll`（CleanPlayerContent + BuildPlayerContent）+ `ContentUpdateBuild`（ContentUpdateScript.BuildContentUpdate 增量） |
| `tools/deploy_remote.ps1`（新） | ✅ 拷贝 `GameBox/ServerData/{Target}` 到 `_deploy_remote/`（gitignore）+ python http.server |
| `.gitignore` | ✅ 否定规则放行 `addressables_content_state.bin`（**必须入库**，丢失=无法 Content Update；产物在 `AddressableAssetsData/Android/`）+ 忽略 `ServerData/`、`RemoteContent/`、`_deploy_remote/` |

要点：module_overrides JSON 与 `ModuleEntry` 字段一一对应，从 `ModuleCatalog.asset` 序列化模板（`{"version":"1.1.0","entries":[{"id":"sudoku",...}]}` 已验证）；
更新流程 = 改代码 → GenerateAll → GenerateContent → BuildPlayerContent（产 ServerData）→ deploy → 真机验证"远程新 dll 覆盖包内旧 dll"；
Content Update 增量验证放后半（先全量验证同 key 覆盖）。真机访问本机 HTTP：防火墙放行 + 同网段，失败先 `curl` 自测。

**验收证据（✅ ① ④ 2026-08-31）**：
- **提交**：Phase9HybridCLRSetup（EnsureRemoteSetup/GenerateContent）+ Phase9Publish + deploy_remote.ps1 + .gitignore + 组/schema 资产 + HotUpdateServiceTests 语义修正（metadata 按 AOT 程序集逐个装载）。
- **验证①（BuildPlayerContent 产出 ServerData/）**：全量构建产出 `ServerData/Android/`：`catalog_1.0.bin/.hash` + `hotupdate_local_assets_all_....bundle`（1.8MB）；
  临时解包脚本 `AssetBundle.LoadFromFile` 校验 **8/8 资源就位**：2 热更 dll（Core 4.6KB / Sudoku 49KB）+ 5 AOT metadata（Box.UI 39KB / System.Core 401KB / UniTask 396KB / UnityEngine.CoreModule 910KB / mscorlib 2.2MB）+ module_overrides（167 字符含 sudoku 条目）。
- **验证④（content_state.bin 入库）**：产物在 `Assets/AddressableAssetsData/Android/addressables_content_state.bin`，gitignore 否定规则放行；
  增量 `ContentUpdateBuild` 基于它构建成功（catalog 重建、bundle 哈希未变不重写）。
- **部署链路**：deploy_remote.ps1 拷贝 + `python -m http.server` 起服，`curl http://127.0.0.1:8765/ServerData/Android/{catalog,bundle}` 均 200。
- **回归**：编译零错误；EditMode **175/175 全绿**（7 热更用例含新 metadata 语义：元数据缺失→降级不碰 dll）；PlayMode **4/4 全绿**。
- **踩坑记录（根因已修，代码幂等防复发）**：① Phase 6 建 settings 时 `m_RemoteCatalogBuildPath/LoadPath` 空引用，开远程 catalog 后 CreateRemoteCatalog 直接失败 → EnsureRemoteSetup 显式 `SetVariableByName`；
  ② `profile.SetValue(profileId, 变量名, 值)` 第二参数是**变量名不是 GUID**，传 id 静默失败 → 已改传名字；
  ③ 组 schema 路径残留 Local 变量 → bundle 误入本地构建路径 → EnsureHotUpdateGroup 改为幂等校正并自检。
- **验证②③（真机，2026-09-01 进行中）**：在线首启 / 断网冷启动 / 服务器停机三态；远程新 dll 覆盖包内旧 dll。
  真机执行已暴露 **4 个纯真机问题**（编辑器 Mono 侧全部测不出），根因与修复见下节「真机闭环踩坑记录」。
  当前进展：坑①②③④已修复（link.xml / InternalIdTransformFunc / 双层 cleartext 放行+Development / manifest 补全），
  坑③ 最终根因（DevelopmentOnly 需 development build）已修复,APK 第 6 次重建中。
  2026-09-02 修复轮：坑⑤⑥⑦⑧ 已修复并验收（网络排查 + Development 宏对齐,APK 第 12 次构建在线首启热更链全通）；
  随后暴露 **坑⑨ = 热更视图挂载架构缺陷**（非 AB 场景/prefab 序列化热更组件 → 真机静默丢失,见下表）,
  HotViewBinder 桥修复后 APK 重建（v12-2203）→ 重装冷启动热更链复通（5 metadata + 2 dll + overrides v1.1.0,~0.6s）,
  真机日志实锤：`missing script (Box.HotUpdate.Sudoku.GameplayView)` 警告后紧跟 `[HotViewBinder] 运行时附加热更视图` → **修复生效**；
  用户真机全流程检查 2026-09-02 **"功能正常"**（主页/难度弹窗/棋盘渲染/按钮响应/返回,9/9 条踩坑全部定位修复闭环）。
  完整复盘（现象→排查→根因证据链→方案→复发坑→验收）见 `docs/20_9-4热更真机验收复盘.md`。
  **2026-09-03 断网修复轮：坑⑩ 空降级缺陷发现→修复→双态真机验收闭环**（详见下表 ⑩）——
  内置兜底组 HotUpdate_Builtin(dll/metadata 随包副本)+ 装载层兜底(任何远程装载失败 → 固化切内置),
  v12-2156 重装后 **飞行模式冷启动数独可玩** + **直连不可达(有网无代理)同样兜底可玩**,用户人工确认"可玩"。

**真机闭环踩坑记录（2026-09-01，均已在代码修复并附注释）**：

| # | 症状（真机日志） | 根因 | 修复 | 为何编辑器测不出 |
|---|---|---|---|---|
| ① | `[HotUpdate] 主包无 HybridCLR 运行时(v1.0 语义),热更链路跳过` | IL2CPP 链接裁剪掉 `HybridCLR.RuntimeApi`(主包无静态引用,link.xml 未保留) → `Type.GetType` 返回 null → 整链静默跳过 | `Assets/link.xml` 加 `<assembly fullname="HybridCLR.Runtime" preserve="all"/>` | 编辑器 Mono 不裁剪,反射能找到 |
| ② | `Unable to open archive file: RemoteHostURL/Android/...bundle` | 远程 bundle 路径的 `{RemoteHostURL}` 是**运行时变量**,构建值不烘焙进 catalog,设备端无值 → 占位符原样输出;且 Addressables 2.8 **已移除 1.x 的 `SetProfileVariable`**(编译即报 CS0117) | `Addressables.InternalIdTransformFunc` 钩子把 `RemoteHostURL` 替换为实际服务器地址(开发期局域网 IP,生产换 CDN);构造函数即装 + 每次 catalog 更新前兜底 | 编辑器 profile 有变量值,本地可求值 |
| ③ | bundle 秒级失败,但 `nc`/ping 全通;服务器日志显示请求**根本没到达** | **双层拦截**:Unity 6000 引擎层 WebRequest 默认拒绝非 localhost 明文 HTTP(报 `Insecure connection not allowed`,`usesCleartextTraffic` 管不到引擎层);Android 9+ 系统层另有限制。Addressables 还把远程 catalog 请求失败静默吞成"无更新"。**最坑**:`insecureHttpOption=DevelopmentOnly` 只对 **development build** 生效——原 APK 构建选项是 `BuildOptions.None`,选项写了也不生效,直到构建期日志核对才发现 | ① `PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly`(BuildScript.PrepareV11)+ manifest `usesCleartextTraffic="true"` ② **APK 构建加 `BuildOptions.Development`**、AAB 保持 `None`(BuildScript.BuildAndroidInternalCore,注释说明) | 编辑器 PlayMode 用 `127.0.0.1`(localhost 豁免),桌面无此策略;选项生效依赖构建选项,编辑器态验证不到 |
| ④ | 安装后 `No activities found to launch`(monkey 无法启动) | 自定义主 manifest 是**替换而非补全**,极简 manifest 丢掉了 `UnityPlayerGameActivity` 声明 | 基于官方模板 `PlaybackEngines/AndroidPlayer/Apk/UnityManifest.xml` 补全 activity 声明(属性取自正常构建旧 APK:singleTask/fullUser/configChanges 等) | 编辑器不打包,构建阶段不校验 launcher |
| ⑤ | ——(诊断工具) | 定位③时:请求未达服务器 = 本地拦截;错误详情在 `UnityWebRequest result : ConnectionError : ...`(Addressables 外层只报 "Unable to load asset bundle",**实时流式抓 logcat** 才能看到内层) | 无(记录排查路径:logcat 流式抓取 + python http.server 访问日志 + `toybox nc` 验证) | —— |
| ⑥ | 服务器日志 404(bundle 下载失败),但文件确实在部署目录 | 目录结构与 profile 的 URL 不对齐:Remote.LoadPath=`{RemoteHostURL}/[BuildTarget]` → URL `/Android/...`;旧部署从 `_deploy_remote` 根起服务(URL 变成 `/ServerData/Android/...`),另一错误做法是把 serve root 设成 `_deploy_remote/Android`(URL 又多找一层)。**http.server 的 root 必须是 `_deploy_remote`,文件放在 `_deploy_remote/Android/`** | `tools/deploy_remote.ps1` 拷贝目标改为 `_deploy_remote/$Target` + `--directory $deploy`;附注释说明两种错误结构 | 编辑器 profile 变量本地可求值,目录错位测不出 |
| ⑦ | 旧 http.server 未停时重跑 deploy 脚本,`Remove-Item`/`Copy-Item` 静默失败(产物缺失,curl 404) | 旧 python 进程占用 `_deploy_remote` 目录(Windows 文件锁),脚本 `$ErrorActionPreference=Stop` 未中止(疑似句柄共享) | 重跑前 `netstat -ano \| grep :8000` 确认无 LISTEN,或 taskkill 旧 python;脚本注释已加提示 | —— |
| ⑧ | `[HotUpdate] 装载 AOT 元数据 Box.UI 失败: ... TargetInvocationException`(完整异常链此前被 catch 吞掉,2026-09-02 真机+模拟器) | **Development 宏不一致**:HybridCLR 的 `StripAOTDlls`/`CompileDll`/`MethodBridge` 读的是 `EditorUserBuildSettings.development`(batchmode 默认 **false**),而 BuildScript 正式构建传的是 `BuildPlayerOptions` 级 `BuildOptions.Development`(不写回编辑器开关)→ strip 产物(AOT metadata 来源)与包内 AOT 程序集 **DEVELOPMENT_BUILD 宏不同 → IL 不同** → Consistent 模式逐程序集校验失败。重跑 GenerateContent 也无效:metadata 永远是"无宏"版本 | `BuildV11` 在 `GenerateAll` 前显式 `EditorUserBuildSettings.development = true`(与正式 Development 构建对齐,finally 恢复原值);另 `LoadMetadata` catch 改打印完整异常链(含反射 Invoke 内层) | 编辑器 Mono 不涉 IL2CPP 宏;旧 bundle 时间戳与 metadata 拷贝时序在真机才暴露 |
| ⑨ | 主页(AOT MainMenuView)渲染/点击正常;进数独对局页:**静态 UI(标题/工具条/数字盘/返回)全部正常显示,棋盘(代码生成)缺失,所有按钮无响应**,日志零报错(2026-09-02 真机) | **热更视图组件序列化丢失**:GameplayView/DifficultySelectView(Box.HotUpdate.Sudoku)作为 MonoBehaviour **直接挂在 prefab 根**(Gameplay.unity 通过 PrefabInstance 引用);v1.1 打包 FilterHotFixAssemblies 把热更程序集从主包剥离 → IL2CPP 资源反序列化按**构建期静态 MonoScript 表**解析,该类不在表内 → 组件静默丢弃(Awake/OnCreate 永不执行:棋盘 = OnCreate 里代码生成,按钮 onClick = OnCreate 里绑定)。HybridCLR 官方:prefab/场景挂热更脚本仅 **AB 打包资源**可还原,**Build Settings 非 AB 场景必丢**;官方唯一"无限制"路径 = Assembly.Load 后代码 `AddComponent`。APK 解剖佐证:ScriptingAssemblies.json 含热更 dll(官方 patch 生效)但组件仍丢 → 证明关卡是脚本表而非 dll 注册 | 新增 **HotViewBinder**(AOT,Box.UI)+ 迁移脚本 Phase9HotViewBinderSetup:两视图 prefab 根挂 Binder 并写入 viewTypeFullName;运行期根上无 UIView(序列化组件已被剥)时按类型名 AppDomain 解析 + `AddComponent` 挂回 → 同步触发 Awake,生命周期与序列化直挂一致;类型未载(热更 dll 未装载)时逐帧重试 10s 兜底;v1.0/编辑器组件正常序列化 → GetComponent 命中 → 桥空转幂等,双模式共用同一 prefab。**架构纪律:热更视图组件永不直接进 prefab/场景,一律经桥运行时附加** | 编辑器全量编译(无 filter),组件序列化还原正常;真机才丢,且丢失是静默的(无任何 error 日志) |
| ⑩ | **断网(飞行模式)冷启动进不去游戏**:连点入口报 `[ModuleFramework] 入口类型不可用: Box.HotUpdate.Sudoku.SudokuModule`;热更链静默放弃,dll 从未装载(2026-09-03 用户报告"没有网络的情况下没法进入游戏") | **v1.1 剥离热更程序集后的"空降级"**:包内无 dll/metadata,"降级包内版本"是空操作。首轮修复把切换点放 catalog 阶段,但 **catalog 阶段成败判定不可靠,两种路径都绕过**:①断网时 Addressables `CheckForCatalogUpdates` 把远程 hash 下载失败**静默吞成"无更新"**(CheckCatalogsOperation 仅全部失败才报错)→ 返回成功 → 源不切内置;②网络部分可达(直连 web.app 被 SSL 拦)时 catalog 检查/更新成功、**bundle 实际下载失败发生在 LoadAsset 阶段** → catalog 阶段无从感知。两态实锤日志:`加载 HotUpdate/Metadata/Box.UI 失败: Dependency Exception`(无远程条目)/ `RemoteProviderException: Unable to complete SSL connection`(有条目下载失败),地址前缀始终是远程而非 Builtin —— UseBuiltinContent 从未置位 | ①新增 **HotUpdate_Builtin 本地组**(GenerateContent 同批双写 dll/metadata 副本到 `Assets/BuiltinHotUpdate`,BuildV11 内插 GenerateContent 保证与包内 AOT 同批;组 BuildPath/LoadPath 强制 Local 变量)②**切换点后置到装载层**(核心):`LoadWithFallbackAsync` 每次装载先试首选源,**任何失败(任何原因)固化切内置再试一次**,此后本进程全走本地(零网络)——杜绝远程/内置混载,一致性由"内置=同批"天然保证;catalog 阶段预切换保留为双保险 ③编排层(HotUpdateService)catalog 失败/超时不再 return,继续走装载链 | 编辑器 Mono 下网络层与 Addressables 行为差异大;Addressables 吞 hash 失败/部分可达等状态只有真机才出现;catalog 阶段"成功"却无任何日志,不看 LoadAsset 失败详情无法定位 |

**真机网络排查备忘(下次直接照做)**：① 电脑 `ipconfig` 与手机 `adb shell ip addr show wlan0` 对比网段,不同网段互相不可达 → 手机切到同 WiFi;
② Windows 默认拦 ICMP,ping 不通≠不可达,直接验证 TCP:`adb shell "printf 'GET /... HTTP/1.0\r\n\r\n' \| toybox nc -w 5 <电脑IP> 8000"`(Android 自带 toybox nc);
③ 手机连 WiFi 后注意切换时序(断旧连新需几秒,立刻启动会踩空);④ AP 隔离验证:电脑 ping 手机 0% 丢包即二层可达;
⑤ 每次 batchmode 构建后 `git diff ProjectSettings.asset` 查 SENTIS 符号漂移(已 3 次复发,批处理进程截断 Android defines 末尾条目);
⑥ 构建日志的 `summary.totalSize` 会虚报(2026-09-02 第 8 次构建报 1189.8 MB,磁盘实际 79.7 MB,ZIP 校验完整)——以磁盘文件大小为准,别被日志吓到重建。

### 16.6 收尾勾账（待执行）+ 当前阻塞项

**最终验收**：① v1.1 AAB 真机在线启动 → dll 经 Addressables 加载、Assembly.Load 成功、数独可玩；断网冷启动包内可玩 —— **断网态已 ✅ 2026-09-03 APK 中间态闭环**（坑⑩ 修复后 v12-2156 飞行模式冷启动 + 直连不可达双态可玩,用户人工确认;AAB 最终形态待阻塞解除后按 17 文档 §6 跑 `PrepareV11 → BuildV11` 复验一次）
② 首包 ≤60MB 预算：v1.0 对比 Phase 8 基线增量 ≈ 0；v1.1 = v1.0 + hybridclr 运行时（9-1 实测 +1.01MB）+ 包内 dll/metadata —— **坑⑩ 修复体积账：内置兜底 bundle 实测增量 ≈ +1.96MB**（同构 APK 对比 20260902-2331→20260903-2156:hotupdate_builtin bundle 1,883,413 Stored 免二次压缩 + libil2cpp +80KB + metadata +346B,逐项吻合;AAB 上界同理,预算仍 ≤60MB 口径内）
③ v1.0 模式回归：出包无 HybridCLR 符号、数独可玩、CI-1 全绿 ④ 红线 9：仓库无远程内容、RemoteHostURL 为开发值
⑤ 文档勾账：本 §16 验收打勾 + 17 文档补 v1.1 构建入口 —— ✅ 2026-09-03：17 文档已补 §6 v1.1 发布流程（含 BOX_REMOTE_URL 注入）。
⑥ **坑⑩ 修复纪律（防复发）**：改 AOT 侧热更链代码 → 必重跑 GenerateAll→GenerateContent→BuildPlayerContent 同批（内置副本 = 与包内 AOT 同批,擅自只传远程内容会造成内置/远程不一致）;内置兜底内容变更一律走新包,禁 Content Update 更新 HotUpdate_Builtin 组（无 ContentUpdate schema）。

**当前阻塞项**：v1.1 AAB 完整构建需环境变量 `BOX_KEYSTORE_PASS`（用户注入，agent 不碰密码明文）+ `BOX_REMOTE_URL=production`（发布通道注入，机制 2026-09-03 已落地，见 17 文档 §6）；
已用 APK 中间态验证符号等价性，AAB 随时可跑：`PrepareV11 → BuildV11`。

**风险排序**：R1 GenerateAll 在大工程失败（9-1 已通过，风险解除）→ R2 桥方案失效（✅ 9-2 验证通过：v1.1 无悬垂引用，风险解除）
→ R3 v1.0 回归破坏（最高优先，每步都回归；9-3 后待合并回归）→ R4 AOT 泛型缺失（9-1 已审查，9-3 热更代码注意控泛型面）
→ R5 真机闭环只能真机验（9-4 需准备设备）。

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

## 16.8 Phase 9.6 — 水排序第二玩法（M1 完成 2026-09-04）

> **2026-09-04 拍板**：水排序优先开发，**Phase 9.5 数独新手引导顺延**（理由：第二玩法打通"新玩法整链"对
> 学习型商业项目的面试/商业价值高于引导留存优化；期间引导通用件按可复用设计，9.5 随后复用同一组件）。
> PRD 见 19 文档（v0.4：关卡全预生成 / 难度代理指标 / 每日题预生成 / 提示任意解，求解器 Spike 实证支撑）。

| 里程碑 | 状态 | 范围 |
|---|---|---|
| M1 核心闭环 | ✅ 2026-09-04（提交 c04bc39~c5abb23） | Spike 正式化 WaterSort.Core → 玩法程序集/模块骨架 → prefab+Game_WaterSort 组 → 清单接入+金币闭环（提示 20/空瓶 40/首通 20~100 递增）→ 首批 100 关题库落库 |
| M2 题库规模化+每日挑战 | ✅ 2026-09-05（提交 273ea5b~1fb7c50） | 难度校准采样定版 → 常规题扩产 + ≥2 年每日题预生成 → Streak/按日期取关/兜底 |
| M3 商业化+引导+热更验证 | 执行中（3.1/3.2/3.3 已验收，3.4 构建验证+发布形态决策待续） | 激励视频三点位 → 插屏频控配置化（AOT 一次改造）→ 引导 3 步通用件 → v1.1 热更构建验证 + 发布形态决策 |

**M1 验收证据**：CLI 编译零 error；EditMode 全量 217/217（水排序相关 32 用例全绿 = 清单 2 + 会话状态机 7 + Core 23）；
100 关 JSON 逐关 SolveAny 可解 + 同种子可复现（Easy 10 关 6~9 步 / Medium 45 关 15~34 / Hard 45 关 27~51，分布落档位内）；
ModuleCatalog 含 watersort 条目 → More Games 自动出现，大厅零改动。

**M2/M3.1~3.3 验收证据（详见 19 文档 §10 逐条记录）**：M2 = 难度代理区间校准定版（附录 A 留档）+ 800 天每日题库逐日验收全绿（15.5 关/min）+ EditMode 236/236；
M3.1 激励视频三点位 + M3.2 插屏频控全局化 → 241/241；M3.3 引导通用件（Core.Onboarding 四件 + 无头测试接缝）+ 水排序 3 步引导 → 253/253。

---

## 17. Phase 10 — 热更内容管线

| 任务 | 内容 |
|---|---|
| 10-1 | 新玩法包构建/打包/上传流程 |
| 10-2 | 版本管理 + 回滚机制 |
| 10-3 | 灰度发布能力 |

**验收**：新玩法 ≤8MB 增量下发，客户端可热更运行。

**10-2 版本化部署骨架已落地（2026-09-04 前置，提交 7bb16e2 + 4158ac2）**：版本目录 + `index.json` 指针
（发布/回滚都只改指针，秒级生效，布局契约见 20 文档 §11）；bundle 内容寻址共享、旧客户端兼容 catalog 双写、
部署前自动归档 `_history/`；回滚 = `deploy_firebase.ps1 -RollbackTo <版本>`（须与设备 APK 同代）。10-3 灰度在
index.json 加权重字段或接 Remote Config 即可，布局已预留。

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
