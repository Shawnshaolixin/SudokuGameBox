# 17 号文档：AAB 发布构建指南

> 依据：10 号文档 Phase 1-3 / 15 号文档 §4.3 / 14 号文档（Jenkins CI）
> 状态：2026-08-26 排坑后固化。可上架的 AAB 构建全流程 + 验证 + 常见坑。
> 关联技能：Claude Code 可用 `/build-android-aab` 直接执行（`.claude/skills/build-android-aab/SKILL.md`）。

## 1. 背景：为什么需要这份指南

2026-08-26 首次上传 Play Console 被拒："您上传的是在调试模式下签名的 APK 或 Android App Bundle"。
排查链：

1. **直接证据**：`jarsigner -verify -certs GameBox.aab` → 全部条目 `CN=Android Debug` → 产物是 debug 签名；
2. **根因 1（keystore 路径）**：`BuildScript.ApplyReleaseSigningIfAvailable()` 用 `Path.GetFullPath("Build/keystore/...")` 相对 CWD 找 keystore。CLI 启动 CWD=工程目录（GameBox/），而 keystore 实际在仓库根 `Build/keystore`（与 GameBox 平级）→ 找不到 → **静默回退 debug 签名**（仅日志一行 warning）；
3. **根因 2（JDK）**：batchmode 下 JDK 偏好为空时报 `UnityException: JDK not found`。GUI 编辑器会兜底内置 OpenJDK，batchmode 不会；且构建检查不读 EditorPrefs 的 `JdkPath`，只认 `JdkUseEmbedded` 标志（注册表/偏好）。

修复（已合入 `GameBox/Assets/Editor/BuildScript.cs`）：

- keystore 路径改为 `Application.dataPath` 推导仓库根（CWD 无关）；
- AAB 签名不可用（缺 keystore / 缺密码）→ `LogError` + exit 1 **硬失败**，不再产出 debug 签名包；
- `EnsureJdkConfigured()`：JDK 偏好为空时写入内置 OpenJDK 路径（配合 `JdkUseEmbedded=1` 生效）。

## 2. 环境事实（本机，2026-08-24~26 验证）

| 项 | 值 | 说明 |
|---|---|---|
| Unity | `C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe` | 6000.3.20f1 |
| 工程 | `d:\Projects\AI\SudokuGameBox\GameBox` | 仓库根含 Build/、docs/ 等 |
| 上传 keystore | 仓库根 `Build/keystore/upload.keystore` | alias `sudoku`；目录 gitignore，双备份纪律见 15 号文档 §4.3 |
| keystore 密码 | 见 `Build/keystore/README.md` | 当前为占位值 `SudokuGameBox_Upload_2026`，**上架前更换** |
| upload.cer | `Build/keystore/upload.cer` | Play App Signing 注册时上传公钥 |
| NDK | `D:/Projects/AI/AndroidNDK/android-ndk-r27c` | 环境变量 `ANDROID_NDK_ROOT` 注入（14 号文档 FAQ 的 headless workaround） |
| JDK | 内置 OpenJDK（AndroidPlayer 下） | 需 `JdkUseEmbedded=1`：注册表 `HKCU\Software\Unity Technologies\Unity Editor 5.x\JdkUseEmbedded_h2297287597` = 1（GUI 等价：Preferences → External Tools → JDK → JDK installed with Unity） |
| Gradle | `D:\Tools\gradle-9.1.0` | GUI 偏好已存，无需处理 |
| 产物 | `GameBox/Build/Android/GameBox.aab`（约 57 MB） | 本地测试 APK 同目录 |
| 耗时 | 10~20 分钟 | IL2CPP 全量，建议后台执行 |

## 3. 构建步骤（CLI）

### 3.1 前置检查

- `GameBox/Temp/UnityLockfile` 不存在（Unity 编辑器未开着本工程，否则 lock 冲突）；
- 本机 keystore 就位（`Build/keystore/upload.keystore` 存在）。

### 3.2 注入环境变量并启动

