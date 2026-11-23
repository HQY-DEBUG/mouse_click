// 文件：Ui.cs
// 描述：统一桌面界面的颜色、文本和按钮外观。
// 版本：v1.3
// 日期：2026/11/23
// 历史：v1.3 2026/11/23 为数字输入框的原生步进区绘制细线箭头与交互状态。
// v1.2 2026/11/23 统一石墨深色主题、蓝紫高亮、圆角卡片和原生输入框。
// v1.1 2026/11/15 新增秒单位三位小数时间输入。
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MouseClick.App;

internal static class Ui
{
    // 2026/11/23 修改：石墨背景上分开定义文本高亮与白字按钮底色，高对比模式采用 Windows 系统色。
    internal static Color Ink => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(242, 242, 245);
    internal static Color Muted => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(178, 178, 192);
    internal static Color Accent => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(181, 166, 255);
    internal static Color AccentFill => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(117, 96, 221);
    internal static Color Background => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(21, 22, 26);
    internal static Color Danger => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(245, 163, 174);
    internal static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(32, 33, 38);
    internal static Color Line => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(58, 59, 69);
    internal static Color AccentSoft => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(52, 48, 69);
    private static Color InputSurface => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(43, 44, 52);

    // 2026/11/23 新增：Program 在创建窗口前启用 WinForms 的原生暗色标题栏、菜单及输入控件。
    internal static void EnableNativeDarkMode()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemInformation.HighContrast)
        {
#pragma warning disable WFO5001 // .NET 10 的 WinForms 深色主题接口仍标记为试验性。
            Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        }
    }

    internal static Label Text(string text, float size = 10, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = Ink,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 4, 0, 5)
    };

    // 2026/11/23 修改：主窗体和对话框沿用文字按钮入口，传入图标即可增加左侧操作符号。
    internal static ActionButton Button(string text, bool primary = false, UiIcon icon = UiIcon.None) =>
        new(text, primary, icon);

    // 2026/11/23 新增：卡片保持 Panel 的原生布局和滚动行为，绘制圆角底色与细边框。
    internal static Panel Card() => new CardPanel();

    private sealed class CardPanel : Panel
    {
        internal CardPanel()
        {
            BackColor = Surface;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Background);
            if (Width <= 1 || Height <= 1) return;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            var radius = new SizeF(LogicalToDeviceUnits(12), LogicalToDeviceUnits(12));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(BackColor);
            using var border = new Pen(Line);
            e.Graphics.FillRoundedRectangle(fill, bounds, radius);
            e.Graphics.DrawRoundedRectangle(border, bounds, radius);
        }
    }

    // 2026/11/23 修改：下拉选择与展开列表共用深色输入区域，保留原生方向键导航和选择。
    internal static ComboBox Choice(params string[] items)
    {
        var box = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top,
            FlatStyle = SystemInformation.HighContrast ? FlatStyle.Standard : FlatStyle.Flat,
            BackColor = InputSurface, ForeColor = Ink
        };
        if (!SystemInformation.HighContrast)
        {
            box.DrawMode = DrawMode.OwnerDrawFixed;
            box.DrawItem += (_, e) =>
            {
                bool selected = e.State.HasFlag(DrawItemState.Selected) && !e.State.HasFlag(DrawItemState.ComboBoxEdit);
                Color fill = selected ? AccentFill : box.BackColor;
                using var brush = new SolidBrush(fill);
                e.Graphics.FillRectangle(brush, e.Bounds);
                string? text = e.Index >= 0 ? box.GetItemText(box.Items[e.Index]) : box.Text;
                var bounds = Rectangle.Inflate(e.Bounds, -box.LogicalToDeviceUnits(4), 0);
                TextRenderer.DrawText(e.Graphics, text, box.Font, bounds, selected ? Color.White : box.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
        }
        box.Items.AddRange(items);
        box.SelectedIndex = 0;
        return box;
    }

    // 2026/11/23 修改：原生数字编辑区及其输入文字共用深色配色，保留原有步进与范围。
    internal static NumericUpDown Number(decimal min, decimal max, decimal value, string name) => new NumberField
    {
        Minimum = min, Maximum = max, Value = value, Name = name,
        ThousandsSeparator = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4),
        BackColor = InputSurface, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle
    };

    // 2026/11/23 修改：Number 与 Seconds 共用原生编辑和步进子控件，仅在其完成绘制后调整外观。
    private sealed class NumberField : NumericUpDown
    {
        private readonly Control _stepper;
        private int _hoveredHalf = -1;
        private int _pressedHalf = -1;

        internal NumberField()
        {
            // 2026/11/23 新增：步进子控件负责绘制，鼠标事件由 WinForms 转发到父数字框，保留原生捕获与重复计时。
            _stepper = Controls.Cast<Control>().Single(child => child is not TextBoxBase);
            _stepper.Paint += PaintStepper;
            MouseMove += (_, e) => ShowStepState(HalfAt(e.Location), _pressedHalf);
            MouseLeave += (_, _) => ShowStepState(-1, _pressedHalf);
            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    int half = HalfAt(e.Location);
                    ShowStepState(half, half);
                }
            };
            MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowStepState(HalfAt(e.Location), -1);
            };
            _stepper.MouseCaptureChanged += (_, _) =>
            {
                if (!_stepper.Capture) ShowStepState(_hoveredHalf, -1);
            };
        }

        // 2026/11/23 新增：视觉状态使用与原生步进区相同的上下半区，拖离按下位置时取消高亮。
        private int HalfAt(Point point) => !_stepper.Bounds.Contains(point) ? -1 :
            point.Y - _stepper.Top < _stepper.ClientSize.Height / 2 ? 0 : 1;

        private void ShowStepState(int hovered, int pressed)
        {
            if (_hoveredHalf == hovered && _pressedHalf == pressed) return;
            _hoveredHalf = hovered;
            _pressedHalf = pressed;
            _stepper.Invalidate();
        }

        // 2026/11/23 新增：两枚对称箭头共用石墨底色和弱分隔，高对比模式直接显示原生绘制结果。
        private void PaintStepper(object? sender, PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) return;
            var size = _stepper.ClientSize;
            float scale = _stepper.DeviceDpi / 96f;
            int middle = size.Height / 2;
            if (size.Width < 3 || middle < 2) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(39, 40, 47));
            using var rule = new Pen(Line);
            g.DrawLine(rule, 0.5f, scale, 0.5f, size.Height - scale);
            g.DrawLine(rule, 3 * scale, middle - 0.5f, size.Width - 3 * scale, middle - 0.5f);
            for (int half = 0; half < 2; half++)
            {
                int top = half == 0 ? 0 : middle;
                int height = half == 0 ? middle : size.Height - middle;
                bool hot = Enabled && _hoveredHalf == half && (_pressedHalf == -1 || _pressedHalf == half);
                bool pressed = hot && _pressedHalf == half;
                if (hot)
                {
                    using var fill = new SolidBrush(pressed ? AccentFill : AccentSoft);
                    g.FillRoundedRectangle(fill, new RectangleF(1, top, size.Width - 1, height),
                        new SizeF(3 * scale, 3 * scale));
                }
                float x = size.Width * 0.5f;
                float y = top + (height - 1) * 0.5f;
                float wing = Math.Min(3 * scale, (size.Width - 2) * 0.5f);
                float rise = Math.Min(1.5f * scale, (height - 1) * 0.25f) * (half == 0 ? 1 : -1);
                Color ink = !Enabled ? Color.FromArgb(138, 137, 150) : pressed ? Color.White : hot ? Accent : Muted;
                using var stroke = new Pen(ink, 1.4f * scale)
                {
                    StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
                };
                g.DrawLines(stroke, new PointF[] { new(x - wing, y + rise), new(x, y - rise), new(x + wing, y + rise) });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (SystemInformation.HighContrast) return;
            using var border = new Pen(ContainsFocus ? Accent : Color.FromArgb(105, 105, 123));
            e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
    }

    // 2026/11/15 新增：时间输入统一为秒，归一小数精度，确保显示值与执行时长一致。
    internal static NumericUpDown Seconds(decimal min, decimal max, decimal value, string name)
    {
        var input = Number(min, max, value, name);
        input.DecimalPlaces = 3;
        input.Increment = 0.1m;
        input.ThousandsSeparator = false;
        input.ValueChanged += (_, _) =>
        {
            decimal rounded = decimal.Round(input.Value, 3, MidpointRounding.AwayFromZero);
            if (input.Value != rounded) input.Value = rounded;
        };
        return input;
    }

    internal static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty };
        row.Controls.AddRange(controls);
        return row;
    }
}
