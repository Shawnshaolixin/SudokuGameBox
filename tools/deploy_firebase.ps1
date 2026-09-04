# =========================================================
# 9-4 远程内容 Firebase Hosting 部署脚本(Phase 9,10 文档 §16.5 / 20 复盘)
# =========================================================
# 双通道布局(2026-09-02 拍板;2026-09-04 升级为版本化目录 + index.json 指针):
#
#   firebase-hosting/public/<Channel>/
#   ├── index.json                     ← 版本指针(客户端每次启动解析,no-cache;回滚=改写本文件)
#   ├── Android/                       ← 共享内容目录(旧客户端兼容 + bundle 共享层)
#   │   ├── catalog_1.0.bin/.hash      ← 旧客户端(不解析 index)仍读这里的固定名 catalog
#   │   └── *.bundle                   ← bundle 内容寻址(hash 文件名),全版本共享,URL 永不变化保缓存
#   ├── <version>/Android/
#   │   └── catalog_1.0.bin/.hash      ← 新客户端按 index.json 指示读版本化 catalog(每版一份,互不覆盖)
#   └── _history/<时间戳>/             ← 部署前自动归档的旧 catalog(防御性备份,见下)
#
# 设计要点:
#   · bundle 不进版本目录——内容寻址天然不可变,共享一份即可;版本切换只重下 catalog(几 KB)。
#   · 旧客户端兼容:线上还有按固定路径 RemoteServerUrl/Android/catalog_1.0.bin 拉取的 v1.1 包,
#     因此每次发布仍同步覆盖共享目录的 catalog(双写),待旧包淘汰后可下线该步骤。
#   · 回退=改 index.json 指回旧版本目录 + 重新 deploy,秒级生效;旧 bundle 因累积保留仍可下载。
#   · ServerData/Android 内 bundle 是累积产物(历史 bundle 保留在本地),版本化 catalog 回退后
#     引用的旧 bundle 也存在于共享目录——不要清理 ServerData 里的旧 bundle,否则回退断链。
#
# 缓存头(firebase.json):index.json 与 catalog .bin/.hash = no-cache(每次启动拿最新),
#   *.bundle 内容寻址 = immutable。
#
# 用法(需 firebase-tools + 已 firebase login):
#   powershell -ExecutionPolicy Bypass -File tools/deploy_firebase.ps1 [-Channel staging|production]
#             [-Target Android] [-Version <版本名>] [-RollbackTo <旧版本名>]
#
#   发布(默认 Channel=staging,Version=自动时间戳 vyyyyMMdd-HHmmss):
#     powershell -ExecutionPolicy Bypass -File tools/deploy_firebase.ps1
#   指定版本名发布:
#     powershell -ExecutionPolicy Bypass -File tools/deploy_firebase.ps1 -Version v20260904-1
#   回滚到历史版本(改指针+恢复旧客户端兼容 catalog,无需 ServerData/无需重新构建):
#     powershell -ExecutionPolicy Bypass -File tools/deploy_firebase.ps1 -RollbackTo v20260903-080000
#   查看可回退版本:列 public/<Channel>/ 下的版本目录,或浏览
#     https://sudokugamebox.web.app/<Channel>/index.json
#
# 前置:发布模式需先执行 Phase9Publish.BuildAll(ServerData 已产出);回滚模式无需构建。
# 流程纪律:内容先上 staging → 真机验收 → 验收通过再 -Channel production 提升。
# 注意:改 AOT 白名单代码后必须重跑 GenerateContent+BuildAll 再部署(metadata 一致性,坑⑧)。
# =========================================================
param(
    [ValidateSet("staging", "production")]
    [string]$Channel = "staging",
    [string]$Target = "Android",
    # 发布模式:新版本目录名(默认时间戳)。仅允许 URL 安全字符(字母数字._-),防止路径注入
    [string]$Version = "",
    # 回滚模式:改写 index.json 指回该历史版本并恢复旧客户端兼容 catalog。与 -Version 互斥
    [string]$RollbackTo = ""
)

$ErrorActionPreference = "Stop"
# 仓库根:经 $MyInvocation 求脚本所在目录的上级(PSScriptRoot 在部分调用方式下为空,如 git-bash 直调)
$script = [System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Definition)
$root = Split-Path -Parent (Split-Path -Parent $script)
$serverData = Join-Path $root "GameBox\ServerData\$Target"
$hosting = Join-Path $root "firebase-hosting"     # firebase.json 所在目录
$publicDir = Join-Path $hosting "public\$Channel"
$catalogNames = @("catalog_1.0.bin", "catalog_1.0.hash")   # catalog 固定文件名(Addressables 契约)

