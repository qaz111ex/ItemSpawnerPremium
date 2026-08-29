#Requires -Version 7.0
<#
.SYNOPSIS
    ItemSpawnerPremium 发行装配脚本（默认**不**打 zip）。

.DESCRIPTION
    把 dist\ItemSpawnerPremium\ 变成完全可再生的产物目录：DLL 来自 clean Release 构建，
    文档与 manifest/icon 来自 src 的权威副本（src\package\），不再需要任何手工拷贝。

    ────────────────────────── 发布流程（唯一正确顺序） ──────────────────────────
    1. 改代码 → 四处版本号同步：
         a) ItemSpawnerEnhancement.csproj 的 <Version>
         b) Plugin.cs 的 [BepInPlugin(..., "x.y.z")]
         c) package\manifest.json 的 version_number
         d) CHANGELOG.md 顶部新增 ## [x.y.z] - YYYY-MM-DD
    2. git commit（工作区必须干净 —— 否则 DLL 里的 SourceLink/InformationalVersion
       会指向一个**不包含实际发布代码**的提交，调试符号与版本溯源全部失真。
       历史上 2.1.0 的发行 DLL 就是这么打出来的：informational version 写着
       2.1.0+3e79798，而 3e79798 其实是 2.0.1 期的 README 提交）。
    3. git tag vx.y.z
    4. pwsh .\pack.ps1              ← 校验 + clean Release 构建 + 装配 dist（不打包）
    5. 人工检查 dist 内容与 SHA256，确认无误后再上传 Thunderstore。
       需要 zip 时显式加 -Pack（默认关闭，见下）。
    ──────────────────────────────────────────────────────────────────────────────

    校验门禁（任一失败即非零退出，不产出任何东西）：
      * git 工作区干净（git status --porcelain 为空）
      * HEAD 有对应 v<version> tag（缺失只警告，不阻断；用 -RequireTag 升级为硬失败）
      * 四处版本号完全一致
      * ItemCatalog.cs 不变量（verify_catalog.py：隐藏 60 / 可见 153、ItemTagMap 精确对应）
      * 单元测试全部通过（tests\ItemSpawnerPremium.Tests，零 Unity 依赖）

.PARAMETER Pack
    生成 Thunderstore zip。**默认关闭**，因为用户明确要求打包这一步必须显式触发、
    不允许脚本自作主张产出 zip。

.PARAMETER RequireTag
    HEAD 缺少 v<version> tag 时直接失败（默认只警告）。

.PARAMETER SkipCatalogVerify
    跳过 ItemCatalog.cs 不变量校验（仅在没有 python 或真值文件的机器上临时使用）。

.PARAMETER SkipTests
    跳过单元测试（tests\ItemSpawnerPremium.Tests，需要 net8.0 SDK）。

.PARAMETER TruthJson
    item_truth.json 路径，传给 verify_catalog.py。

.EXAMPLE
    pwsh .\pack.ps1
    pwsh .\pack.ps1 -RequireTag
    pwsh .\pack.ps1 -Pack          # 只有明确需要 zip 时才加
#>
[CmdletBinding()]
param(
    [switch]$Pack,
    [switch]$RequireTag,
    [switch]$SkipCatalogVerify,
    [switch]$SkipTests,
    [string]$TruthJson = 'D:\zhuanban\youhua\item_truth.json',
    [string]$DistRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\ItemSpawnerPremium')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SrcRoot = $PSScriptRoot
$Csproj = Join-Path $SrcRoot 'ItemSpawnerEnhancement.csproj'
$PackageDir = Join-Path $SrcRoot 'package'
$ReleaseDir = Join-Path $SrcRoot 'bin\Release'
$TestsCsproj = Join-Path $SrcRoot 'tests\ItemSpawnerPremium.Tests\ItemSpawnerPremium.Tests.csproj'
$errors = [System.Collections.Generic.List[string]]::new()

function Write-Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg) { Write-Host "    [OK] $msg" -ForegroundColor Green }
function Write-Warn2([string]$msg) { Write-Host "    [WARN] $msg" -ForegroundColor Yellow }
function Add-Failure([string]$msg) { $script:errors.Add($msg); Write-Host "    [FAIL] $msg" -ForegroundColor Red }

function Get-FirstMatch {
    param([string]$Path, [string]$Pattern)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $m = [regex]::Match((Get-Content -LiteralPath $Path -Raw), $Pattern)
    if ($m.Success) { return $m.Groups[1].Value } else { return $null }
}

