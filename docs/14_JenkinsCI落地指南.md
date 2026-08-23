# 14 号文档：Jenkins CI 落地指南（Phase 6.5）

> 版本:v1.0 | 状态:Draft(2026-08-23) | 对应:10 文档 §13 Phase 6.5(v1.2) | 落地产物:仓库根 `Jenkinsfile` + `tools/asset_check.py` | 前置:已拍板 Jenkins 本机部署 + SCM 轮询(2026-08-23)

## 背景与目的

10 文档 §13 已拍板 **Phase 6.5(CI 管线)**：核心价值 = 学习企业级 CI/自动化测试实践 + 面试素材，v1.0 发布兜底仅为附带收益。

本文档是 Phase 6.5 的「怎么装、怎么配、怎么讲」落地指南，按步骤执行即可跑通三个 job：

| Job | 对应任务 | 职责 | 频率 |
|---|---|---|---|
| CI-1 | 6.5-1 | Unity 无头编译 + EditMode 全量回归(可选 PlayMode) | SCM 轮询自动 |
| CI-2 | 6.5-2 | 资产校验(纯 Python,零 Unity 依赖) | SCM 轮询自动 |
| CI-3 | 6.5-3 | AAB 构建(复用 `BuildScript.cs`),产物归档 | 手动勾选参数 |

**为什么是 Jenkins 而不是 GitHub Actions**(面试也按此讲):
- 本地已激活 Unity → BatchMode 直接跑,**免 .ulf / UNITY_LICENSE** 云端激活流程
- 无云分钟额度、国内网络零依赖(Webhook/制品下载都不需要外网)
- **Jenkins 是国内游戏公司 CI 标配**，本机部署 + SCM 轮询正是国内公司真实环境
- 代价:PC 需保持开机、构建占 CPU(可错开日常时段)——已在 10 文档成本纪律中记账

**诚实的限制(已写入 10 文档 §13⚠️)**:本机无公网 IP,GitHub 无法回调本地 Jenkins → 不能做 PR 状态检查;"失败阻止合并"落地为"**失败阻止交付/产物归档**";CI 结果在 Jenkins 页面看,不上 GitHub。面试讲清楚这个理由(无公网 IP → SCM 轮询),反而体现对 CI 原理的理解深度。

---

## 步骤 1：前置检查

| # | 检查项 | 确认方法 | 备注 |
|---|---|---|---|
| 1 | Unity 6000.3.20f1 已激活 | 手动跑过一次 CLI(13 号文档 §6 命令) | Jenkins 直接复用本机 License,无需 .ulf |
| 2 | 仓库已初始化 Git | `git -C D:\Projects\AI\SudokuGameBox status` | 轮询指向**本地路径**,无需远程 |
| 3 | JDK 17 或 21 | `java -version`(见步骤 2) | Jenkins 服务本身需要 |
| 4 | Android 构建模块 | `Build/Android/` 目录存在过 AAB 产物 | 证明 BuildTarget Android 可用 |
| 5 | 磁盘空间 ≥ 10GB | Jenkins 工作区会 clone 仓库副本 | 构建产物 Archive 在 Jenkins 下 |

> 任何一项不符合,先解决再继续,避免装完 Jenkins 再回头补环境。

---

## 步骤 2：安装 JDK(21 或 25 LTS,本机实测 21)

Jenkins 2.568 LTS(本仓库安装版本)要求 **JDK 21 或 25**——**JDK 17 不满足**:服务能启动但 30 秒内退出(Java 进程打印 "older than the minimum required version (Java 21)"),导致 MSI 安装时服务启动失败(1920)→ 1603 回滚(2026-08-23 实测踩坑)。Windows 上推荐 Adoptium Temurin(开源免费),国内下载用清华镜像最快(官方源极慢,300s 只下 24MB):

```powershell
# ① 先看本机是否已有 JDK
java -version

# ② 没有则下载安装 Temurin 21(选 .msi 格式)
# 下载页:https://adoptium.net/temurin/releases/?version=21&os=windows&arch=x64&package=jdk
# 安装时默认勾选「Set JAVA_HOME」「Add to PATH」
```

安装完重开终端验证:

```powershell
java -version   # 应显示 21.x 或 17.x
echo $env:JAVA_HOME   # 应显示 JDK 安装目录
```

> 为什么先装 JDK：Jenkins 本体是 Java 程序，Windows 服务由 java.exe 拉起；**不要**在 Jenkins 里再装一份 JDK（冗余）。

---

## 步骤 3：安装 Jenkins(Windows 服务)

下载官方 LTS 版 Windows 安装包:https://www.jenkins.io/zh/download/ → 「Windows」→ jenkins.msi

安装向导要点(每步为什么):

