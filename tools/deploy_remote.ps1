# =========================================================
# 9-4 远程内容部署脚本(Phase 9,10 文档 §16.5)
# 用途:把 Addressables 全量/增量构建产物拷贝到 _deploy_remote/(gitignore),
#       并用 python http.server 起本机静态服务(开发用 RemoteHostURL=http://127.0.0.1:8000)。
# 用法:
#   powershell -ExecutionPolicy Bypass -File tools/deploy_remote.ps1 [-Target Android] [-Port 8000]
# 前置:已执行 Phase9Publish.BuildAll(或 ContentUpdateBuild),ServerData 已产出。
# 真机访问本机需:防火墙放行该端口 + 手机与电脑同网段(失败先 curl http://127.0.0.1:8000 自测)。
# =========================================================
param(
    [string]$Target = "Android",
    [int]$Port = 8000
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot   # 仓库根
# Remote.BuildPath = ServerData/[BuildTarget],相对路径基于工程根(GameBox)解析
$serverData = Join-Path $root "GameBox\ServerData\$Target"
$deploy = Join-Path $root "_deploy_remote"

if (-not (Test-Path $serverData)) {
    Write-Error "ServerData 不存在: $serverData`n请先执行 Phase9Publish.BuildAll(构建远程产物)"
    exit 1
}

# 全量清理部署目录,防旧产物残留(本地目录可随意重建,不入库)
if (Test-Path $deploy) { Remove-Item -Recurse -Force $deploy }
New-Item -ItemType Directory -Path $deploy | Out-Null

# 拷贝 ServerData(含 catalog.bin + bundle + hash)到部署根: _deploy_remote/$Target。
# 9-4 真机踩坑(2026-09-02):目录结构必须与 Addressables profile 的 URL 完全对齐 ——
# Remote.LoadPath = "{RemoteHostURL}/[BuildTarget]" → URL 形如 /Android/...,
# serve 根必须是 _deploy_remote,文件放在 _deploy_remote/Android/ 下(/Android/ 首段命中目录)。
# 两种错误结构都曾 404(服务器日志可证):_deploy_remote/ServerData/Android(多一层)、
# serve root 设为 _deploy_remote/Android(URL 又多找一层 Android/)。
# 注意:重跑本脚本前先确认旧 http.server 已停止(占用 _deploy_remote 目录会导致 Remove-Item/Copy-Item 静默失败,产物 404)
Copy-Item -Recurse -Force $serverData (Join-Path $deploy $Target) | Out-Null

Write-Host "`n[deploy_remote] 已拷贝 ServerData\$Target → _deploy_remote\$Target`n" -ForegroundColor Green
Get-ChildItem -Recurse -File (Join-Path $deploy $Target) | ForEach-Object {
    Write-Host ("  " + $_.FullName.Substring($deploy.Length + 1) + "  (" + [math]::Round($_.Length/1KB, 1) + " KB)")
}

Write-Host "`n[deploy_remote] 启动 HTTP 服务: http://127.0.0.1:$Port (Ctrl+C 停止)`n" -ForegroundColor Cyan
Write-Host "真机自测: curl http://<本机IP>:$Port/$Target/catalog_1.0.hash"
python -m http.server $Port --directory $deploy