if (-not (Test-Path (Join-Path $hosting "firebase.json"))) {
    Write-Error "缺少 firebase-hosting\firebase.json(Hosting 配置)"
    exit 1
}

# ---- 公共工具:把线上当前共享目录 catalog 归档到 _history\<时间戳>\(回退保险,2026-09-04) ----
# 背景:共享目录 catalog 是旧客户端唯一入口且每次覆盖,ServerData 不入库(gitignore 红线 9),
# 归档保证"任何时刻线上 catalog"都有底档;版本化目录本身也是全量历史,此处是第二道保险。
function Backup-LegacyCatalog {
    $legacyDir = Join-Path $publicDir $Target
    if (-not (Test-Path $legacyDir)) { return }
    $historyDir = Join-Path $publicDir "_history\$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    $archived = $false
    foreach ($name in $catalogNames) {
        $existing = Join-Path $legacyDir $name
        if (Test-Path $existing) {
            New-Item -ItemType Directory -Path $historyDir -Force | Out-Null
            Copy-Item -Force $existing (Join-Path $historyDir $name)
            $archived = $true
        }
    }
    if ($archived) {
        Write-Host "[deploy_firebase] 已归档线上旧 catalog → $historyDir" -ForegroundColor DarkGray
    }
}

# ---- 公共工具:读 ServerData 产出的 catalog hash(index.json 的 catalogHash 字段用) ----
function Get-CatalogHash {
    $hashFile = Join-Path $serverData "catalog_1.0.hash"
    if (-not (Test-Path $hashFile)) { Write-Error "缺少 $hashFile,请先执行 Phase9Publish.BuildAll" }
    return (Get-Content $hashFile -Raw).Trim()
}

