# HQY 鼠标连点器

Windows 中文鼠标连点软件，使用 C# / .NET 10 / Windows Forms。

## 第一版范围

- 左键、右键、中键；单击、双击。
- 设置点击间隔、启动延时、指定轮数或持续运行。
- 跟随鼠标、固定坐标、多坐标顺序循环。
- 在屏幕上点击选取坐标，添加任意数量的位置，调整顺序或移除。
- 全局快捷键启停、紧急停止、运行计数、保存设置。
- 提供 Windows x64 免安装 EXE。

多位置模式按列表顺序点击，完成整个列表计为一轮；双击计为一次动作。

## 开发进度

功能设计已建立，按小阶段验证、commit 并 push。
详见 [设计](docs/superpowers/specs/2026-11-15-mouse-click-design.md) 和 [实现计划](docs/superpowers/plans/2026-11-15-mouse-click.md)。