# ── 1. git 工作区必须干净 ─────────────────────────────────────────────────────
Write-Step '校验 git 工作区'
$gitStatus = & git -C $SrcRoot status --porcelain
if ($LASTEXITCODE -ne 0) {
    Add-Failure 'git status 执行失败（src 不是 git 仓库？）'
} elseif ($gitStatus) {
    Add-Failure @"
工作区存在未提交改动，拒绝打包。发行 DLL 的 SourceLink / AssemblyInformationalVersion
会指向 HEAD，而 HEAD 不含这些改动 —— 打出来的包无法溯源。请先提交：
$([string]::Join([Environment]::NewLine, ($gitStatus | ForEach-Object { '  ' + $_ })))
"@
} else {
    Write-Ok "工作区干净（HEAD = $(& git -C $SrcRoot rev-parse --short HEAD))"
}

# ── 2. 四处版本号一致 ─────────────────────────────────────────────────────────
Write-Step '校验四处版本号一致'
$versions = [ordered]@{
    'csproj <Version>'       = Get-FirstMatch $Csproj '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>'
    'Plugin.cs [BepInPlugin]' = Get-FirstMatch (Join-Path $SrcRoot 'Plugin.cs') '\[BepInPlugin\(\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*"([0-9]+\.[0-9]+\.[0-9]+)"'
    'manifest.json'          = Get-FirstMatch (Join-Path $PackageDir 'manifest.json') '"version_number"\s*:\s*"([0-9]+\.[0-9]+\.[0-9]+)"'
    'CHANGELOG.md'           = Get-FirstMatch (Join-Path $SrcRoot 'CHANGELOG.md') '(?m)^##\s*\[([0-9]+\.[0-9]+\.[0-9]+)\]'
}
foreach ($k in $versions.Keys) {
    if ([string]::IsNullOrWhiteSpace($versions[$k])) { Add-Failure "无法从 $k 解析出版本号" }
    else { Write-Host "    $k = $($versions[$k])" }
}
$distinct = @($versions.Values | Where-Object { $_ } | Select-Object -Unique)
$version = $null
if ($distinct.Count -eq 1 -and $versions.Values -notcontains $null) {
    $version = $distinct[0]
    Write-Ok "四处版本号一致：$version"
} elseif ($distinct.Count -gt 1) {
    Add-Failure "版本号不一致：$($distinct -join ' / ')。请把四处改成同一个值后重跑。"
}

# ── 3. HEAD 是否有对应 tag ───────────────────────────────────────────────────
if ($version) {
    Write-Step "校验 HEAD 的 tag (v$version)"
    $tags = @(& git -C $SrcRoot tag --points-at HEAD)
    if ($tags -contains "v$version" -or $tags -contains $version) {
        Write-Ok "HEAD 已打 tag：$($tags -join ', ')"
    } else {
        $msg = "HEAD 上没有 v$version tag" + $(if ($tags) { "（当前 tag: $($tags -join ', ')）" } else { '（HEAD 无任何 tag）' }) + "。建议先 git tag v$version 再打包，否则日后无法定位发行对应的源码。"
        if ($RequireTag) { Add-Failure $msg } else { Write-Warn2 $msg }
    }
}

# ── 4. ItemCatalog.cs 不变量 ─────────────────────────────────────────────────
if ($SkipCatalogVerify) {
    Write-Step 'ItemCatalog.cs 不变量校验：已按 -SkipCatalogVerify 跳过'
} else {
    Write-Step '校验 ItemCatalog.cs 不变量 (verify_catalog.py)'
    $py = Get-Command python -ErrorAction SilentlyContinue
    if (-not $py) { $py = Get-Command python3 -ErrorAction SilentlyContinue }
    if (-not $py) {
        Add-Failure '找不到 python，无法校验 ItemCatalog.cs 不变量（确认无 python 环境时可加 -SkipCatalogVerify）'
    } else {
        & $py.Source (Join-Path $SrcRoot 'verify_catalog.py') --truth $TruthJson |
            ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -ne 0) { Add-Failure "verify_catalog.py 校验失败（退出码 $LASTEXITCODE）" }
        else { Write-Ok 'ItemCatalog.cs 不变量成立' }
    }
}

