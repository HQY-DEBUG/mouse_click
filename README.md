# HQY 鼠标连点器

Windows 中文鼠标连点软件，使用 C# / .NET 10 / Windows Forms，无第三方运行依赖。

## 功能

- 左键、右键、中键；单击、双击。
- 设置点击间隔、启动延时、指定轮数或持续运行。
- 跟随鼠标、固定坐标、多坐标顺序循环。
- 在屏幕上点击选取坐标，添加任意数量的位置，调整顺序或移除。
- 全局快捷键启停、紧急停止、运行计数、自动保存设置。
- 提供 Windows x64 免安装 EXE。

多位置模式按列表顺序点击，完成整个列表计为一轮；双击计为一次动作。

## 使用

双击本机发布产物 `artifacts/windows-x64/HQY.MouseClick.exe`，不需要另外安装 .NET。

1. 设置按键、单击/双击、点击间隔和启动延时，时间单位均为毫秒。
2. 选择点击位置模式。多位置模式点击“屏幕点选”，在目标位置依次左键点击；右键或 Esc 完成选择。
3. 列表每一行就是一个位置，添加多少行就循环多少个位置。可选中行上移、下移、移除，也可在 X/Y 输入坐标后点击“添加坐标”。
4. 设置重复轮数；`0` 为持续运行。点击“开始连点”或按全局快捷键启动。
5. 按紧急停止快捷键或点击“停止”结束。关闭窗口也会先停止任务。

例如列表为 `(1,1)`、`(2,3)`、`(3,4)`，轮数为 `2`，实际顺序为 `1→2→3→1→2→3`，共 6 次动作、2 轮。请用屏幕点选取得实际目标坐标。

| 操作 | 快捷键 |
| --- | --- |
| 开始 / 停止 | 默认 F6，可选择 F9 或 F10 |
| 屏幕取点 | F7 |
| 紧急停止 | 默认 F8；被占用时自动尝试 Ctrl+Alt+F8，界面显示实际按键 |
| 完成多点选择 | 鼠标右键 / Esc |

快捷键冲突会显示原因；两个紧急停止组合均不可用时禁止启动。点击间隔范围 1～86400000 毫秒，实际节奏受 Windows 调度影响，不承诺 1 毫秒精度。

设置保存在 `%LOCALAPPDATA%\HQY.MouseClick\settings.json`，不会覆盖鼠大侠或其他连点器配置。损坏配置读取时会提示；再次保存前自动保留 `.invalid-*.bak` 备份。

## 构建与验证

环境：Windows 10/11 x64，.NET 10 SDK（`global.json` 固定基线 10.0.204，允许同主版本更新功能带）。

```powershell
# 编译、核心测试、点选回归和输入边界测试；不产生桌面点击
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1

# 在专用测试窗口验证真实点击；约数秒，请暂时不要移动鼠标
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1 -Desktop

# 生成免安装自包含单文件 EXE
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

源码分为 `src/MouseClick.Core` 与 `src/MouseClick.App`，行为测试位于 `tests`，产物统一放在被 Git 忽略的 `artifacts`。源码仓库不包含大型 .NET 自包含二进制，执行发布脚本即可生成。

目标程序若以管理员身份运行，连点器可能也需要相同权限；是否接收模拟输入由目标程序决定。本工具提供桌面前台点击，不提供后台点击或操作录制。Windows 输入与热键实现参考微软 [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) 和 [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)。

## 开发记录

按小阶段验证、commit 并 push。详见 [设计](docs/superpowers/specs/2026-11-15-mouse-click-design.md)、[实现计划](docs/superpowers/plans/2026-11-15-mouse-click.md) 和 Git 提交历史。
