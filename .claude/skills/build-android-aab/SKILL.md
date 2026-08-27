---
name: build-android-aab
description: 构建可上架的 Android AAB(上传签名 + 签名验证)。当用户要求"打 AAB/发布包/上架包/打 release 包/Google Play 上传包"时使用。完整指南见 docs/17_AAB发布构建指南.md。
---

# 构建发布用 Android AAB

一次性跑通的上架构建流程（2026-08-26 排坑后固化）。**核心原则：AAB 必须带上传签名，产出一个 debug 签名包 = 构建失败。**

## 何时使用

- 用户要求构建可上架 Google Play 的 AAB
- 用户说"打 release 包""打上架包""构建 AAB"

## 前置条件（本机事实，2026-08-26 验证）

| 项 | 值 |
|---|---|
| Unity | `C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe` |
| 工程 | `d:\Projects\AI\SudokuGameBox\GameBox` |
| 上传 keystore | 仓库根 `Build/keystore/upload.keystore`（alias `sudoku`；密码见 `Build/keystore/README.md`，当前为占位值 `SudokuGameBox_Upload_2026`） |
| NDK | r27c，需环境变量 `ANDROID_NDK_ROOT=D:/Projects/AI/AndroidNDK/android-ndk-r27c` |
| JDK | 内置 OpenJDK，本机已设 `JdkUseEmbedded=1`（注册表 `HKCU\Software\Unity Technologies\Unity Editor 5.x`，GUI 等价于 Preferences → External Tools 选 "JDK installed with Unity"） |
| Gradle | `D:\Tools\gradle-9.1.0`（GUI 偏好已存，无需处理） |
| 产物 | `GameBox/Build/Android/Rovilo.aab`（约 57 MB） |
| 耗时 | 10~20 分钟（IL2CPP 全量），建议后台运行 |

## 构建步骤

### 1. 确认版本号已 +1（必须，否则 Play Console 拒收）

Google Play 要求 `versionCode` 严格递增，**每次构建必须比 Console 上已存在的最新版本号大 1**。

1. 询问用户 Play Console 当前最大的 versionCode（或用户自行确认）；
2. 检查 `GameBox/ProjectSettings/ProjectSettings.asset` 的 `AndroidBundleVersionCode`；
3. 若 `< Console 最新 + 1`，先改该字段（`AndroidBundleVersionCode: N`），**确认后再构建**；
4. 已经启动的构建若版本号不对，必须停止重建（构建启动时会快照版本号，中途改不生效）。

### 2. 检查 Unity 编辑器未占用工程

编辑器开着本工程会导致 lock 冲突。检查 `GameBox/Temp/UnityLockfile` 是否存在；存在则请用户关闭编辑器再继续。

### 3. 注入环境变量并启动 CLI 构建（关键！）

```bash
export BOX_KEYSTORE_PASS="SudokuGameBox_Upload_2026"   # 必须与 Build/keystore/README.md 一致
export BOX_KEY_PASS="SudokuGameBox_Upload_2026"
export ANDROID_NDK_ROOT="D:/Projects/AI/AndroidNDK/android-ndk-r27c"
"C:/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Unity.exe" \
  -batchmode -quit -projectPath "d:/Projects/AI/SudokuGameBox/GameBox" \
  -executeMethod BuildScript.BuildAndroidAab \
  -logFile "d:/Projects/AI/SudokuGameBox/Build/Logs/release-aab.log"
```

- `-executeMethod BuildScript.BuildAndroidAab`：走 AAB 分支，自动应用上传签名（[BuildScript.cs](GameBox/Assets/Editor/BuildScript.cs) 已内置三项修复：keystore 路径由 dataPath 推导、签名不可用硬失败、JDK 偏好兜底）
- 密码经环境变量注入：**不要**把密码写进命令行/脚本/git
- 若要本地测试 APK：`BuildScript.BuildAndroidApk`（APK 无需签名）

### 4. 验证（必须做，构建成功 ≠ 签名正确）

```bash
# ① 日志确认签名分支生效（而非 debug 回退）
grep "已应用上传签名" "d:/Projects/AI/SudokuGameBox/Build/Logs/release-aab.log"

# ② jarsigner 验产物证书
"C:/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/jarsigner.exe" \
  -verify -verbose -certs "d:/Projects/AI/SudokuGameBox/GameBox/Build/Android/Rovilo.aab" 2>&1 | grep "CN="
```

**通过标准**：日志有 `已应用上传签名 upload.keystore(alias: sudoku)`；jarsigner 全部条目为 `CN=SudokuGameBox`，**不得出现 `CN=Android Debug`**。

### 5. 交付

验证通过后告知用户上传 `GameBox/Build/Android/Rovilo.aab`。首次上传 Play Console 会要求 Play App Signing 注册，用 `Build/keystore/upload.cer`。

## 常见坑（都是 2026-08-26 实战踩过的）

| 症状 | 原因 | 处理 |
|---|---|---|
| Play Console 报"调试模式签名" | 产物是 debug 签名 | 按上述流程重打；现在 BuildScript 对无签名 AAB 会直接失败，不会静默产出 |
| 日志 `未找到上传 keystore` | keystore 路径解析依赖 CWD | BuildScript 已修复为 dataPath 推导，勿改回相对路径 |
| `UnityException: JDK not found`（`JDK: ''`） | batchmode 不读 EditorPrefs 的 JdkPath，也不像 GUI 那样回退内置 JDK | 设 `JdkUseEmbedded=1`（见前置条件表）；新机器/CI 首次构建前必须设置 |
| 构建失败 `error CS...` | 脚本编译错误 | 修代码重跑（构建本身即编译验证） |
| Jenkins CI-3 产物 debug 签名 | CI 未注入 `BOX_KEYSTORE_PASS` 凭据、SYSTEM 账户无 GUI 偏好 | 见 docs/17 的 CI 注意事项，需 Jenkins 凭据注入 + 注册表/JDK 配置 |

## 参考

- 完整指南（含背景、CI 注意事项、Play Console 流程）：`docs/17_AAB发布构建指南.md`
- 密码/keystore 管理：`Build/keystore/README.md`、`docs/15_Phase7商业化执行清单.md` §4.3
- 构建脚本：`GameBox/Assets/Editor/BuildScript.cs`（菜单入口 Box/Build/Android AAB）
