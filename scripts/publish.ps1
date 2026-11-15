# 文件：publish.ps1
# 描述：发布不依赖已安装 .NET 的 Windows x64 单文件程序。
# 版本：v1.0
# 日期：2026/11/15
# 历史：v1.0 2026/11/15 新增自包含发布入口。
$ErrorActionPreference = 'Stop'

# 2026/11/15 新增：发布产物集中存放，避免把二进制或中间文件加入源码提交。
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'artifacts\windows-x64'
Push-Location -LiteralPath $repoRoot
try {
    dotnet publish src/MouseClick.App -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    Write-Host (Join-Path $publishDir 'HQY.MouseClick.exe')
}
finally {
    Pop-Location
}
