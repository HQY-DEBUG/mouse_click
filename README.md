# 钰宇鼠标连点器

Windows 中文鼠标连点软件，使用 C# / .NET 10 / Windows Forms。支持 Windows 10/11 x64，发行包自带 .NET 运行时，无需另外安装 .NET。

## 下载

本版：**v1.3.2**。下载入口：[GitHub Release](https://github.com/HQY-DEBUG/mouse_click/releases/tag/v1.3.2)。

| 发行文件 | 用途 |
| --- | --- |
| [YuyuMouseClick-v1.3.2-win-x64-setup.exe](https://github.com/HQY-DEBUG/mouse_click/releases/download/v1.3.2/YuyuMouseClick-v1.3.2-win-x64-setup.exe) | 安装版，提供开始菜单入口、可选桌面快捷方式及 Windows 卸载入口 |
| [YuyuMouseClick-v1.3.2-win-x64-portable.zip](https://github.com/HQY-DEBUG/mouse_click/releases/download/v1.3.2/YuyuMouseClick-v1.3.2-win-x64-portable.zip) | 便携版，解压到可写目录后直接运行 |
| [SHA256SUMS.txt](https://github.com/HQY-DEBUG/mouse_click/releases/download/v1.3.2/SHA256SUMS.txt) | 下载文件的 SHA-256 校验值 |
| [release-manifest.json](https://github.com/HQY-DEBUG/mouse_click/releases/download/v1.3.2/release-manifest.json) | 版本、源码提交及发行产物信息 |

发行包尚未数字签名。Windows 可能显示未知发布者或 SmartScreen 提示；请核对下载来源及校验值。详见 [v1.3.2 发布说明](https://github.com/HQY-DEBUG/mouse_click/blob/v1.3.2/docs/releases/v1.3.2.md) 和 [版本记录](https://github.com/HQY-DEBUG/mouse_click/blob/v1.3.2/CHANGELOG.md)。

## 功能

- 左键、右键、中键；单击、双击。
- 鼠标按下后保持 50 ms 再抬起，提高需要持续按住的目标程序兼容性；停止时释放已按下按钮。
- 设置点击间隔、启动延时、指定轮数或持续运行。
- 跟随鼠标、固定坐标、多坐标顺序循环。
- 在屏幕上点击选取坐标，添加任意数量的位置，调整顺序或移除。
- 全局快捷键启停、紧急停止、运行计数、自动保存设置。
- 启动成功后自动最小化到托盘，启动失败时保留或恢复主窗口并显示具体原因。
- 关闭窗口时选择最小化到托盘或退出，可记住选择并随时在设置中修改。
- 托盘菜单支持恢复窗口、开始或停止连点、打开设置及退出程序。
- 提供 Windows x64 安装版和便携版。

多位置模式按列表顺序点击，完成整个列表计为一轮；双击计为一次动作。

## 使用

安装版运行 `YuyuMouseClick-v1.3.2-win-x64-setup.exe`，按中文向导完成安装，再从开始菜单启动。默认仅安装到当前用户的 `%LOCALAPPDATA%\Programs\YuyuMouseClick`。

便携版先完整解压 ZIP，再双击其中的 `钰宇鼠标连点器.exe`；请保留同目录的 `portable.flag`，不要直接从 ZIP 预览中启动。

1. 设置按键、单击/双击、点击间隔和启动延时，时间单位均为秒（s），支持三位小数，例如 0.5、1.25、0.001。
2. 选择点击位置模式。多位置模式点击“屏幕点选”，在目标位置依次左键点击；右键或 Esc 完成选择。
3. 列表每一行就是一个位置，添加多少行就循环多少个位置。可选中行上移、下移、移除，也可在 X/Y 输入坐标后点击“添加坐标”。
4. 设置重复轮数；`0` 为持续运行。点击“开始连点”或按当前全局快捷键（默认 F6，可选 F9/F10）启动；启动成功后主窗口自动最小化到托盘。托盘菜单启动采用相同行为。
5. 按紧急停止快捷键或点击“停止”结束。点击主窗口 × 时，默认询问“最小化到托盘”或“退出”；也可取消关闭。最小化到托盘后继续运行，退出会先停止连点。

例如列表为 `(1,1)`、`(2,3)`、`(3,4)`，轮数为 `2`，实际顺序为 `1→2→3→1→2→3`，共 6 次动作、2 轮。请用屏幕点选取得实际目标坐标。

| 操作 | 快捷键 |
| --- | --- |
| 开始 / 停止 | 默认 F6，可选择 F9 或 F10 |
| 屏幕取点 | F7 |
| 紧急停止 | 固定 Ctrl+F8；被占用时禁止启动，不会自动更换快捷键 |
| 完成多点选择 | 鼠标右键 / Esc |

快捷键冲突会显示原因；Ctrl+F8 不可用时禁止启动，释放后重新选择开始/停止快捷键以重试注册。点击间隔范围 0.001～86400 s，启动延时范围 0～3600 s；超过三位小数按三位四舍五入，步进按钮每次调整 0.1 s。实际节奏受 Windows 调度影响，不承诺毫秒级实时精度。旧配置保持实际时长不变，例如原 5000 毫秒显示为 5.000 s。

单击按住 50 ms；双击由两次 50 ms 按住组成，中间释放 20 ms。**点击间隔仍是在一次动作完成后等待的时长**，不是相邻动作起点之间的总时间。例如设置 3 s，连续单击的起点间隔约为 3.05 s，双击约为 3.12 s，另有系统调度开销。停止可取消按住或等待，已按下的按钮会抬起，不补发额外单击。

启停键和 Ctrl+F8 增加按键状态检测，并与原生热键消息去重；仍须成功注册相应快捷键，不绕过占用冲突。F7 取点继续使用原生热键。

启动失败时，程序会保留或恢复主窗口并显示具体原因；例如停止热键不可用、坐标不在当前屏幕内或设置无法保存。已经进入托盘时，也可按当前全局启停快捷键启动、停止或重新开始连点，无需先恢复窗口。

关闭提示可勾选“记住我的选择（可在设置中修改）”。主窗口或托盘菜单中的“设置”提供“每次询问”“最小化到系统托盘”“直接退出”三个选项；已有配置升级后默认每次询问。最小化到托盘后，双击图标或选择菜单“显示主窗口”即可恢复；也可通过菜单开始或停止连点、打开设置、退出程序。全局启停和 **Ctrl+F8 紧急停止仍然有效**，托盘菜单“退出”会停止连点并真正结束程序。

安装版设置保存在 `%LOCALAPPDATA%\HQY.MouseClick\settings.json`，卸载保留此配置。便携版通过 EXE 同目录的 `portable.flag` 启用，设置保存在同目录下的 `data\settings.json`；移动便携版时请连同 `data` 目录一起移动。两种模式使用各自的配置，不会自动迁移或共享，也不会覆盖鼠大侠或其他连点器配置。损坏配置读取时会提示；再次保存前自动保留 `.invalid-*.bak` 备份。

## 构建与验证

环境：Windows 10/11 x64，.NET 10 SDK（`global.json` 固定基线 10.0.204，允许同主版本更新功能带）。完整发行打包还需要 Inno Setup 6.7.4。版本统一由根目录 `version.props` 中的 `Version` 管理。

```powershell
# 编译、核心测试、点选回归和输入边界测试；不产生桌面点击
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1

# 在专用窗口验证真实点击和 F6/Ctrl+F8；请暂时不要操作鼠标键盘或切换窗口
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1 -Desktop

# 真实窗体和 Ctrl+F8 验证托盘生命周期；使用计数替身，不产生鼠标点击
dotnet run --project tests/MouseClick.WindowsTests -c Release -- --tray-desktop

# 生成自包含单文件 EXE，输出 artifacts/windows-x64
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1

# 生成正式安装版、便携 ZIP、校验文件及源码版本清单
powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

源码分为 `src/MouseClick.Core` 与 `src/MouseClick.App`，行为测试位于 `tests`，Windows 安装脚本位于 `packaging/windows`，产物统一放在被 Git 忽略的 `artifacts`。正式发行输出到 `artifacts/releases/v1.3.2/`；同版本输出目录已存在时拒绝覆盖，可用 `-OutputRoot` 指定其他输出根目录。源码仓库不包含大型 .NET 自包含二进制；下载成品请使用上方 Release 入口。完整发布步骤见 [发行维护指南](https://github.com/HQY-DEBUG/mouse_click/blob/v1.3.2/docs/releasing.md)。

v1.3.2 已通过构建、25 项核心测试、默认 Windows 回归，以及四窗口输入保持、双击取消、托盘长间隔停止、热键兼容性和完整桌面验收，详见[本版验证记录](https://github.com/HQY-DEBUG/mouse_click/blob/main/docs/releases/validation-v1.3.2.md)。本轮针对用户四坐标、3 s 间隔的反馈排查；自有窗口结果不代表四个游戏的业务操作均已验证，事实与限制见[输入兼容性诊断](https://github.com/HQY-DEBUG/mouse_click/blob/main/docs/diagnostics/2026-11-16-input-compatibility.md)。

此前版本的验收见[v1.3.1 验证记录](https://github.com/HQY-DEBUG/mouse_click/blob/main/docs/releases/validation-v1.3.1.md)、[v1.3.0 验证记录](https://github.com/HQY-DEBUG/mouse_click/blob/main/docs/releases/validation-v1.3.0.md)和[早期桌面输入记录](https://github.com/HQY-DEBUG/mouse_click/blob/v1.3.0/docs/validation-2026-11-15.md)，不视为 v1.3.2 重复执行的证据。测试使用独立配置，不覆盖用户设置。

每次按下前会检查前台窗口和目标窗口的权限；发现权限高于连点器时停止并提示处理方式。请先退出连点器，再按提示右键选择“以管理员身份运行”，或将目标程序改为普通权限；软件不会自动提权。是否接收模拟输入仍由目标程序决定。**托盘后台驻留仍会产生实际桌面鼠标输入**，本工具不提供后台窗口点击或操作录制。Windows 输入与热键实现参考微软 [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) 和 [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)。

## 开发记录

程序图标使用选定的蓝紫双色指针：[SVG Repo 原图（CC0）](https://www.svgrepo.com/svg/184232/clicker-mouse-clicker)。原始 SVG、许可和转换记录保存在 [assets/icons](https://github.com/HQY-DEBUG/mouse_click/blob/v1.3.2/assets/icons/SOURCE.md)。

按小阶段验证、commit 并 push。详见 [设计](https://github.com/HQY-DEBUG/mouse_click/blob/v1.3.2/docs/superpowers/specs/2026-11-15-mouse-click-design.md)、[实现计划](https://github.com/HQY-DEBUG/mouse_click/blob/v1.3.2/docs/superpowers/plans/2026-11-15-mouse-click.md) 和 Git 提交历史。
