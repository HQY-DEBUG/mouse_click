// 文件：PositionPreview.cs
// 描述：按显示器实际排列预览固定坐标及跟随鼠标模式。
// 版本：v1.0
// 日期：2026/11/23
// 历史：v1.0 2026/11/23 新增支持虚拟桌面负坐标的只读位置预览。
using System.Drawing.Drawing2D;
using MouseClick.Core;

namespace MouseClick.App;

internal sealed class PositionPreview : Control
{
    private TargetMode _mode = TargetMode.Cursor;
    private ClickPoint _point = new(0, 0);

    // 2026/11/23 新增：预览只显示目标，Tab 键直接前往下一项可操作控件。
    internal PositionPreview()
    {
        Name = "PositionPreview";
        AccessibleName = "点击位置预览";
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleDescription = "跟随鼠标，指针为示意位置。";
        TabStop = false;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
    }

    // 2026/11/23 新增：主窗体切换模式、点选或手动修改坐标时刷新，无需定时查询指针。
    internal void SetTarget(TargetMode mode, ClickPoint fixedPoint)
    {
        if (_mode == mode && _point == fixedPoint) return;
        _mode = mode;
        _point = fixedPoint;
        AccessibleDescription = mode == TargetMode.Fixed
            ? $"固定位置，X {fixedPoint.X}，Y {fixedPoint.Y}。" : "跟随鼠标，指针为示意位置。";
        Invalidate();
    }

    // 2026/11/23 新增：根据屏幕边界的并集缩放示意图，逐屏判断坐标是否在有效区域内。
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        bool contrast = SystemInformation.HighContrast;
        Color surface = contrast ? SystemColors.Window : Ui.Surface;
        Color backdrop = contrast ? SystemColors.Window : Ui.Background;
        Color border = contrast ? SystemColors.WindowText : Ui.Line;
        Color accent = contrast ? SystemColors.Highlight : Ui.Accent;
        var graphics = e.Graphics;
        graphics.Clear(backdrop);

        Screen[] screens = Screen.AllScreens;
        var target = new Point(_point.X, _point.Y);
        bool fixedMode = _mode == TargetMode.Fixed;
        bool onScreen = screens.Any(screen => screen.Bounds.Contains(target));
        string label = fixedMode ? $"固定位置   ({_point.X}, {_point.Y})" : "跟随鼠标   ·   指针示意";
        if (fixedMode && !onScreen) label += "   ·   屏幕范围外";
        int margin = LogicalToDeviceUnits(12);
        int labelHeight = Font.Height + LogicalToDeviceUnits(3);
        var caption = new Rectangle(margin, margin, Width - 2 * margin, labelHeight);
        if (caption.Width <= 0) return;
        TextRenderer.DrawText(graphics, label, Font, caption, contrast ? SystemColors.WindowText : Ui.Ink,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        int mapTop = caption.Bottom + margin;
        var map = Rectangle.FromLTRB(margin, mapTop, Width - margin, Height - margin);
        if (screens.Length == 0 || map.Width < margin || map.Height < margin) return;

        Rectangle desktop = screens.Select(screen => screen.Bounds).Aggregate(Rectangle.Union);
        float scale = Math.Min((float)map.Width / desktop.Width, (float)map.Height / desktop.Height);
        float left = map.Left + (map.Width - desktop.Width * scale) / 2;
        float top = map.Top + (map.Height - desktop.Height * scale) / 2;
        PointF Project(double x, double y) => new(left + (float)((x - desktop.Left) * scale),
            top + (float)((y - desktop.Top) * scale));

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var face = new SolidBrush(surface);
        using var edge = new Pen(border, Math.Max(1, LogicalToDeviceUnits(1)));
        using var grid = new Pen(Color.FromArgb(110, border));
        foreach (Screen screen in screens)
        {
            Rectangle bounds = screen.Bounds;
            PointF origin = Project(bounds.Left, bounds.Top);
            var monitor = new RectangleF(origin.X, origin.Y, bounds.Width * scale, bounds.Height * scale);
            graphics.FillRectangle(face, monitor);
            if (!contrast && monitor.Width > 4 * margin && monitor.Height > 3 * margin)
            {
                for (int part = 1; part < 4; part++)
                {
                    float x = monitor.Left + monitor.Width * part / 4;
                    float y = monitor.Top + monitor.Height * part / 4;
                    graphics.DrawLine(grid, x, monitor.Top, x, monitor.Bottom);
                    graphics.DrawLine(grid, monitor.Left, y, monitor.Right, y);
                }
            }
            graphics.DrawRectangle(edge, monitor);
        }

        // 2026/11/23 新增：在有效固定位置画靶心，跟随模式在主屏中央画示意指针。
        using var mark = new SolidBrush(accent);
        if (fixedMode)
        {
            if (!onScreen) return;
            PointF center = Project(target.X, target.Y);
            int radius = LogicalToDeviceUnits(6);
            using var ring = new Pen(accent, Math.Max(1, LogicalToDeviceUnits(2)));
            graphics.DrawEllipse(ring, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
            graphics.FillEllipse(mark, center.X - radius / 3f, center.Y - radius / 3f, 2 * radius / 3f, 2 * radius / 3f);
        }
        else
        {
            Rectangle primary = screens.FirstOrDefault(screen => screen.Primary)?.Bounds ?? screens[0].Bounds;
            PointF tip = Project(primary.Left + primary.Width * 0.5, primary.Top + primary.Height * 0.5);
            float unit = LogicalToDeviceUnits(16) / 16f;
            PointF[] arrow = [tip, new(tip.X + 3 * unit, tip.Y + 15 * unit),
                new(tip.X + 6 * unit, tip.Y + 10 * unit), new(tip.X + 13 * unit, tip.Y + 9 * unit)];
            graphics.FillPolygon(mark, arrow);
        }
    }
}
