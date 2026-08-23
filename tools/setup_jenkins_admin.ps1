<#
工具脚本：Jenkins 本机一键安装（Phase 6.5 / 14 号文档步骤 3）
用法：右键本文件 → 「使用 PowerShell 运行」（脚本内自检管理员，不足会退出）
前置（本脚本只负责「管理员权限」那一步）：
  - 本次会话已完成：环境检查（JDK 21 已装 / git 已装；注意 Jenkins 2.568 要求 Java 21+，17 不行）、下载安装包
    Build\jenkins-install\jenkins.msi（官方 LTS）
关键事实（决定脚本可以全自动的原因）：
  - 本机 Unity License 已是「机器级」激活：C:\ProgramData\Unity\Unity_lic.ulf
    → Jenkins 服务用默认 LocalSystem 账户即可读取，无需改服务登录账号
      （14 号文档步骤 3「改登录用户」一步因此可跳过，License 机器级时无此坑）
本脚本步骤：静默安装 → 等服务启动 → 等 Web 端口就绪 → 输出解锁密码路径。
下一步（安装完成后，见 14 号文档步骤 4）：浏览器打开 http://localhost:8080 解锁并创建账号。
#>

# ① 管理员自检：不足则直接退出，避免半途因权限失败造成半安装状态
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "请右键本脚本 →「以管理员身份运行」。当前非管理员，无法安装 Windows 服务。"
    exit 1
}

$msi = "D:\Projects\AI\SudokuGameBox\Build\jenkins-install\jenkins.msi"
if (-not (Test-Path $msi)) { Write-Error "未找到安装包: $msi"; exit 1 }

# ② 预建 LocalSystem 的 Jenkins 数据目录（实测踩坑修复：2026-08-23 安装失败根因）
# Jenkins 以 LocalSystem 运行，PID 文件与 war 解压目录都在 systemprofile 下：
#   C:\Windows\system32\config\systemprofile\AppData\Local\Jenkins\
# 该目录默认不存在 → WinSW 写 jenkins.pid 报 DirectoryNotFoundException
# → 服务启动即退（事件 7034，发生 N 次）→ MSI 判定失败回滚 → 退出码 1603
# 修复：安装前先把目录建出来，PID 文件与 webroot 才有落点。
$sysprofileJenkins = Join-Path $env:SystemRoot "system32\config\systemprofile\AppData\Local\Jenkins"
[System.IO.Directory]::CreateDirectory((Join-Path $sysprofileJenkins "war")) | Out-Null
Write-Host "[prep] 已确保目录: $sysprofileJenkins"

# ③ 已存在则跳过安装（幂等，重启电脑后服务还在）
if (Get-Service -Name Jenkin* -ErrorAction SilentlyContinue) {
    Write-Host "[skip] Jenkins 服务已存在，跳过安装"
} else {
    Write-Host "[install] msiexec 静默安装 Jenkins ..."
    # /qn = 无界面；/norestart = 不自动重启；退出码 0=成功、3010=成功但需重启（属正常）
    $p = Start-Process msiexec -ArgumentList "/i", "`"$msi`"", "/qn", "/norestart" -PassThru -Wait
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        Write-Error "msiexec 退出码 $($p.ExitCode)（0=成功, 3010=成功需重启）；排查日志: %TEMP%\Jenkins*.log 与 $sysprofileJenkins\jenkins.wrapper.log"
        exit $p.ExitCode
    }
}

# ④ 启动 Jenkins Windows 服务
$svc = Get-Service -Name Jenkin* -ErrorAction SilentlyContinue
if (-not $svc) { Write-Error "服务 Jenkins 未创建，安装异常（查 %TEMP%\Jenkins*.log）"; exit 1 }
if ($svc.Status -ne 'Running') { Start-Service -Name $svc.Name; $svc.Refresh() }
Write-Host "[service] Jenkins 服务已启动: $($svc.Name) ($($svc.Status))"

# ⑤ 等 Web 端口就绪（Jenkins 首次启动需解压 war，最多等 2 分钟）
$ready = $false
for ($i = 0; $i -lt 24; $i++) {
    try {
        Invoke-WebRequest -Uri http://localhost:8080 -UseBasicParsing -TimeoutSec 3 | Out-Null
        $ready = $true; break
    } catch { Start-Sleep -Seconds 5 }
}
if ($ready) { Write-Host "[web] Jenkins 已就绪: http://localhost:8080" }
else { Write-Warning "[web] 端口 120 秒未就绪，稍后手动打开 http://localhost:8080 检查" }

# ⑥ 输出初始解锁密码位置（浏览器解锁页第一步要填的密码）
# JENKINS_HOME 实际位置（2026-08-23 实测）：%LocalAppData%\Jenkins\.jenkins（LocalSystem 下即 systemprofile 路径）
$pwPath = "C:\Windows\system32\config\systemprofile\AppData\Local\Jenkins\.jenkins\secrets\initialAdminPassword"
if (Test-Path $pwPath) {
    Write-Host "[unlock] 初始密码: 文件 $pwPath"
    Write-Host "         下一步（14 号文档步骤 4）：浏览器打开 http://localhost:8080 → 粘贴该密码解锁"
} else {
    Write-Warning "[unlock] 未找到初始密码文件（也许已解锁过；或服务尚未写入）"
}