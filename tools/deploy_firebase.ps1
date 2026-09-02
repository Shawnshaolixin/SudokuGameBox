# =========================================================
# 9-4 远程内容 Firebase Hosting 部署脚本(Phase 9,10 文档 §16.5 / 20 复盘)
# 双通道布局(2026-09-02 用户拍板):firebase-hosting/public/<Env>/Android/
#   目录结构(URL 契约:Addressables catalog 内 id 在构建期烘焙为 <前缀>/Android/<文件>,
#   前缀 = 站点根以下任意段,Android 段位置不可动):
#     firebase-hosting/
#     ├── firebase.json          ← public: "public"(相对本目录)
#     ├── .firebaserc            ← default project = sudokugamebox
#     └── public/
#         ├── staging/           ← 开发/真机验证内容(dev APK 的 RemoteServerUrl 指这里)
#         │   ├── Android/       ← catalog_1.0.bin/.hash + *.bundle(Addressables 契约目录)
#         │   └── manifest/      ← 预留:module_overrides.json(Phase 10 从 AB 迁出后落位)
#         └── production/        ← 上架内容(发布 AAB 经构建注入指向这里,红线 9:不入库)
# 缓存头(firebase.json):catalog .bin/.hash = no-cache,*.bundle 内容寻址 = immutable。
# 用法(需 firebase-tools + 已 firebase login):
#   powershell -ExecutionPolicy Bypass -File tools/deploy_firebase.ps1 [-Channel staging|production] [-Target Android]
# 前置:已执行 Phase9Publish.BuildAll,ServerData 已产出。
# 流程纪律:内容先上 staging → 真机验收 → 验收通过再 -Channel production 提升。
# 注意:改 AOT 白名单代码后必须重跑 GenerateContent+BuildAll 再部署(metadata 一致性,坑⑧)。
# =========================================================
param(
    [ValidateSet("staging", "production")]
    [string]$Channel = "staging",
    [string]$Target = "Android"
)

$ErrorActionPreference = "Stop"
# 仓库根:经 $MyInvocation 求脚本所在目录的上级(PSScriptRoot 在部分调用方式下为空,如 git-bash 直调)
$script = [System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Definition)
$root = Split-Path -Parent (Split-Path -Parent $script)
$serverData = Join-Path $root "GameBox\ServerData\$Target"
$hosting = Join-Path $root "firebase-hosting"     # firebase.json 所在目录
$publicDir = Join-Path $hosting "public\$Channel"

if (-not (Test-Path $serverData)) {
    Write-Error "ServerData 不存在: $serverData`n请先执行 Phase9Publish.BuildAll(构建远程产物)"
    exit 1
}
if (-not (Test-Path (Join-Path $hosting "firebase.json"))) {
    Write-Error "缺少 firebase-hosting\firebase.json(Hosting 配置)"
    exit 1
}

# 只重建本通道的 Android/ 契约目录(production/staging 互不影响)
# 注意:目标目录已预创建 → 源必须带通配符"拷内容不拷目录",
# 否则会把 ServerData\Android 整体嵌进 public\<Channel>\Android\Android(URL 契约 404,2026-09-02 踩过)
$publicTarget = Join-Path $publicDir $Target
if (Test-Path $publicTarget) { Remove-Item -Recurse -Force $publicTarget }
New-Item -ItemType Directory -Path $publicTarget | Out-Null
Copy-Item -Recurse -Force (Join-Path $serverData "*") $publicTarget | Out-Null

Write-Host "`n[deploy_firebase] 已拷贝 ServerData\$Target → public\$Channel\$Target,待上传:" -ForegroundColor Green
Get-ChildItem -Recurse -File $publicTarget | ForEach-Object {
    Write-Host ("  " + $_.FullName.Substring((Join-Path $hosting "public").Length + 1) + "  (" + [math]::Round($_.Length/1KB, 1) + " KB)")
}

Write-Host "`n[deploy_firebase] firebase deploy(环境: $Channel)→ https://sudokugamebox.web.app/$Channel/$Target/ ..." -ForegroundColor Cyan
Push-Location $hosting
try {
    firebase deploy --only hosting
}
finally {
    Pop-Location
}
Write-Host "`n自测: curl -sI https://sudokugamebox.web.app/$Channel/$Target/catalog_1.0.hash" -ForegroundColor Green