# ── 5. 权威副本存在性 ────────────────────────────────────────────────────────
Write-Step '校验 src\package\ 权威副本'
foreach ($f in @('manifest.json', 'icon.png')) {
    if (Test-Path -LiteralPath (Join-Path $PackageDir $f)) { Write-Ok "package\$f" }
    else { Add-Failure "缺少 package\$f（dist 的对应文件由它生成，不可反向手工维护）" }
}

# ── 5b. 单元测试 ─────────────────────────────────────────────────────────────
# 测试工程零 Unity 依赖，覆盖搜索归一化/拼音/排名/全序比较/分类隐藏规则/收藏编解码/本地化。
# 放在构建之前：这些是纯逻辑回归，跑失败说明不该出包。
if ($SkipTests) {
    Write-Step '单元测试：已按 -SkipTests 跳过'
} else {
    Write-Step '运行单元测试'
    if (-not (Test-Path -LiteralPath $TestsCsproj)) {
        Add-Failure "找不到测试工程 $TestsCsproj"
    } else {
        & dotnet test $TestsCsproj -nologo --verbosity quiet
        if ($LASTEXITCODE -ne 0) { Add-Failure "单元测试失败（退出码 $LASTEXITCODE）" }
        else { Write-Ok '单元测试全部通过' }
    }
}

if ($errors.Count -gt 0) {
    Write-Host ''
    Write-Host "前置校验未通过（$($errors.Count) 项），未执行构建与装配。" -ForegroundColor Red
    exit 1
}

# ── 6. clean Release 构建 ────────────────────────────────────────────────────
Write-Step 'clean Release 构建'
if (Test-Path -LiteralPath $ReleaseDir) { Remove-Item -LiteralPath $ReleaseDir -Recurse -Force }
& dotnet build $Csproj -c Release --no-incremental -nologo -clp:NoSummary
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Release 构建失败，未改动 dist。' -ForegroundColor Red
    exit 1
}
$dll = Join-Path $ReleaseDir 'ItemSpawnerPremium.dll'
if (-not (Test-Path -LiteralPath $dll)) {
    Write-Host "构建成功但找不到 $dll。" -ForegroundColor Red
    exit 1
}
Write-Ok "Release DLL：$dll ($((Get-Item -LiteralPath $dll).Length) 字节)"

# ── 7. 装配 dist ─────────────────────────────────────────────────────────────
# 只取 DLL，绝不拷 pdb：pdb 会把本机绝对路径带进发行包，且对玩家毫无用处。
Write-Step "装配 $DistRoot"
$pluginsDir = Join-Path $DistRoot 'plugins'
New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginsDir 'ItemSpawnerPremium.dll') -Force
Get-ChildItem -LiteralPath $pluginsDir -Filter *.pdb -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force; Write-Warn2 "已清除残留 pdb：$($_.Name)" }

# src 为权威，单向覆盖 dist —— 消除历史上 src↔dist 文档各改一份造成的分叉。
foreach ($f in @('README.md', 'CHANGELOG.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    $from = Join-Path $SrcRoot $f
    if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination (Join-Path $DistRoot $f) -Force }
    else { Write-Warn2 "src 缺少 $f，dist 中的旧副本保持不变" }
}
foreach ($f in @('manifest.json', 'icon.png')) {
    Copy-Item -LiteralPath (Join-Path $PackageDir $f) -Destination (Join-Path $DistRoot $f) -Force
}
Write-Ok 'dist 已由 src 单向覆盖（dist 是纯产物目录，请勿在其中手工改文件）'

Write-Step '产物清单与 SHA256'
Get-ChildItem -LiteralPath $DistRoot -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($DistRoot.Length).TrimStart('\')
    '    {0,-40} {1,9}  {2}' -f $rel, $_.Length, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}

# ── 8. 打包（默认关闭） ──────────────────────────────────────────────────────
if ($Pack) {
    Write-Step '生成 Thunderstore zip（-Pack 已显式指定）'
    $zip = Join-Path (Split-Path -Parent $DistRoot) "ItemSpawnerPremium-$version.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $DistRoot '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Ok "$zip  SHA256=$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)"
} else {
    Write-Host ''
    Write-Host 'dist 已就绪；未生成 zip（打包默认关闭，需要时显式加 -Pack）。' -ForegroundColor Yellow
}

Write-Host ''
Write-Host "完成：ItemSpawnerPremium $version" -ForegroundColor Green
exit 0