# ---- 公共工具:写 index.json 版本指针(客户端 JsonUtility 解析,字段与 RemoteContentIndex 对应) ----
function Write-ChannelIndex {
    param([string]$Ver, [string]$CatalogHash, [string]$Previous = "")
    $now = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss zzz")
    $idx = [ordered]@{
        version       = $Ver          # 当前内容版本(=版本目录名),客户端据此拼版本化 catalog URL
        channel       = $Channel      # 通道标识(staging/production),诊断用
        catalogHash   = $CatalogHash  # 该版本 catalog hash,诊断/校验用(客户端主要依赖 Addressables 自身 hash 检查)
        deployedAt    = $now          # 部署时间,审计用
        previousVersion = $Previous   # 回滚时记录的回退来源,正常发布为空;审计用
    }
    $json = ConvertTo-Json $idx
    # 显式无 BOM UTF8:Windows PowerShell 5.1 的 -Encoding UTF8 会写 BOM,客户端 JsonUtility 解析带 BOM 文本有历史坑
    [System.IO.File]::WriteAllText((Join-Path $publicDir "index.json"), $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "[deploy_firebase] index.json 已更新: version=$Ver" -ForegroundColor Green
}

# =========================================================
# 回滚模式:-RollbackTo 指回历史版本(无需 ServerData/构建,只改指针)
# =========================================================
if (-not [string]::IsNullOrEmpty($RollbackTo)) {
    if (-not [string]::IsNullOrEmpty($Version)) {
        Write-Error "-Version 与 -RollbackTo 互斥:发布用 -Version,回滚用 -RollbackTo"
        exit 1
    }
    $targetCatalogDir = Join-Path $publicDir "$RollbackTo\$Target"
    $targetCatalogBin = Join-Path $targetCatalogDir "catalog_1.0.bin"
    if (-not (Test-Path $targetCatalogBin)) {
        Write-Error "回滚目标版本不存在或缺少 catalog: $targetCatalogDir`n可回退版本=public\$Channel\ 下的版本目录"
        exit 1
    }

    Backup-LegacyCatalog   # 当前线上 catalog 先归档(回滚动作本身也要可撤销)

    # 恢复旧客户端兼容 catalog:把目标版本 catalog 覆盖回共享目录,让旧包也一并回退
    $legacyDir = Join-Path $publicDir $Target
    New-Item -ItemType Directory -Path $legacyDir -Force | Out-Null
    foreach ($name in $catalogNames) {
        Copy-Item -Force (Join-Path $targetCatalogDir $name) (Join-Path $legacyDir $name)
    }
    Write-Host "[deploy_firebase] 旧客户端兼容 catalog 已恢复为 $RollbackTo" -ForegroundColor Green

    $rollbackHash = (Get-Content (Join-Path $targetCatalogDir "catalog_1.0.hash") -Raw).Trim()
    $previous = ""
    $idxFile = Join-Path $publicDir "index.json"
    if (Test-Path $idxFile) {
        try { $previous = (ConvertFrom-Json (Get-Content $idxFile -Raw)).version } catch { $previous = "" }
    }
    Write-ChannelIndex -Ver $RollbackTo -CatalogHash $rollbackHash -Previous $previous

    # 提醒:回退 catalog 引用的 bundle 必须仍在共享目录(内容寻址累积保留);缺失会导致该版本装载失败
    Write-Warning "回滚前请确认共享目录 $legacyDir 仍保留 $RollbackTo 所需的历史 bundle(默认累积不清理)"

    Write-Host "`n[deploy_firebase] firebase deploy(回滚 $Channel → $RollbackTo)→ https://sudokugamebox.web.app/$Channel/ ..." -ForegroundColor Cyan
    Push-Location $hosting
    try { firebase deploy --only hosting }
    finally { Pop-Location }
    Write-Host "`n自测: curl -s https://sudokugamebox.web.app/$Channel/index.json" -ForegroundColor Green
    exit 0
}

# =========================================================
# 发布模式:ServerData → 共享目录(bundle) + 版本目录(catalog) + index.json 指针
# =========================================================
if (-not (Test-Path $serverData)) {
    Write-Error "ServerData 不存在: $serverData`n请先执行 Phase9Publish.BuildAll(构建远程产物)"
    exit 1
}
# 版本名默认时间戳;校验 URL 安全字符(版本名会成为 URL 路径段,禁止路径分隔符等)
if ([string]::IsNullOrEmpty($Version)) { $Version = "v$(Get-Date -Format 'yyyyMMdd-HHmmss')" }
if ($Version -notmatch '^[A-Za-z0-9._-]+$') {
    Write-Error "非法版本名 '$Version'(仅允许字母数字._-,且不含路径分隔符)"
    exit 1
}

$legacyDir = Join-Path $publicDir $Target

# 1) 共享 bundle 目录:只增不删地覆盖拷贝(同 hash 文件内容相同)。
#    关键:绝不 wipe——旧版本化 catalog 引用的历史 bundle 都靠这里留底,清了回退就断链
#    (与旧版脚本差异:旧版整目录 Remove-Item 后重拷,会连带删掉历史 bundle)。
#    注意:源必须带通配符"拷内容不拷目录",否则会把 ServerData\Android 整体嵌进
#    public\<Channel>\Android\Android(URL 契约 404,2026-09-02 踩过)。
New-Item -ItemType Directory -Path $legacyDir -Force | Out-Null
Copy-Item -Force (Join-Path $serverData "*.bundle") $legacyDir | Out-Null

# 2) 旧客户端兼容 catalog:归档当前 → 覆盖为最新(双写;待旧包淘汰后可移除此步骤)
Backup-LegacyCatalog
foreach ($name in $catalogNames) {
    Copy-Item -Force (Join-Path $serverData $name) (Join-Path $legacyDir $name)
}

# 3) 版本化 catalog 目录:新客户端按 index.json 指示读 <version>/Android/catalog_1.0.*
#    只放 catalog 两个小文件(bundle 共享,见步骤 1)——版本切换设备端只需重下 catalog
$versionDir = Join-Path $publicDir "$Version\$Target"
New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
foreach ($name in $catalogNames) {
    Copy-Item -Force (Join-Path $serverData $name) (Join-Path $versionDir $name)
}

# 4) 写版本指针 index.json(客户端启动解析,决定本次走哪个版本目录)
$catalogHash = Get-CatalogHash
Write-ChannelIndex -Ver $Version -CatalogHash $catalogHash

Write-Host "`n[deploy_firebase] 发布内容就绪(通道 $Channel,版本 $Version),待上传:" -ForegroundColor Green
Get-ChildItem -File $legacyDir | ForEach-Object {
    Write-Host ("  $Target/" + $_.Name + "  (" + [math]::Round($_.Length/1KB, 1) + " KB)")
}
Get-ChildItem -File $versionDir | ForEach-Object {
    Write-Host ("  $Version/$Target/" + $_.Name + "  (" + [math]::Round($_.Length/1KB, 1) + " KB)")
}
Write-Host "  index.json  (version=$Version, catalogHash=$catalogHash)"

Write-Host "`n[deploy_firebase] firebase deploy(环境: $Channel)→ https://sudokugamebox.web.app/$Channel/ ..." -ForegroundColor Cyan
Push-Location $hosting
try {
    firebase deploy --only hosting
}
finally {
    Pop-Location
}
Write-Host "`n自测: curl -s https://sudokugamebox.web.app/$Channel/index.json" -ForegroundColor Green
Write-Host "回退: tools/deploy_firebase.ps1 -Channel $Channel -RollbackTo <历史版本名>" -ForegroundColor Green