| 向导项 | 建议 | 原因 |
|---|---|---|
| Service Logon | **保持默认 Local System** | **本机已实测:License 是机器级激活**(`C:\ProgramData\Unity\Unity_lic.ulf` 存在且有效),LocalSystem 可直接读取,无需改服务账户;若未来变成用户级激活再改(见 FAQ) |
| Port | 默认 8080 | 本机访问,无需 HTTPS/反向代理(面试可提「内网部署不暴露公网」) |
| JNLP Port | **Disable** | 单节点本机部署,不需要入站 agent(这是国内小团队典型形态) |
| 安装位置 | 默认 `C:\Program Files\Jenkins` | 服务数据（JENKINS_HOME）实际在 `%LocalAppData%\Jenkins\.jenkins`（LocalSystem 即 `C:\Windows\system32\config\systemprofile\AppData\Local\Jenkins\.jenkins`，2026-08-23 实测） |

安装完成后 Jenkins 自动注册为 Windows 服务并启动。

---

## 步骤 4：初始化 Jenkins

```powershell
# ① 打开浏览器
http://localhost:8080

# ② 解锁密码(安装后一次性)
# JENKINS_HOME 实际位置（2026-08-23 实测），需管理员权限
Get-Content "C:\Windows\system32\config\systemprofile\AppData\Local\Jenkins\.jenkins\secrets\initialAdminPassword"

# ③ 插件安装:选「Install suggested plugins」(含 Git/Pipeline/JUnit/PowerShell/Timestamper 等)
#    或「Select plugins to install」只装最小集:
#    必装:Git Plugin / Pipeline / JUnit / PowerShell / Timestamper
#    可选:Localization: Chinese (Simplified)(中文界面)/ Warnings Next Generation(日志告警解析)

# ④ 创建管理员账号(用户名/密码自己定,本机使用)
```

> 为什么这些插件：Git(检出仓库)、Pipeline(跑 Jenkinsfile)、JUnit(解析 Unity 测试 XML 出趋势图)、PowerShell(执行 .ps1 脚本)、Timestamper(构建日志显示每行耗时,排查慢构建)。

---

## 步骤 5：全局配置(只做必要的)

- **工具 → Git** ：Path to Git executable 留空自动检测(装了 Git 默认在 PATH)
- **无需任何凭据**：SCM 指向本地路径 `D:\Projects\AI\SudokuGameBox`，无账号/密码/Token
  ——这是本方案最大的简化点(对比 GitHub Actions 要配 `UNITY_LICENSE` Secret),面试可强调

---

## 步骤 6：创建 Pipeline Job + SCM 轮询配置

```text
Dashboard → New Item → 名称「SudokuGameBox-CI」→ 类型选「Pipeline」→ OK

① Pipeline 页签:
   Definition:   Pipeline script from SCM
   SCM:          Git
   Repository URL: D:\Projects\AI\SudokuGameBox        ← 本地路径,不用 https://github.com
   Branches to build: */main
   Script Path:  Jenkinsfile                            ← 仓库根已提供(本仓库已创建)

② Build Triggers 页签:
   勾选「Poll SCM(轮询 SCM)」,日程填:
   H/5 * * * *      ← 每 5 分钟检查一次本地仓库是否有新提交,有则自动跑

③ 保存后首次运行:
   点「Build with Parameters」→ 不勾 BUILD_AAB/RUN_PLAY_MODE → 构建

④ 首次构建大概率报「Checkout ... references a local directory ... ALLOW_LOCAL_CHECKOUT」:
   这是 Git 插件的安全策略(默认禁止检出本地目录,防恶意 job 读本地文件)。
   解法:给 Jenkins JVM 加系统属性 `hudson.plugins.git.GitSCM.ALLOW_LOCAL_CHECKOUT=true`
   (2026-08-23 实测流程:管理员编辑 `C:\Program Files\Jenkins\jenkins.xml`,
   `<arguments>` 开头加 `-Dhudson.plugins.git.GitSCM.ALLOW_LOCAL_CHECKOUT=true `,重启服务;
   或用 Jenkins 脚本控制台以 SYSTEM 身份改此文件后 `jenkins.exe restart`)
```

**为什么是 SCM 轮询而不是 Webhook**(10 文档 §13 拍板,面试必讲):
- Webhook = GitHub 主动回调 Jenkins,需要 Jenkins 有**公网 IP/域名**;本机无公网 IP,GitHub 根本打不进来
- SCM 轮询 = Jenkins 反向去问 Git 仓库「有没有新提交」,只访问本地路径,零外网依赖
- 代价是延迟(最多 5 分钟),对单人开发完全可接受——这正是国内无公网环境的标准做法

---

## 步骤 7：验收验证(按顺序做)

