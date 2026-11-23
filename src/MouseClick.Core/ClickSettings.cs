// 文件：ClickSettings.cs
// 说明：定义点击设置、坐标与参数校验。
// 版本：v1.3
// 日期：2026/11/23
// 历史：v1.3 2026/11/23 新增 F8 并统一界面、配置校验与注册使用的启停键列表。
// v1.2 2026/11/16 新增关闭行为偏好，旧配置默认询问并拒绝非法枚举。
// v1.1 2026/11/15 校验提示采用秒，保持存储字段及数值边界不变。

namespace MouseClick.Core;

// 2026/11/15 新增：使用屏幕坐标表示固定位置与顺序点击位置，允许负坐标。
public sealed record ClickPoint(int X, int Y);

// 2026/11/15 新增：定义原生鼠标动作与目标选择方式。
public enum MouseButton { Left, Right, Middle }
public enum ClickKind { Single, Double }
public enum TargetMode { Cursor, Fixed, Sequence }

// 2026/11/16 新增：为 MainForm 关闭流程与 SettingsStore 持久化提供统一的关闭行为选项。
public enum WindowCloseBehavior { Ask, MinimizeToTray, Exit }

// 2026/11/15 新增：集中定义可保存的设置和合法输入范围。
public sealed record ClickSettings
{
    // 2026/11/23 新增：保存值为 Windows 虚拟键，MainForm 与 HotkeyWindow 共用同一组选项。
    public static ReadOnlySpan<int> ToggleVirtualKeys => [0x75, 0x77, 0x78, 0x79];

    public MouseButton Button { get; init; } = MouseButton.Left;
    public ClickKind Kind { get; init; } = ClickKind.Single;
    public int IntervalMilliseconds { get; init; } = 100;
    public int StartDelayMilliseconds { get; init; } = 1000;
    public int RepeatCount { get; init; }
    public TargetMode Mode { get; init; } = TargetMode.Cursor;
    public ClickPoint FixedPoint { get; init; } = new(0, 0);
    public IReadOnlyList<ClickPoint> Points { get; init; } = Array.Empty<ClickPoint>();
    public int ToggleHotkey { get; init; } = 0x75;
    // 2026/11/16 新增：旧配置未保存此字段时默认询问，记住偏好由主窗口选择后保存。
    public WindowCloseBehavior CloseBehavior { get; init; } = WindowCloseBehavior.Ask;

    // 2026/11/15 新增：在执行和保存边界拒绝不合法设置。
    public void Validate()
    {
        if (!Enum.IsDefined(Button)) throw new ArgumentException("鼠标按键无效。", nameof(Button));
        if (!Enum.IsDefined(Kind)) throw new ArgumentException("点击类型无效。", nameof(Kind));
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("目标模式无效。", nameof(Mode));
        // 2026/11/16 新增：在加载、保存及执行边界拒绝未定义的关闭行为。
        if (!Enum.IsDefined(CloseBehavior)) throw new ArgumentException("关闭行为无效。", nameof(CloseBehavior));
        // 2026/11/15 修改：供界面显示的时间错误使用秒单位，内部调用契约仍为整数毫秒。
        if (IntervalMilliseconds is < 1 or > 86400000)
            throw new ArgumentException("点击间隔必须为 0.001～86400 s。", nameof(IntervalMilliseconds));
        if (StartDelayMilliseconds is < 0 or > 3600000)
            throw new ArgumentException("启动延时必须为 0～3600 s。", nameof(StartDelayMilliseconds));
        if (RepeatCount is < 0 or > 1000000)
            throw new ArgumentException("轮数必须为 0～1000000，0 表示持续运行。", nameof(RepeatCount));
        // 2026/11/23 修改：允许将单独 F8 保存为启停键。
        if (!ToggleVirtualKeys.Contains(ToggleHotkey))
            throw new ArgumentException("启停快捷键只能选择 F6、F8、F9 或 F10。", nameof(ToggleHotkey));
        if (FixedPoint is null) throw new ArgumentException("固定坐标不能为空。", nameof(FixedPoint));
        if (Points is null) throw new ArgumentException("坐标列表不能为空。", nameof(Points));
        if (Mode == TargetMode.Sequence && Points.Count == 0)
            throw new ArgumentException("顺序模式至少需要一个坐标。", nameof(Points));
        if (Points.Any(point => point is null))
            throw new ArgumentException("坐标列表不能包含空坐标。", nameof(Points));
    }
}
