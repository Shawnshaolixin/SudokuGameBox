---
name: hotupdate-build
description: v1.1(HybridCLR)热更 APK 构建与远程内容部署全链(四段构建 + Firebase Hosting 双通道发布 + 真机验证守门)。当用户要求"打 v1.1 包/热更构建/打热更 APK/部署远程内容/发布 Firebase/更新热更内容"时使用。坑记录权威:docs/10_Phase执行计划.md §16.5(坑①~⑩,含断网空降级修复)与 docs/20_9-4热更真机验收复盘.md(§10 坑⑩ 复盘)。
---

# v1.1 热更包构建 + 远程内容部署

2026-09-02 全链验证通过后固化的执行流。**核心纪律:顺序不可乱、每段守门、改 AOT 白名单代码后远程 metadata 必须重建(坑⑧)。**

## 何时使用

- 用户要求构建 v1.1(热更)APK/AAB 或验证热更内容
- 用户改了代码(热更 dll 或 AOT 白名单程序集)后要"重打热更包/更新远程内容"

## 本机事实(2026-09-02 验证)

| 项 | 值 |
|---|---|
| Unity | `C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe`(批处理,禁 `-runTests` 加 `-quit`) |
| 工程 | `d:\Projects\AI\SudokuGameBox\GameBox`,ADB 在 Unity SDK 下 `.../AndroidPlayer/SDK/platform-tools/adb.exe` |
| APK 产物 | `GameBox/Build/Android/Rovilo-debug-v12-*.apk`(免签名中间态,Development;正式上架走 build-android-aab skill) |
| 包名/入口 | `com.lixingames.rovilo` / `com.unity3d.player.UnityPlayerGameActivity`(查入口用 aapt dump badging,勿猜) |
| 远程 URL(客户端) | 常量 `AddressablesHotUpdateSource.RemoteServerUrl`(`GameBox/Assets/Gameplay/HotUpdate/IHotUpdateContentSource.cs`):`#if BOX_REMOTE_PRODUCTION` 双值 —— 仓库常态=`https://sudokugamebox.web.app/staging`(红线 9),发布注入 `export BOX_REMOTE_URL=production` → PrepareV11 自动加符号切 production(17 文档 §6,收尾自愈) |
| Firebase | firebase-tools 已装;配置在 `firebase-hosting/`(firebase.json/.firebaserc);本机直连 web.app 被墙,curl 自测须 `-x http://127.0.0.1:7897`(真机不受影响,实测可直连) |
| 退出码噪音 | Unity 批处理 exit=2/日志尾 `##utp:{...MemoryLeaks...}` 均为噪音;以 `Exiting batchmode successfully now!` 与 `error CS`=0 为准 |

## 标准流程(四段构建 → 部署 → 真机)

### 1. 判断是否需要四段全跑

| 改动类型 | 需要 |
|---|---|
| 改热更 dll(Box.HotUpdate.*) | 全链(四段 + 部署) |
| 改 AOT 白名单程序集(Box.UI/Box.Gameplay 等主包代码) | 全链 —— **GenerateContent 必跑**(strip 产物变 → 远程+内置 metadata 必须同步,坑⑧⑩ 复发点) |
| 只改远程内容配置/overrides | 仅 GenerateContent → BuildAll → 部署 |

> **2026-09-03 起(BuildV11 内插 GenerateContent,坑⑩ 修复)**:阶段 ② 内部已自动执行 GenerateContent 双写
> (RemoteContent 远程组 + BuiltinHotUpdate 内置兜底组,紧跟 GenerateAll 保证与包内 AOT 同批)。
> **改 AOT/热更代码后出包 = 内置副本自动同批刷新,无需单独跑 ③**;③ 仅用于"不改代码、只刷新远程素材"的轻量轮。

### 2. 四段构建(串行,每段单独 -executeMethod,建议后台跑)

```bash
U="/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Unity.exe"; P="d:/Projects/AI/SudokuGameBox/GameBox"
# ① 阶段 A:enable=true + HYBRIDCLR_UNITY + NDK + insecureHttpOption(每次 BuildV11 收尾会恢复 v1.0,故每轮都要先跑)
$U -batchmode -quit -projectPath "$P" -executeMethod BuildScript.PrepareV11
# ② 阶段 B:GenerateAll(development 对齐)→ Addressables BuildPlayerContent → Development APK(出 .apk 产物)
$U -batchmode -quit -projectPath "$P" -executeMethod BuildScript.BuildV11Apk
# ③ 远程内容素材:拷贝白名单 strip 产物(dll/metadata)→ Assets/RemoteContent(必须 ② 之后跑:原料来自②的 GenerateAll)
$U -batchmode -quit -projectPath "$P" -executeMethod Phase9HybridCLRSetup.GenerateContent
# ④ 权威远程产物:CleanPlayerContent + BuildPlayerContent → GameBox/ServerData/Android/(catalog+bundle)
$U -batchmode -quit -projectPath "$P" -executeMethod Phase9Publish.BuildAll
```

**每段守门**(勿信 exit code):日志无 `error CS` + 尾部 `Exiting batchmode successfully now!`。
**构建后必查**:`git diff GameBox/ProjectSettings/ProjectSettings.asset` —— SENTIS 符号漂移(Unity 6000 批处理 bug,已 3 次复发,丢了手工恢复)。

### 3. 部署(Firebase Hosting 双通道)

```powershell
powershell -ExecutionPolicy Bypass -File tools/deploy_firebase.ps1 -Channel staging    # 内容先上 staging
curl -sI -x http://127.0.0.1:7897 https://sudokugamebox.web.app/staging/Android/catalog_1.0.hash  # 200 + no-cache
```