```bash
export BOX_KEYSTORE_PASS="SudokuGameBox_Upload_2026"   # 与 Build/keystore/README.md 一致
export BOX_KEY_PASS="SudokuGameBox_Upload_2026"
export ANDROID_NDK_ROOT="D:/Projects/AI/AndroidNDK/android-ndk-r27c"
"C:/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Unity.exe" \
  -batchmode -quit -projectPath "d:/Projects/AI/SudokuGameBox/GameBox" \
  -executeMethod BuildScript.BuildAndroidAab \
  -logFile "d:/Projects/AI/SudokuGameBox/Build/Logs/release-aab.log"
```

要点：

- **密码只能经环境变量注入 Unity 进程**。编辑器菜单构建（Box/Build/Android AAB）时，Unity 必须从设了变量的终端启动（桌面快捷方式不继承环境变量）；
- 密码不写命令行参数、不写脚本文件、不进 git（BuildScript 构建后自动清除签名密码字段）；
- 本地测试用 `BuildScript.BuildAndroidApk`（APK 无签名要求）。

### 3.3 验证（必须，构建成功 ≠ 签名正确）

```bash
# ① 日志确认签名分支
grep "已应用上传签名" "d:/Projects/AI/SudokuGameBox/Build/Logs/release-aab.log"
# 期望：已应用上传签名 upload.keystore(alias: sudoku)

# ② 产物证书
"C:/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/jarsigner.exe" \
  -verify -verbose -certs "d:/Projects/AI/SudokuGameBox/GameBox/Build/Android/GameBox.aab" 2>&1 | grep "CN="
```

**通过标准**：日志含签名确认行；jarsigner 全部条目 `CN=SudokuGameBox`，**禁止出现 `CN=Android Debug`**。

### 3.4 上传

Play Console → 创建版本 → 上传 AAB。首次上传触发 Play App Signing 注册：选"使用您自己的上传密钥"，上传 `Build/keystore/upload.cer`。

## 4. 常见问题

| 症状 | 原因 | 处理 |
|---|---|---|
| Play Console 拒收"调试模式签名" | 产物 debug 签名 | 重打（§3）；现在 BuildScript 对无签名 AAB 硬失败，不会静默产出 |
| 日志 `未找到上传 keystore` | 旧版路径相对 CWD | 已修复（§1）；勿改回相对路径 |
| `JDK not found`（工具路径里 `JDK: ''`） | batchmode 不读 JdkPath 偏好、不回退内置 JDK | `JdkUseEmbedded=1`（§2）；新机器/CI 首次构建必须设置 |
| `error CS...` | 脚本编译错误 | 构建即编译验证，修代码重跑 |
| 构建中断 `Scripts have compiler errors` | 同上 | 同上 |

## 5. Jenkins CI-3 注意事项（未完成项，2026-08-26）

Jenkins CI-3（14 号文档 6.5-3）复用 `BuildScript.BuildAndroidApkAndAab`，目前**无法产出签名包**，上 CI 前需补齐：

1. **凭据注入**：Jenkins 凭据（Credentials）注入 `BOX_KEYSTORE_PASS` / `BOX_KEY_PASS`，密码不写入任何脚本（15 号文档 §4.3 步骤 4）；
2. **keystore 就位**：`Build/keystore` 被 gitignore，Jenkins 工作区不会自动检出 → 需将 keystore 放到 Jenkins 本机受控目录；
3. **JDK**：SYSTEM 账户无 GUI 偏好 → CI 构建前需设置 `JdkUseEmbedded=1`（或等价配置），否则撞 `JDK not found`；
4. **NDK**：Jenkinsfile environment 块已注入 `ANDROID_NDK_ROOT`，保持。

## 6. 变更记录

- 2026-08-26：本指南建立；BuildScript.cs 三项修复（keystore 路径 / 签名硬失败 / JDK 兜底）；`JdkUseEmbedded=1` 落注册表；首次成功产出签名 AAB。
