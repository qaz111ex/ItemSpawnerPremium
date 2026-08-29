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

    硬门禁（任一失败即非零退出、不触碰 dist、不构建）：
      * git 工作区干净（git status --porcelain 为空）
      * 不存在 *.csproj.user / *.props.user / Directory.Build.*.user
        （.gitignore 忽略 *.user，而 MSBuild SDK 会自动 Import 同名 .user 文件 ——
         这类文件能静默改变编译结果却在 git status 里不可见，正是本脚本要防的失真）
      * 四处版本号完全一致，且 csproj 里 <Version> 只出现一次
      * manifest.json / icon.png 满足 Thunderstore 硬约束（icon 恰好 256×256 PNG、
        description ≤250 字符、name 仅 [A-Za-z0-9_]）
      * ItemCatalog.cs 不变量（verify_catalog.py：隐藏 62 / 可见 151、ItemTagMap 精确对应）
      * 单元测试全部通过（tests\ItemSpawnerPremium.Tests，零 Unity 依赖）
      * 装配后 dist 的文件集合恰好等于预期 7 项（多一个就失败）

    软门禁（只警告，不阻断）：
      * HEAD 有对应 v<version> tag —— 默认仅警告，加 -RequireTag 升级为硬失败
      * 游戏版本溯源信息（<GameDir>\version.txt、Assembly-CSharp.dll 的 SHA256）缺失

.PARAMETER Pack
    生成 Thunderstore zip。**默认关闭**，因为用户明确要求打包这一步必须显式触发、
    不允许脚本自作主张产出 zip。

.PARAMETER RequireTag
    HEAD 缺少 v<version> tag 时直接失败（默认只警告）。

.PARAMETER SkipCatalogVerify
    跳过 ItemCatalog.cs 不变量校验。**这是全量跳过、不留任何审计痕迹的开关**，
    只有在确实没有 python 时才该用。若只是真值文件路径不同，请改用 -TruthJson 或
    环境变量 ITEMSPAWNER_TRUTH_JSON —— 校验照跑，不留缺口。

.PARAMETER SkipTests
    跳过单元测试（tests\ItemSpawnerPremium.Tests，需要 net8.0 SDK）。

.PARAMETER TruthJson
    item_truth.json 路径，传给 verify_catalog.py。该文件不在 git 仓库内，所以默认值
    取环境变量 ITEMSPAWNER_TRUTH_JSON（与 verify_catalog.py 同一个变量），没设才回退
    到本机硬编码路径 —— 换机器时不必改脚本。

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
    [string]$TruthJson = $(if ($env:ITEMSPAWNER_TRUTH_JSON) { $env:ITEMSPAWNER_TRUTH_JSON } else { 'D:\zhuanban\youhua\item_truth.json' }),
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