1. **自动触发**:在仓库改一行代码并 `git commit`(中文 message)→ 5 分钟内 Jenkins 自动出现新构建
2. **测试阶段通过**:CI-1 绿,构建页出现「测试结果趋势」链接(EditMode XML 被 JUnit 解析)
3. **故意失败**:临时把 `Assets/Art/` 下某个 PNG 改名去掉 `_bg` 等白名单后缀(或放一张 4096px 图)→ 提交 → CI-2 标红,构建历史显示失败 → 改回来再提交 → 恢复绿
   ——验证"**失败阻止交付**"真实生效
4. **构建 AAB**:再次 Build with Parameters,勾选 `BUILD_AAB` → CI-3 产 AAB,构建页「Artifacts」可下载 `GameBox.aab`
5. 确认构建历史页:绿 = 可交付,红 = 找原因(见 FAQ)

### 2026-08-24 实测记录(全部通过)

| 验收项 | 构建号 | 实测结果 |
|---|---|---|
| 自动触发 | #21 | 提交 `cb836f7`(meta 补交)后 SCM 轮询自动触发,无需手动 |
| 测试阶段通过 | #20/#21 | 双绿,Unity exit 0,144 用例;测试趋势页数据落库 |
| 失败拦截(非故意) | #19 | 提交 `cf16686` 编译错误(`NamedBuildTarget` 缺 using)2 分钟内被 CI-1 抓出标红 |
| 故意失败 CI-2 | #23 | `play_btn.png` 改名 `play.png` → CI-1 绿、CI-2 抓出"文件命名不符合白名单"、CI-3 被跳过 → FAILURE;恢复后提交 `a42422f` |
| CI-3 双产物 | — | CI-3 一次会话产出 **APK(本地装机测试)+ AAB(上架)**,Artifacts 均可下载(2026-08-24 增补:BuildScript.BuildAndroidApkAndAab) |

---

## FAQ(按踩坑概率排序)

| 现象 | 原因 | 解决(也是 13 号文档/Phase 6 已验证的经验) |
|---|---|---|
| 构建里 Unity 报 License 错误 | 服务账号读不到 License | 本机 License 在 `C:\ProgramData\Unity\Unity_lic.ulf`(机器级)时 LocalSystem 即可;若为用户级(在 `%LOCALAPPDATA%\Unity`)则把服务登录身份改为激活用户(需密码) |
| Jenkins 服务装完即死(7034 循环)/ MSI 1603 回滚 | **Java 版本不满足**:Jenkins 2.568 要求 Java 21+,本机只有 17 → Java 进程启动即退(err.log: "older than the minimum required version") | 装 Temurin 21 并把机器级 JAVA_HOME 切到 21(2026-08-23 实测);JDK17 方案已废 |
| MSI 安装报 1618「另一个安装已在进行」 | 上次失败安装残留挂死的 msiexec 进程,锁住 Windows Installer 互斥 | `taskkill /F /PID <残留进程>` 后重跑安装 |
| CI-1 报 "Lockfile exists" | Unity 上次异常退出残留 `Temp/UnityLockfile` | Jenkinsfile 已内置删除,无需手动;手动跑时同样先删 |
| PowerShell 立即返回/拿不到退出码 | Unity.exe 是 GUI 程序,`&` 调用不等待 | Jenkinsfile 已用 `Start-Process -PassThru -Wait`(13 号文档定型做法) |
| CI-1 显示绿但没有测试趋势 | JUnit 插件未装 / XML 路径不对 | 装 JUnit 插件;junit 路径相对 workspace,保持 `TestResults/ci-editmode-junit.xml` |
| JUnit 插件报 "None of the test reports contained any result" | **Unity 的 NUnit3 XML 是嵌套 test-suite 结构,标准 JUnit 解析器处理不了(JENKINS-6545)**——文件存在、有 test-case 也解析 0 结果 | 2026-08-23 实测:新增 `tools/nunit3_to_junit.py` 把所有 test-case 拍平为单层 testsuite 的 JUnit 扁平 XML,CI-1/CI-1b 转换后再 junit 解析(仅标准库,零依赖) |
| `python` / `py` 报 CommandNotFoundException | Jenkins 服务(LocalSystem)的 PATH **不含用户级 Python** | Jenkinsfile `environment` 已写死绝对路径 `C:\Users\<用户>\AppData\Local\Programs\Python\Python312\python.exe`,不要靠 PATH 探测 |
| 用 curl REST 触发构建报 400 "Nothing is submitted" | 带参数的 Pipeline job 要用 `buildWithParameters` 端点;`build` 端点收空 body 会 400 | `POST /job/<name>/buildWithParameters`(需 crumb + session cookie + Basic 认证) |
| CI-3 报 "Android NDK not found or invalid" | ① SYSTEM 账户无 GUI Preferences → 自动探测到 Unity 内置 NDK r27,Unity 6000.3 对其校验失败(已知问题)② `-androidNdkRoot` 命令行参数在 6000 **已废弃,实测无效** | Jenkinsfile `environment` 注入 `ANDROID_NDK_HOME` 环境变量指向项目终版 r27c(官方探测顺序:Preferences → 环境变量 → 内置;Start-Process 子进程继承) |
| REST 带参数触发后构建参数丢失 | quiet period 内多次触发(手动 + SCM 轮询)会合并,合并后可能以默认参数构建 | 触发后立即查 `lastBuild/api/json?tree=actions[parameters]` 确认;必要时重触发 |
| 轮询不触发 | ① 日程语法错 ② Git 插件缺 ③ 服务账号无权限读仓库路径 | H/5 格式;确认插件;给仓库目录加读权限 |
| 构建报 "references a local directory ... ALLOW_LOCAL_CHECKOUT" | Git 插件默认禁止本地目录 checkout(安全策略,4.7+ 引入) | jenkins.xml 的 `<arguments>` 加 `-Dhudson.plugins.git.GitSCM.ALLOW_LOCAL_CHECKOUT=true` 后重启服务;属性在类加载时读取,运行时 System.setProperty 无效 |
| 中文乱码 | Unity 日志 stdout 编码问题 | 一律用 `-logFile` 写文件(项目已定型),不在 stdout 解析 |
| 8080 端口被占 | 其他程序占用 | 安装时换端口(如 8090) |
| 构建太慢/开机耗资源 | 本机跑 Unity CI 的正常代价 | 错开时段(如轮询 H/5 但 CI-3 手动)、只在 main 提交触发(10 文档成本纪律) |

