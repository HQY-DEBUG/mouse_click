// 文件：TestWindow.cs
// 描述：显示专用大目标并记录真实 Win32 鼠标消息及其物理屏幕落点。
// 版本：v1.1
// 日期：2026/11/15
// 历史：
// v1.1 2026/11/15 同步钰宇鼠标连点器名称。
// v1.0 2026/11/15 新增置顶验收窗口、三个目标与原生消息记录。

using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/15 新增：保存窗口实际收到的输入，不以发送参数代替操作系统交付结果。
internal enum MousePhase { Down, Up, DoubleClick }
internal sealed record ObservedMouseMessage(int Target, MouseButton Button, MousePhase Phase, Point ScreenPoint);
internal sealed record TestTarget(int Id, IntPtr Handle, Rectangle ScreenBounds)
{
    public ClickPoint Center => new(ScreenBounds.Left + ScreenBounds.Width / 2, ScreenBounds.Top + ScreenBounds.Height / 2);
}

// 2026/11/15 新增：专用窗口没有业务操作，用户关闭窗口时由入口取消测试并完成清理。
internal sealed class TestWindow : Form
{
    private readonly TargetControl[] targets;
    public List<ObservedMouseMessage> Messages { get; } = [];

    public TestWindow()
    {
        Rectangle desktop = Screen.PrimaryScreen?.WorkingArea
            ?? throw new InvalidOperationException("没有可用的主显示器，不能执行桌面验收。");
        if (desktop.Width < 640 || desktop.Height < 360)
            throw new InvalidOperationException("主显示器可用区域过小，无法隔离三个测试目标。");

        Text = "钰宇鼠标连点器 · 专用输入验收";
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new(Math.Min(960, desktop.Width - 60), Math.Min(360, desktop.Height - 100));
        Location = new(desktop.Left + (desktop.Width - Width) / 2, desktop.Top + (desktop.Height - Height) / 2);
        BackColor = Color.FromArgb(24, 28, 38);
        AutoScaleMode = AutoScaleMode.None;

        // 2026/11/15 新增：目标之间保留足够距离，单击验收不会被上一目标的点击合成为双击。
        int targetWidth = (ClientSize.Width - 64) / 3;
        targets = Enumerable.Range(1, 3).Select(id => new TargetControl(id, Messages)
        {
            Bounds = new(16 + (id - 1) * (targetWidth + 16), 74, targetWidth, ClientSize.Height - 96)
        }).ToArray();
        Controls.AddRange(targets);
    }

    // 2026/11/15 新增：在 UI 线程建立句柄和物理坐标快照，后台执行器无需跨线程访问控件。
    public TestTarget[] SnapshotTargets() => targets.Select(target =>
        new TestTarget(target.Id, target.Handle, target.RectangleToScreen(target.ClientRectangle))).ToArray();

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        TextRenderer.DrawText(args.Graphics, "正在验证鼠标输入；请暂时不要移动鼠标。关闭本窗口可取消。", Font,
            new Rectangle(16, 16, ClientSize.Width - 32, 48), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}

// 2026/11/15 新增：启用 CS_DBLCLKS，分别捕获三种鼠标键的 down、up 和 doubleclick 消息。
internal sealed class TargetControl : Control
{
    private readonly List<ObservedMouseMessage> messages;
    public int Id { get; }

    public TargetControl(int id, List<ObservedMouseMessage> messages)
    {
        Id = id;
        this.messages = messages;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        BackColor = id switch { 1 => Color.FromArgb(31, 108, 173), 2 => Color.FromArgb(22, 126, 96), _ => Color.FromArgb(139, 79, 168) };
        TabStop = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x0008;
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        TextRenderer.DrawText(args.Graphics, $"目标 {Id}", Font, ClientRectangle, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    // 2026/11/15 新增：在控件默认处理之前记录原始消息；双击消息代表第二次按下，不能重复算作普通 down。
    protected override void WndProc(ref Message message)
    {
        (MouseButton Button, MousePhase Phase)? input = message.Msg switch
        {
            0x0201 => (MouseButton.Left, MousePhase.Down),
            0x0202 => (MouseButton.Left, MousePhase.Up),
            0x0203 => (MouseButton.Left, MousePhase.DoubleClick),
            0x0204 => (MouseButton.Right, MousePhase.Down),
            0x0205 => (MouseButton.Right, MousePhase.Up),
            0x0206 => (MouseButton.Right, MousePhase.DoubleClick),
            0x0207 => (MouseButton.Middle, MousePhase.Down),
            0x0208 => (MouseButton.Middle, MousePhase.Up),
            0x0209 => (MouseButton.Middle, MousePhase.DoubleClick),
            _ => null
        };
        if (input is { } observed)
        {
            long packed = message.LParam.ToInt64();
            Point clientPoint = new(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff)));
            messages.Add(new(Id, observed.Button, observed.Phase, PointToScreen(clientPoint)));
            Invalidate();
        }
        base.WndProc(ref message);
    }
}
