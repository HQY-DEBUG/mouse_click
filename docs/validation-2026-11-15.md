# 2026/11/15 验证记录

## 已完成

- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1` 退出码 0。
- 核心行为测试 19/19 通过：多点顺序与轮数、单双击动作计数、取消及重新启动、并发拒绝、错误传播、运行快照、间隔、设置校验、JSON 保存与损坏备份。
- 点选回归 4/4 通过。修复前固定点左键、固定点右键、多点右键在按下时提前关闭，回归仅 1/4 通过；修改为收到对应抬起事件后关闭，四项通过。
- 原生输入边界测试通过，非法按键、非法点击类型与屏幕外坐标在发送前被拒绝。
- Release 构建 0 警告、0 错误。
- 自包含单文件发布成功：`artifacts/windows-x64/HQY.MouseClick.exe`。
- 主窗口已通过截图检查，布局、中文控件、输入框、列表、按钮及热键冲突提示可见。
- 独立静态代码审查发现的点选层 MouseUp 透传风险已修复。
- Windows PowerShell 5.1 读取无 BOM 中文脚本发生解析错误；发布与验证脚本改为 UTF-8 BOM 后，两个入口均运行成功。

## 尚未验证完成

`--desktop` 真实点击全套验收未通过。专用窗口在发送前使用 `WindowFromPoint` 检查坐标归属，发现目标被其他窗口遮挡后中止；没有为通过测试而移除该检查。因此不把程序编译成功或 SendInput 实现完成当成桌面端到端验证成功。

后续发布版界面控制又遇到桌面工具的窗口归属映射错误（返回“window id ... no longer belongs ...”，前后应用路径相同），未继续使用失效窗口绑定。未验证所有显示器缩放组合、特定第三方应用或游戏是否接收点击。

可在可交互且无遮挡的桌面重新执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1 -Desktop
```

测试只点击自己创建的三个目标，检查真实 down/up/doubleclick 消息、三点两轮顺序、精确像素落点和热键释放；结束关闭测试窗口并恢复光标。
