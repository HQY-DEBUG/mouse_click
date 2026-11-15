# 文件：publish.ps1
# 描述：发布不依赖已安装 .NET 的 Windows x64 单文件程序。
# 版本：v1.2
# 日期：2026/11/15
# 历史：v1.2 2026/11/15 允许发行脚本提供独立输出目录与源码提交标识。
# v1.1 2026/11/15 同步钰宇鼠标连点器发布名称。
# v1.0 2026/11/15 新增自包含发布入口。
# 2026/11/15 新增：发行构建输出到独立暂存目录，不占用已运行的本机程序。
param([string]$OutputDirectory, [string]$SourceRevisionId)
$ErrorActionPreference = 'Stop'

# 2026/11/15 新增：发布产物集中存放，避免把二进制或中间文件加入源码提交。
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts\windows-x64' }
$revisionArguments = @()
if ($SourceRevisionId) {
    if ($SourceRevisionId -notmatch '^[0-9a-f]{40}$') { throw '源码提交标识必须为完整 Git SHA。' }
    $revisionArguments = @("-p:SourceRevisionId=$SourceRevisionId")
}
Push-Location -LiteralPath $repoRoot
try {
    dotnet publish src/MouseClick.App -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -o $publishDir @revisionArguments
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    # 2026/11/15 修改：发布结果采用用户指定的软件名称。
    Write-Host (Join-Path $publishDir '钰宇鼠标连点器.exe')
}
finally {
    Pop-Location
}