function Get-MatchCount {
    param([string]$Path, [string]$Pattern)
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    return ([regex]::Matches((Get-Content -LiteralPath $Path -Raw), $Pattern)).Count
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

# ── 1b. 不允许存在 MSBuild .user 覆盖文件 ─────────────────────────────────────
# 上面那道 git 干净门禁有个漏洞：.gitignore 忽略 *.user，而 MSBuild SDK 会自动
# Import 与工程同名的 <Project>.csproj.user。也就是说一个 git status 看不见、
# 不入库的文件可以改变编译结果（实测：注入 DefineConstants 后
# `dotnet msbuild -getProperty:DefineConstants` 多出 INJECTED_FROM_USER_FILE），
# 而 AssemblyInformationalVersion 仍指向不含该改动的提交 —— 与 2.1.0 那次
# SourceLink 失真是同一类事故，只是来源更隐蔽。因此发行时一律禁止。
Write-Step '校验无 MSBuild .user 覆盖文件'
$userFiles = @(
    Get-ChildItem -LiteralPath $SrcRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*.csproj.user' -or $_.Name -like '*.props.user' -or $_.Name -like 'Directory.Build.*.user' }
)
if ($userFiles.Count -gt 0) {
    Add-Failure @"
发现 $($userFiles.Count) 个 MSBuild .user 覆盖文件。它们被 .gitignore 忽略（git status 看不到）
但会被 SDK 自动 Import，可静默改变编译结果，使发行 DLL 与 HEAD 的源码不一致。
发行前请移除或改名：
$([string]::Join([Environment]::NewLine, ($userFiles | ForEach-Object { '  ' + $_.FullName.Substring($SrcRoot.Length).TrimStart('\') })))
"@
} else {
    Write-Ok '无 .csproj.user / .props.user / Directory.Build.*.user'
}

# ── 2. 四处版本号一致 ─────────────────────────────────────────────────────────
Write-Step '校验四处版本号一致'
$versions = [ordered]@{
    'csproj <Version>'       = Get-FirstMatch $Csproj '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>'
    'Plugin.cs [BepInPlugin]' = Get-FirstMatch (Join-Path $SrcRoot 'Plugin.cs') '\[BepInPlugin\(\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*"([0-9]+\.[0-9]+\.[0-9]+)"'
    'manifest.json'          = Get-FirstMatch (Join-Path $PackageDir 'manifest.json') '"version_number"\s*:\s*"([0-9]+\.[0-9]+\.[0-9]+)"'
    'CHANGELOG.md'           = Get-FirstMatch (Join-Path $SrcRoot 'CHANGELOG.md') '(?m)^##\s*\[([0-9]+\.[0-9]+\.[0-9]+)\]'
}
# 上面取的是**第一个** <Version>。当前 csproj 里唯一的 <Version> 就是程序集版本，
# 但 <PackageReference><Version>x</Version></PackageReference> 用的是同一个标签名：
# 哪天加了个位置更靠前的依赖，第一个匹配就变成依赖版本，版本一致性检查会拿错值去比。
# 与其猜上下文，不如断言全文件只有一个匹配 —— 真要加依赖时这里失败，正好提醒收紧正则。
$versionTagCount = Get-MatchCount $Csproj '<Version>\s*[0-9]+\.[0-9]+\.[0-9]+\s*</Version>'
if ($versionTagCount -ne 1) {
    Add-Failure "csproj 里 <Version> 匹配到 $versionTagCount 处（期望恰好 1 处）。可能是新增了带 <Version> 的 PackageReference —— 请把版本号正则收紧到 PropertyGroup 内，否则会读到依赖版本号。"
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
    } elseif (-not (Test-Path -LiteralPath $TruthJson)) {
        # 真值文件不在 git 仓库内，换机器时最容易在这里绊住。给出可操作的三条出路，
        # 而不是让人直接伸手去拿 -SkipCatalogVerify（那会把整道门禁静默拿掉）。
        Add-Failure "找不到真值文件 $TruthJson。请用 -TruthJson <路径>、或设环境变量 ITEMSPAWNER_TRUTH_JSON、或修改 verify_catalog.py 的 DEFAULT_TRUTH。不要用 -SkipCatalogVerify 绕过 —— 那会跳过全部不变量校验且不留痕迹。"
    } else {
        & $py.Source (Join-Path $SrcRoot 'verify_catalog.py') --truth $TruthJson |
            ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -ne 0) { Add-Failure "verify_catalog.py 校验失败（退出码 $LASTEXITCODE）" }
        else { Write-Ok 'ItemCatalog.cs 不变量成立' }
    }
}

# ── 5. 权威副本存在性 + Thunderstore 硬约束 ──────────────────────────────────
Write-Step '校验 src\package\ 权威副本'
foreach ($f in @('manifest.json', 'icon.png')) {
    if (Test-Path -LiteralPath (Join-Path $PackageDir $f)) { Write-Ok "package\$f" }
    else { Add-Failure "缺少 package\$f（dist 的对应文件由它生成，不可反向手工维护）" }
}

# Thunderstore 的这几条限制只在**上传时**才报错，那时 zip 已经打好、tag 已经推了，
# 返工代价远高于在这里查一遍。数值来自 Thunderstore 的包格式要求。
$manifestPath = Join-Path $PackageDir 'manifest.json'
if (Test-Path -LiteralPath $manifestPath) {
    Write-Step '校验 manifest.json 的 Thunderstore 约束'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    # StrictMode 下访问不存在的属性会抛终止性异常，缺字段要当成一条可读的门禁失败而不是崩栈。
    $mfields = @($manifest.PSObject.Properties.Name)
    if ($mfields -notcontains 'name') { Add-Failure 'manifest.json 缺少 name 字段' }
    elseif ($manifest.name -notmatch '^[A-Za-z0-9_]+$') {
        Add-Failure "manifest.name = '$($manifest.name)' 含非法字符（Thunderstore 只允许 [A-Za-z0-9_]）"
    } elseif ($manifest.name.Length -gt 128) {
        Add-Failure "manifest.name 长度 $($manifest.name.Length) > 128"
    } else {
        Write-Ok "name = $($manifest.name)（$($manifest.name.Length) 字符）"
    }
    if ($mfields -notcontains 'description') { Add-Failure 'manifest.json 缺少 description 字段' }
    elseif ($manifest.description.Length -gt 250) {
        Add-Failure "manifest.description 长度 $($manifest.description.Length) > 250（Thunderstore 上限）"
    } else {
        Write-Ok "description $($manifest.description.Length)/250 字符"
    }
    if ($mfields -notcontains 'version_number') { Add-Failure 'manifest.json 缺少 version_number 字段' }
    elseif ($manifest.version_number -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        Add-Failure "manifest.version_number = '$($manifest.version_number)' 不是三段 semver（Thunderstore 强制 major.minor.patch）"
    }
}

$iconPath = Join-Path $PackageDir 'icon.png'
if (Test-Path -LiteralPath $iconPath) {
    Write-Step '校验 icon.png 尺寸（Thunderstore 要求恰好 256×256）'
    # 直接读 PNG 头，避免依赖 System.Drawing（跨平台/无 GDI 环境下不可用）。
    # 布局：8 字节签名 + 4 字节 chunk 长度 + 'IHDR' + 4 字节宽 + 4 字节高，宽高为**大端**。
    $bytes = [System.IO.File]::ReadAllBytes($iconPath)
    $sig = [byte[]]@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    $sigOk = $bytes.Length -ge 24
    if ($sigOk) {
        for ($i = 0; $i -lt 8; $i++) { if ($bytes[$i] -ne $sig[$i]) { $sigOk = $false; break } }
    }
    if (-not $sigOk -or [System.Text.Encoding]::ASCII.GetString($bytes, 12, 4) -ne 'IHDR') {
        Add-Failure 'package\icon.png 不是合法 PNG（签名或 IHDR chunk 不匹配）；Thunderstore 只接受 PNG'
    } else {
        $w = [int]$bytes[16] * 16777216 + [int]$bytes[17] * 65536 + [int]$bytes[18] * 256 + [int]$bytes[19]
        $h = [int]$bytes[20] * 16777216 + [int]$bytes[21] * 65536 + [int]$bytes[22] * 256 + [int]$bytes[23]
        if ($w -ne 256 -or $h -ne 256) {
            Add-Failure "package\icon.png 尺寸 ${w}×${h}，Thunderstore 要求恰好 256×256（上传时才报错，故在此拦下）"
        } else {
            Write-Ok "icon.png = ${w}×${h} PNG"
        }
    }
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

# ── 7b. dist 文件集合必须恰好等于预期清单 ────────────────────────────────────
# 上面的拷贝是**单向覆盖**：只写不删。某版新增的文档在下一版删掉后，dist 里的旧副本
# 会一直留着，并被 `Compress-Archive -Path $DistRoot\*` 原封不动打进发行 zip（玩家拿到
# 一个不属于本版本的文件）。这里不做 Remove-Item -Recurse（用户明确要求不对 dist 做
# 破坏性删除），改用集合相等断言：多一个文件就失败并列出来，由人决定怎么处理。
Write-Step '校验 dist 文件集合'
$expectedDist = @(
    'plugins\ItemSpawnerPremium.dll',
    'manifest.json',
    'icon.png',
    'README.md',
    'CHANGELOG.md',
    'LICENSE',
    'THIRD_PARTY_NOTICES.md'
)
$actualDist = @(Get-ChildItem -LiteralPath $DistRoot -Recurse -File -Force |
    ForEach-Object { $_.FullName.Substring($DistRoot.Length).TrimStart('\') })
$unexpected = @($actualDist | Where-Object { $expectedDist -notcontains $_ })
$absent = @($expectedDist | Where-Object { $actualDist -notcontains $_ })
if ($unexpected.Count -gt 0 -or $absent.Count -gt 0) {
    Write-Host ''
    if ($unexpected.Count -gt 0) {
        Write-Host "dist 存在 $($unexpected.Count) 个非预期文件（很可能是旧版本遗留，会被打进 zip）：" -ForegroundColor Red
        $unexpected | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }
    if ($absent.Count -gt 0) {
        Write-Host "dist 缺少 $($absent.Count) 个预期文件：" -ForegroundColor Red
        $absent | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }
    Write-Host '请人工确认后删除多余文件（脚本故意不自动删 dist），然后重跑。未生成 zip。' -ForegroundColor Red
    exit 1
}
Write-Ok "dist 文件集合与预期一致（$($expectedDist.Count) 项）"

Write-Step '产物清单与 SHA256'
Get-ChildItem -LiteralPath $DistRoot -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($DistRoot.Length).TrimStart('\')
    '    {0,-40} {1,9}  {2}' -f $rel, $_.Length, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}

# ── 7c. 游戏版本溯源 ─────────────────────────────────────────────────────────
# Deterministic + ContinuousIntegrationBuild 只保证「同一提交 + 同一引用集」字节可复现。
# 引用集来自 $(GameDir)\PEAK_Data\Managed\*，那些程序集版本号全是 0.0.0.0 且无强签名，
# 不会留在产物里。PEAK 一更新，同一个提交就可能编出不同的 DLL 而事后无从判别。
# 所以把游戏版本与 Assembly-CSharp.dll 的哈希打进发行记录（缺失只警告 —— 这是溯源
# 信息，不是正确性前提，不该因此挡住发布）。
Write-Step '游戏引用集溯源（PEAK 版本 / Assembly-CSharp.dll 哈希）'
$gameDir = $env:GameDir
if ([string]::IsNullOrWhiteSpace($gameDir)) {
    # 与 Directory.Build.props 的默认值保持单一来源：直接读它，避免两处各写一份路径。
    $gameDir = Get-FirstMatch (Join-Path $SrcRoot 'Directory.Build.props') "<GameDir\s+Condition=[^>]*>([^<]+)</GameDir>"
}
if ([string]::IsNullOrWhiteSpace($gameDir)) {
    Write-Warn2 '无法确定 GameDir，跳过游戏版本溯源'
} else {
    Write-Host "    GameDir = $gameDir"
    $verTxt = Join-Path $gameDir 'version.txt'
    if (Test-Path -LiteralPath $verTxt) {
        (Get-Content -LiteralPath $verTxt) | Where-Object { $_ } | ForEach-Object { Write-Host "    version.txt: $_" }
    } else {
        Write-Warn2 "找不到 $verTxt，发行记录里将缺少 PEAK 版本号"
    }
    $gameAsm = Join-Path $gameDir 'PEAK_Data\Managed\Assembly-CSharp.dll'
    if (Test-Path -LiteralPath $gameAsm) {
        Write-Host "    Assembly-CSharp.dll SHA256 = $((Get-FileHash -LiteralPath $gameAsm -Algorithm SHA256).Hash)"
    } else {
        Write-Warn2 "找不到 $gameAsm，无法记录引用集哈希"
    }
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
