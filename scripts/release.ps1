# 文件：release.ps1
# 描述：从干净提交生成安装版、便携版与校验清单，不覆盖既有发行文件。
# 版本：v1.1
# 日期：2026/11/15
# 历史：v1.1 2026/11/15 按 PE 数字版本校验，避免安装器版本文本填充导致误报。
# v1.0 2026/11/15 建立可追溯的双版本发行构建。
param(
    [Parameter(Mandatory = $true)][string]$IsccPath,
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# 2026/11/15 新增：Inno 的版本文本带填充空格，读取 PE 四段数字字段进行精确校验。
function Get-NumericFileVersion([string]$Path) {
    $info = (Get-Item -LiteralPath $Path).VersionInfo
    return '{0}.{1}.{2}.{3}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
}

# 2026/11/15 新增：版本只从 MSBuild 共用文件读取，并校验发行所需文档和工具。
$repoRoot = Split-Path -Parent $PSScriptRoot
[xml]$versionDocument = Get-Content -LiteralPath (Join-Path $repoRoot 'version.props') -Raw
$version = [string]$versionDocument.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'version.props 必须提供三段数字正式版本。' }
$compiler = (Resolve-Path -LiteralPath $IsccPath).Path
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw '找不到 Inno Setup ISCC.exe。' }
$releaseNotes = Join-Path $repoRoot "docs\releases\v$version.md"
foreach ($file in @($releaseNotes, (Join-Path $repoRoot 'CHANGELOG.md'), (Join-Path $repoRoot 'README.md'))) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "发行文档缺失：$file" }
}
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts\releases' }
$releaseDir = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "v$version"
if (Test-Path -LiteralPath $releaseDir) { throw "发行目录已存在，不会覆盖：$releaseDir。请使用新的 -OutputRoot 或新版本。" }

Push-Location -LiteralPath $repoRoot
try {
    # 2026/11/15 新增：拒绝未提交内容，防止发布清单所指提交与包内代码不一致。
    $status = @(git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw '读取 Git 状态失败。' }
    if ($status.Count -ne 0) { throw '请先提交全部发行相关改动，再生成正式发行包。' }
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw '读取源码提交失败。' }
    $sdk = (dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw '读取 .NET SDK 版本失败。' }
    $workDir = Join-Path $repoRoot ('artifacts\package-work\' + [Guid]::NewGuid().ToString('N'))
    $payload = Join-Path $workDir 'payload'
    $portable = Join-Path $workDir "YuyuMouseClick-v$version-win-x64-portable"
    New-Item -ItemType Directory -Path $payload, $portable, $releaseDir | Out-Null

    # 2026/11/15 新增：同一份已发布 EXE 进入两个包，避免构建内容漂移。
    & (Join-Path $PSScriptRoot 'publish.ps1') -OutputDirectory $payload -SourceRevisionId $commit
    $executable = Join-Path $payload '钰宇鼠标连点器.exe'
    $fileVersion = Get-NumericFileVersion $executable
    if ($fileVersion -ne "$version.0") { throw "程序文件版本与发行版本不一致：$fileVersion" }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $payload '使用说明.md')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') -Destination (Join-Path $payload '更新日志.md')
    $payloadNames = @('钰宇鼠标连点器.exe', '图标许可.txt', '图标来源.md', '使用说明.md', '更新日志.md')
    foreach ($name in $payloadNames) {
        Copy-Item -LiteralPath (Join-Path $payload $name) -Destination (Join-Path $portable $name)
    }
    [IO.File]::WriteAllText((Join-Path $portable 'portable.flag'), "便携版标记：配置保存在同目录 data/settings.json。`r`n", [Text.UTF8Encoding]::new($false))

    # 2026/11/15 新增：安装脚本强制编译器版本；压缩包只包含允许分发的文件。
    & $compiler "/DAppVersion=$version" "/DSourceDir=$payload" "/DOutputDir=$releaseDir" (Join-Path $repoRoot 'packaging\windows\installer.iss')
    if ($LASTEXITCODE -ne 0) { throw '安装包编译失败，已保留暂存目录供排查。' }
    $zipPath = Join-Path $releaseDir "YuyuMouseClick-v$version-win-x64-portable.zip"
    Compress-Archive -LiteralPath $portable -DestinationPath $zipPath -CompressionLevel Optimal
    $setupPath = Join-Path $releaseDir "YuyuMouseClick-v$version-win-x64-setup.exe"
    if ((Get-NumericFileVersion $setupPath) -ne "$version.0") {
        throw '安装包文件版本与发行版本不一致。'
    }

    # 2026/11/15 新增：清单记录源码、工具链和附件摘要；SHA256SUMS 同时覆盖清单自身。
    $assetRecords = @()
    foreach ($assetPath in @($setupPath, $zipPath)) {
        $asset = Get-Item -LiteralPath $assetPath
        $assetRecords += [ordered]@{
            name = $asset.Name
            size = $asset.Length
            sha256 = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        product = '钰宇鼠标连点器'
        version = $version
        tag = "v$version"
        repository = 'https://github.com/HQY-DEBUG/mouse_click'
        sourceCommit = $commit
        builtAtUtc = [DateTime]::UtcNow.ToString('o')
        runtime = 'win-x64'
        selfContained = $true
        dotnetSdk = $sdk
        innoSetup = '6.7.4'
        executableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
        assets = $assetRecords
    }
    $manifestPath = Join-Path $releaseDir 'release-manifest.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    $checksumLines = @($assetRecords | ForEach-Object { "$($_.sha256)  $($_.name)" })
    $checksumLines += "$((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant())  release-manifest.json"
    [IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), $checksumLines, [Text.UTF8Encoding]::new($false))
    Write-Host "发行构建完成：$releaseDir"
    Write-Host "源码提交：$commit"
    Get-ChildItem -LiteralPath $releaseDir | Select-Object Name, Length
}
finally {
    Pop-Location
}
