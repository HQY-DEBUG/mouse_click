# 文件：verify.ps1
# 描述：构建桌面程序并执行核心行为验证。
# 版本：v1.0
# 日期：2026/11/15
# 历史：v1.0 2026/11/15 新增可重复验证入口。
param([switch]$Desktop)
$ErrorActionPreference = 'Stop'

# 2026/11/15 新增：从脚本所在目录定位仓库，所有外部程序失败均终止验证。
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    dotnet run --project tests/MouseClick.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw '核心行为测试失败。' }
    dotnet build src/MouseClick.App -c Release
    if ($LASTEXITCODE -ne 0) { throw '桌面程序构建失败。' }
    if ($Desktop) {
        dotnet run --project tests/MouseClick.WindowsTests -c Release -- --desktop
        if ($LASTEXITCODE -ne 0) { throw 'Windows 桌面输入测试失败。' }
    }
    else {
        dotnet run --project tests/MouseClick.WindowsTests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Windows 边界与点选回归测试失败。' }
    }
    Write-Host '验证通过。'
}
finally {
    Pop-Location
}
