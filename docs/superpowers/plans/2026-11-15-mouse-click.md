# 鼠标连点器实现计划

> 按小阶段实现、验证、commit、push；保持记录，避免重复工作。

**Goal:** 交付含多坐标点选循环的 Windows 中文连点器及源码。

**Architecture:** 核心执行器与桌面输入分离，UI 管理设置和运行生命周期，原生适配器处理实际鼠标与热键。

**Tech Stack:** C#、.NET 10、Windows Forms、Windows SendInput/RegisterHotKey。

**Spec:** [功能设计](../specs/2026-11-15-mouse-click-design.md)

## 全局约束

- 中文 UI、注释和提交信息；新源码 v1.0、日期 2026/11/15。
- 源码、测试、文档和构建产物分目录。
- 不读取用户禁止的内容，不修改其他软件配置。
- 每个完成的小阶段立即 commit 和 push，检查工作树及暂存差异。

## 接口

- `ClickSettings`：`Button`、`Kind`、`IntervalMilliseconds`、`StartDelayMilliseconds`、`RepeatCount`（0 无限）、`Mode`、`FixedPoint`、`Points`、`ToggleHotkey`。
- `ClickPoint(int X, int Y)`；`MouseButton`：Left/Right/Middle；`ClickKind`：Single/Double；`TargetMode`：Cursor/Fixed/Sequence。
- `IClickOutput.Click(MouseButton button, ClickKind kind, ClickPoint? point)`：null 表示当前鼠标。
- `ClickEngine(IClickOutput output)`；`RunAsync(ClickSettings settings, CancellationToken token)`；`CompletedClicks`、`CompletedRounds`、`IsRunning`。
- `SettingsStore(string path)`；`Load()` 和 `Save(ClickSettings settings)`。

## 阶段

- [x] 1. 确认环境、仓库和范围，提交并推送文档。
- [x] 2. 建立 Core 和无第三方依赖的行为测试：先验证缺失实现失败，再实现参数校验、多点顺序、轮数、延时、取消、错误传播、配置读写。`dotnet run --project tests/MouseClick.Tests -c Release`：19/19 通过。
- [x] 3. 建立 Windows 输入和全局热键适配器，校验原生结构与错误处理，提交并推送。F8 冲突时回退 Ctrl+Alt+F8。
- [x] 4. 实现主窗口、坐标列表与点选层、设置保存和运行状态；Release 构建 0 警告、0 错误，提交并推送。
- [x] 5. 独立代码审查，修复点选层提前关闭问题（4/4 回归）；发布自包含 win-x64 EXE，完善 README 和验收记录，提交并推送。
- [ ] 6. 完成真实桌面输入全套验收：测试窗口命中保护检测到外部窗口遮挡后中止；尚未取得完整通过结果。详见验收记录。

## 检查重点

- 多坐标限定轮数不得提前结束或多点一次。
- 停止延时中的运行必须立即取消；停止后允许再次启动。
- 快捷键冲突、坏配置、输入失败必须向用户报告。
- 支持负坐标；屏幕变化后拒绝屏幕外的固定坐标。
- 关闭窗口、重新启停、取点期间不得遗留点击任务。
