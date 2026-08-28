// Jenkinsfile —— SudokuGameBox 本机 CI 流水线（Phase 6.5）
// 依据：10 文档 §13(v1.2) / 14 号文档；仓库规则 AGENTS.md（中文注释）
// 环境事实：
//   - Unity 6000.3.20f1 已在本机激活 → BatchMode 直接调用，免 .ulf（10 文档 §13 拍板）
//   - 触发方式 = SCM 轮询（本机无公网 IP，GitHub Webhook 不可用，10 文档 §13 拍板）
//   - 三个阶段 = CI-1 测试 / CI-2 资产校验 / CI-3 构建 AAB（手动参数开关为主）
//   - 自定义工作区 D:/JenkinsWS（默认工作区在 SYSTEM profile 下，Jenkins 把路径写为小写
//     system32 而磁盘是 System32 → Unity 大小写敏感比较失败导致路径翻倍；迁出系统目录根治，
//     2026-08-23 实测）。故 skipDefaultCheckout + Checkout 阶段在 ws() 块内显式检出
// 注意：需要 Jenkins 插件 Git / Pipeline / JUnit / PowerShell（14 号文档步骤 4）

pipeline {
    agent any

    options {
        // 默认 checkout 挪到自定义 ws 内显式执行（见 Checkout 阶段）
        skipDefaultCheckout()
    }

    parameters {
        // CI-3 构建 AAB 默认关（成本纪律：构建手动触发为主，见 10 文档 §13）
        booleanParam(name: 'BUILD_AAB', defaultValue: false,
            description: '是否构建 AAB（6.5-3，手动勾选后运行）')
        // PlayMode 默认关（项目回归以 EditMode 为主（13 号文档 §6），本机成本纪律）
        booleanParam(name: 'RUN_PLAY_MODE', defaultValue: false,
            description: '是否追加 PlayMode 测试（默认关）')
    }

    // 本机 Unity 路径（如与实际不符只改这一处；14 号文档步骤 5）
    environment {
        UNITY = 'C:\\Program Files\\Unity\\Hub\\Editor\\6000.3.20f1\\Editor\\Unity.exe'
        // 本机 Python(用户级安装,SYSTEM 账户 PATH 无 python/py,必须绝对路径;14 号文档 FAQ)
        PYTHON = 'C:\\Users\\slx97\\AppData\\Local\\Programs\\Python\\Python312\\python.exe'
        // Android 工具链显式注入:CI 账户不继承用户 GUI Preferences。
        // Unity 6000.3 官方推荐 NDK = 27.2.12479018(r27c),内置 r27(27.0.12077973)
        // 版本不匹配 → Detect Android NDK 校验失败;项目终版 r27c(用户本地点过,2026-08-24 实测)。
        // 注入方式 = 环境变量 ANDROID_NDK_ROOT(Unity IssueTracker 实证的 headless workaround;
        //   ANDROID_NDK_HOME 单独不够;-androidNdkRoot 参数 6000 已废弃,实测无效,勿用)
        ANDROID_NDK_ROOT = 'D:/Projects/AI/AndroidNDK/android-ndk-r27c'
        // 自定义工作区：避开 SYSTEM profile 路径大小写问题（Unity 大小写敏感，见文件头注释）
        WS = 'D:/JenkinsWS/SudokuGameBox-CI'
    }

    stages {
        // 在自定义 ws 内检出仓库（job 的 SCM 配置指向本地仓库路径）
        stage('Checkout') {
            steps {
                ws("$WS") {
                    checkout scm
                }
            }
        }

        // CI-1 编译 + EditMode 全量回归（基线：现有用例数不得下降，13 号文档 §6）
        stage('CI-1 编译+EditMode 回归') {
            steps {
                ws("$WS") {
                    powershell(script: '''
                        $unity = $env:UNITY
                        $ws    = $env:WORKSPACE
                        # Unity 工程在 GameBox/ 子目录（仓库根不是工程，13 号文档 §6 命令模板）
                        $proj  = "$ws\\GameBox"
                        # prep: stale lockfile, result dirs, Unity cache dir (SYSTEM profile)
                        Remove-Item "$proj\\Temp\\UnityLockfile" -Force -ErrorAction SilentlyContinue
                        New-Item -ItemType Directory -Force "$ws\\TestResults", "$ws\\Build\\Logs", "$env:LOCALAPPDATA\\Unity\\Caches" | Out-Null
                        # 正斜杠传 -projectPath（Unity 规范化路径比较的坑，见 14 号文档 FAQ）
                        $projFwd = $proj -replace '\\\\', '/'
                        # -runTests must NOT use -quit (Phase 6 lesson)
                        $p = Start-Process -FilePath $unity -WorkingDirectory $proj -ArgumentList @(
                            "-batchmode",
                            "-projectPath", $projFwd,
                            "-runTests",
                            "-testPlatform", "EditMode",
                            "-testResults", "$ws\\TestResults\\ci-editmode.xml",
                            "-logFile", "$ws\\Build\\Logs\\ci-editmode.log"
                        ) -RedirectStandardOutput "$ws\\Build\\Logs\\ci-editmode-stdout.log" -RedirectStandardError "$ws\\Build\\Logs\\ci-editmode-stderr.log" -PassThru -Wait
                        # -Wait mandatory for GUI exe (14 doc FAQ); redirects fix pipe-zombie (licensing child)
                        Write-Host "Unity exit code = $($p.ExitCode)"
                        if ($p.ExitCode -ne 0) { exit $p.ExitCode }
                        # Unity 产出 NUnit3 嵌套 suite XML,JUnit 插件解析不了(JENKINS-6545)
                        # → 用本地转换器拍平为 JUnit 扁平 XML(见 tools/nunit3_to_junit.py)
                        & $env:PYTHON "$ws\\tools\\nunit3_to_junit.py" "$ws\\TestResults\\ci-editmode.xml" "$ws\\TestResults\\ci-editmode-junit.xml"
                        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                    ''')
                    // JUnit 解析 NUnit XML → Jenkins 测试趋势页（相对 ws 内路径）
                    junit allowEmptyResults: true, testResults: 'TestResults/ci-editmode-junit.xml'
                }
            }
        }

        // CI-1b PlayMode 测试（可选：Build with Parameters 勾 RUN_PLAY_MODE 时执行）
        stage('CI-1b PlayMode（可选）') {
            when { expression { params.RUN_PLAY_MODE } }
            steps {
                ws("$WS") {
                    powershell(script: '''
                        $unity = $env:UNITY
                        $ws    = $env:WORKSPACE
                        # Unity 工程在 GameBox/ 子目录（仓库根不是工程）
                        $proj  = "$ws\\GameBox"
                        Remove-Item "$proj\\Temp\\UnityLockfile" -Force -ErrorAction SilentlyContinue
                        New-Item -ItemType Directory -Force "$ws\\TestResults", "$ws\\Build\\Logs", "$env:LOCALAPPDATA\\Unity\\Caches" | Out-Null
                        # 正斜杠传 -projectPath（Unity 规范化路径比较的坑）
                        $projFwd = $proj -replace '\\\\', '/'
                        $p = Start-Process -FilePath $unity -WorkingDirectory $proj -ArgumentList @(
                            "-batchmode",
                            "-projectPath", $projFwd,
                            "-runTests",
                            "-testPlatform", "PlayMode",
                            "-testResults", "$ws\\TestResults\\ci-playmode.xml",
                            "-logFile", "$ws\\Build\\Logs\\ci-playmode.log"
                        ) -RedirectStandardOutput "$ws\\Build\\Logs\\ci-playmode-stdout.log" -RedirectStandardError "$ws\\Build\\Logs\\ci-playmode-stderr.log" -PassThru -Wait
                        Write-Host "Unity exit code = $($p.ExitCode)"
                        if ($p.ExitCode -ne 0) { exit $p.ExitCode }
                        # 同 CI-1:NUnit3 → JUnit 扁平转换,否则 JUnit 插件解析 0 结果
                        & $env:PYTHON "$ws\\tools\\nunit3_to_junit.py" "$ws\\TestResults\\ci-playmode.xml" "$ws\\TestResults\\ci-playmode-junit.xml"
                        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                    ''')
                    junit allowEmptyResults: true, testResults: 'TestResults/ci-playmode-junit.xml'
                }
            }
        }

        // CI-2 资产校验（纯 Python 零 Unity 依赖 → 秒级，唯一廉价常驻 job，10 文档 §13 成本纪律）
        stage('CI-2 资产校验') {
            steps {
                ws("$WS") {
                    powershell(script: '''
                        $ws = $env:WORKSPACE
                        # SYSTEM 账户 PATH 无 python/py,直调绝对路径(14 号文档 FAQ)
                        & $env:PYTHON "$ws\\tools\\asset_check.py"
                        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                    ''')
                }
            }
        }

        // CI-3 AAB 构建（6.5-3：复用 BuildScript.cs，CLI 产出；默认关，勾 BUILD_AAB 运行）
        stage('CI-3 构建 AAB') {
            when { expression { params.BUILD_AAB } }
            steps {
                ws("$WS") {
                    // 签名密码走 Jenkins 凭据(credentialsId 见 17 号文档 §5 / 15 号文档 §4.3:
                    // 密码不写仓库、不写脚本;withCredentials 注入环境变量 → Start-Process 子进程继承)
                    withCredentials([
                        string(credentialsId: 'box-keystore-pass', variable: 'BOX_KEYSTORE_PASS'),
                        string(credentialsId: 'box-key-pass', variable: 'BOX_KEY_PASS')
                    ]) {
                        powershell(script: '''
                            $unity = $env:UNITY
                            $ws    = $env:WORKSPACE
                            # Unity 工程在 GameBox/ 子目录（仓库根不是工程）
                            $proj  = "$ws\\GameBox"
                            # keystore 被 .gitignore 忽略,CI 检出不含 → 从受控目录(D:/JenkinsWS/keystore)
                            # 拷入工作区仓库根 Build/keystore(BuildScript 从 Application.dataPath 上推两级
                            # 求仓库根再拼 Build/keystore,见 17 号文档 §5;勿拷进 GameBox 工程内)
                            New-Item -ItemType Directory -Force "$ws\\Build\\keystore" | Out-Null
                            Copy-Item "D:/JenkinsWS/keystore/upload.keystore" "$ws\\Build\\keystore\\upload.keystore" -Force
                            # withCredentials 注入的变量在 PS 脚本里即 $env:BOX_KEYSTORE_PASS
                            Remove-Item "$proj\\Temp\\UnityLockfile" -Force -ErrorAction SilentlyContinue
                            New-Item -ItemType Directory -Force "$ws\\Build\\Logs", "$env:LOCALAPPDATA\\Unity\\Caches" | Out-Null
                            # 正斜杠传 -projectPath（Unity 规范化路径比较的坑）
                            $projFwd = $proj -replace '\\\\', '/'
                            # 单 AAB 产物(17 号文档:APK+AAB 同会话曾 Bee 缓存挂死 #32,现产物名 Rovilo,4c1272f);
                            # NDK 注入走 environment 块 ANDROID_NDK_ROOT(Start-Process 继承;SYSTEM 无 GUI Preferences)
                            $p = Start-Process -FilePath $unity -WorkingDirectory $proj -ArgumentList @(
                                "-batchmode", "-quit",
                                "-projectPath", $projFwd,
                                "-executeMethod", "BuildScript.BuildAndroidAab",
                                "-logFile", "$ws\\Build\\Logs\\ci-aab.log"
                            ) -RedirectStandardOutput "$ws\\Build\\Logs\\ci-aab-stdout.log" -RedirectStandardError "$ws\\Build\\Logs\\ci-aab-stderr.log" -PassThru -Wait
                            Write-Host "Unity exit code = $($p.ExitCode)"
                            if ($p.ExitCode -ne 0) { exit $p.ExitCode }
                            # 签名验证:日志须含"已应用上传签名"(BuildScript 硬失败保证无 debug 签名产物)
                            $signed = Select-String -Path "$ws\\Build\\Logs\\ci-aab.log" -Pattern "已应用上传签名" -Quiet
                            if (-not $signed) { Write-Host "签名行缺失,视为失败"; exit 1 }
                        ''')
                        // 归档 AAB → 构建记录 Artifacts（本机方案替代 GitHub Actions artifact）
                        // 输出在工程目录内（BuildScript.cs OutputDir 相对工程），工作区根是 GameBox/ 子目录
                        archiveArtifacts artifacts: 'GameBox/Build/Android/Rovilo.aab', onlyIfSuccessful: true
                    }
                }
            }
        }
    }

    // 失败 → 构建历史标红，阻止交付/产物归档（10 文档 §13 验收）
    post {
        failure {
            echo 'CI 失败：阻止交付。查看构建历史与失败阶段日志定位原因（FAQ 见 14 号文档）'
        }
    }
}