---

## 面试故事(2 分钟讲法)

> **为什么上 CI**:每提交自动回归 + 资产命名契约进 CI,是休闲游戏岗位认可的工程实践。
> **为什么 Jenkins 而非 GitHub Actions**:本机已激活 Unity 免云端 License 流程、无分钟额度、国内网络零依赖;且国内游戏公司 CI **标配 Jenkins**,本机部署 + SCM 轮询就是国内无公网环境的真实形态。
> **为什么 SCM 轮询不用 Webhook**:本机无公网 IP,GitHub 无法回调——轮询本地仓库路径,零外网依赖,5 分钟延迟对单人开发无感。能讲清这个取舍 = 真懂 CI 原理。
> **三个 job 分工**:CI-1 测试(Unity 无头 + NUnit XML 被 JUnit 解析出趋势)、CI-2 资产校验(**纯 Python 零 Unity 依赖,唯一能廉平常驻的 job**,读 PNG 头审计尺寸 + 命名白名单,秒级)、CI-3 构建(AAB 手动触发,产物归档)。**成本纪律**:CI 又贵又折腾,只做这三个,不扩矩阵/截图对比。
> **一句话收尾**:CI 结果在 Jenkins 页面,失败阻止交付(不是阻止合并——本地 Jenkins 无公网 IP 做不了 PR check,这是诚实的范围取舍)。

---

## 附录 A:Jenkinsfile 说明(仓库根已创建)

| 元素 | 说明 |
|---|---|
| `parameters.BUILD_AAB` | CI-3 开关,默认 false(构建手动触发为主) |
| `parameters.RUN_PLAY_MODE` | CI-1b 开关,默认 false(项目回归以 EditMode 为主,13 号文档 §6) |
| `environment.UNITY` | 本机 Unity.exe 路径,改 Unity 版本只动这一处 |
| `Start-Process -PassThru -Wait` | Windows 下等待 GUI 程序退出的标准做法 |
| `junit ...` | 解析 `TestResults/*-junit.xml`(Unity NUnit3 XML 需先经 `tools/nunit3_to_junit.py` 拍平,JENKINS-6545) |
| `archiveArtifacts` | 归档 **APK + AAB** 双产物(本机方案替代 GitHub Actions artifact),构建记录可下载 |

## 附录 B:tools/asset_check.py 说明(仓库已创建)

- 规则 ①:文件名含白名单后缀 `_btn/_panel/_icon/_particle/_bg`(10 文档 6.5-2)
- 规则 ②:纹理尺寸 ≤ 2048px(**读 PNG 头 IHDR**,O(1),不加载整图)
- **存量豁免 + 增量约束**:`tools/legacy_assets.txt` 中列出的存量文件(开发期临时命名,如 `play111.png`)跳过检查;规则只约束**今后新增**资产,清理存量后从清单移除
- 用法:`python tools/asset_check.py` 或 `py -3 tools/asset_check.py`;`--max 1024` 可收紧上限
- 违反任一规则 → exit 1 → Jenkins 标红;全部通过 exit 0
- 定位:`GameBox/Assets/Art` 目录自动找(脚本位于 tools/,上级是仓库根)