- 通道目录:Firebase 站点根 = 服务器根,契约目录必须是 `public/<通道>/Android/`(catalog 内 id 烘焙为 `/Android/`);勿嵌套两层 Android(2026-09-02 踩过)。
- 验收通过后提升:`-Channel production`(同法 curl 验证 production)。
- 脚本拷贝时**目标目录先预建则源须带通配符**(`(Join-Path $serverData "*")`),否则目录整体嵌套。
- 离网回退:局域网 http.server(`tools/deploy_remote.ps1`,常驻前台)+ 客户端常量改局域网 IP + 手机需 WLAN 同段别名 IP(netsh,光猫隔离);改回 http 后设备须卸载重装(UnityWebRequest 缓存)。
- 本机直连 web.app 被重置(GFW/ISP 对边缘 IP),PC 自测一律走代理;真机实测可直连。

### 4. 真机验证(守门)

```bash
ADB=".../platform-tools/adb.exe"
"$ADB" install -r GameBox/Build/Android/<最新.apk>
"$ADB" logcat -c && "$ADB" shell am start -n com.lixingames.rovilo/com.unity3d.player.UnityPlayerGameActivity
sleep 12 && "$ADB" logcat -d | grep -E "已装载|程序集已装载|模块清单已按远程|HotViewBinder|降级"
```

通过标志(在线,约 4s):`5× AOT 元数据已装载(Box.UI/System.Core/UniTask/UnityEngine.CoreModule/mscorlib)` + `2× 程序集已装载(Box.HotUpdate.Core/Sudoku)` + `模块清单已按远程 overrides 刷新 v1.1.0`。
通过标志(断网/直连不可达,2026-09-03 坑⑩ 修复后):远程装载失败日志(RemoteProviderException / Dependency)后跟 `固化切内置兜底源`(或 catalog 预切换日志)→ 同一套 `5× metadata + 2× dll 已装载`(走 BuiltinHotUpdate 本地组,零网络)→ 数独可玩。
**坑⑩ 教训:别用"catalog 检查成功/失败"判远程可用** —— Addressables 把 hash 下载失败吞成"无更新",catalog 成功但 bundle 下载失败也常见;装载层(LoadWithFallbackAsync)任何失败自动固化切内置才是最终防线。内置兜底缺内容 = 新包没重打(GenerateContent 在 BuildV11 内部自动跑,勿手删 BuiltinHotUpdate)。
进入对局页应见:`missing script(Box.HotUpdate.Sudoku.GameplayView)` 警告后紧跟 `[HotViewBinder] 运行时附加热更视图`(桥修复正常;只见 missing 不见 binder = 桥缺失或类型未解析)。

## 坑速查(细节见 10 文档 §16.5 表,共 10 条 + 复盘)

| 坑 | 症状 | 一句话对策 |
|---|---|---|
| ① link.xml | `主包无 HybridCLR 运行时(v1.0 语义)` | link.xml 保留 HybridCLR.Runtime |
| ② 远程 URL | `Unable to open archive file: RemoteHostURL/...` | InternalIdTransformFunc 改写前缀,勿依赖 SetProfileVariable |
| ③ cleartext | bundle 秒败、请求未达服务器 | insecureHttpOption=DevelopmentOnly + **Development 构建**(选项只对 dev build 生效) |
| ④ manifest | `No activities found to launch` | 自定义 manifest 按官方模板补全 activity |
| ⑥ 目录错位 | 服务器 404 但文件在 | serve/站点根必须是部署根,URL `/Android/` 首段命中 |
| ⑧ metadata | `装载 AOT 元数据 X 失败(metadata type not match)` | 改 AOT 代码后必须 GenerateContent+BuildAll 重产远程 metadata |
| ⑨ 组件丢失 | 棋盘缺失/按钮死/零日志 | 热更视图组件不进 prefab/场景,经 HotViewBinder AOT 桥运行时附加(架构纪律) |
| ⑩ 断网空降级 | 断网冷启动进不去:`入口类型不可用` | 内置兜底组 HotUpdate_Builtin(随包)+ 装载层失败自动固化切内置(LoadWithFallbackAsync);**切换点绝不只在 catalog 阶段**(Addressables 吞失败/下载失败在 LoadAsset 期) |
| 设备缓存 | 改内容后行为不变、服务器无请求 | UnityWebRequest 磁盘缓存,卸载重装才是彻底清场 |

## 红线(提交前核对)

- ServerData/`firebase-hosting/public/*/Android/` 内容不入库(gitignore 已配);编辑器 profile 变量 RemoteHostURL 保持开发值;RemoteServerUrl 仓库保持 staging 分支(#if 双值,生产 URL 只在发布注入的 BOX_REMOTE_PRODUCTION 分支)。
- 内置兜底:生成物 `Assets/BuiltinHotUpdate/`(含 .meta)不入库(gitignore 已配);**组资产 `AssetGroups/HotUpdate_Builtin.asset` + Schema 资产必须入库**(与 HotUpdate_Local 同套路,丢组 = 内置打包失效 = 断网裸奔);content_state.bin 变化随包入库。
- 发布 AAB:PrepareV11 前 `export BOX_REMOTE_URL=production`(与 BOX_KEYSTORE_PASS 同 env 范式,17 文档 §6);不设 = staging(dev 包),BuildV11 收尾自动移除符号,无残留。
- 代码注释必须中文;commit 中文 `type(模块): 描述`;只提交本次相关文件(水排序线文件不混入)。
- 正式上架 AAB 不走本 skill(走 build-android-aab:签名 + BOX_KEYSTORE_PASS 注入 + jarsigner 验证 + BOX_REMOTE_URL 注入)。
