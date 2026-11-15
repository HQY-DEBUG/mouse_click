// 文件：ClickSettings.cs
// 说明：定义点击设置、坐标与参数校验。
// 版本：v1.0
// 日期：2026/11/15
// 历史：v1.0 2026/11/15 新增点击设置和输入边界。

namespace MouseClick.Core;

// 2026/11/15 新增：使用屏幕坐标表示固定位置与顺序点击位置，允许负坐标。
public sealed record ClickPoint(int X, int Y);

// 2026/11/15 新增：定义原生鼠标动作与目标选择方式。
public enum MouseButton { Left, Right, Middle }
public enum ClickKind { Single, Double }
public enum TargetMode { Cursor, Fixed, Sequence }

// 2026/11/15 新增：集中定义可保存的设置和合法输入范围。
public sealed record ClickSettings
{
    public MouseButton Button { get; init; } = MouseButton.Left;
    public ClickKind Kind { get; init; } = ClickKind.Single;
    public int IntervalMilliseconds { get; init; } = 100;
    public int StartDelayMilliseconds { get; init; } = 1000;
    public int RepeatCount { get; init; }
    public TargetMode Mode { get; init; } = TargetMode.Cursor;
    public ClickPoint FixedPoint { get; init; } = new(0, 0);
    public IReadOnlyList<ClickPoint> Points { get; init; } = Array.Empty<ClickPoint>();
    public int ToggleHotkey { get; init; } = 0x75;

    // 2026/11/15 新增：在执行和保存边界拒绝不合法设置。
    public void Validate()
    {
        if (!Enum.IsDefined(Button)) throw new ArgumentException("鼠标按键无效。", nameof(Button));
        if (!Enum.IsDefined(Kind)) throw new ArgumentException("点击类型无效。", nameof(Kind));
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("目标模式无效。", nameof(Mode));
        if (IntervalMilliseconds is < 1 or > 86400000)
            throw new ArgumentException("点击间隔必须为 1～86400000 毫秒。", nameof(IntervalMilliseconds));
        if (StartDelayMilliseconds is < 0 or > 3600000)
            throw new ArgumentException("启动延时必须为 0～3600000 毫秒。", nameof(StartDelayMilliseconds));
        if (RepeatCount is < 0 or > 1000000)
            throw new ArgumentException("轮数必须为 0～1000000，0 表示持续运行。", nameof(RepeatCount));
        if (ToggleHotkey is not (0x75 or 0x78 or 0x79))
            throw new ArgumentException("启停快捷键只能选择 F6、F9 或 F10。", nameof(ToggleHotkey));
        if (FixedPoint is null) throw new ArgumentException("固定坐标不能为空。", nameof(FixedPoint));
        if (Points is null) throw new ArgumentException("坐标列表不能为空。", nameof(Points));
        if (Mode == TargetMode.Sequence && Points.Count == 0)
            throw new ArgumentException("顺序模式至少需要一个坐标。", nameof(Points));
        if (Points.Any(point => point is null))
            throw new ArgumentException("坐标列表不能包含空坐标。", nameof(Points));
    }
}
