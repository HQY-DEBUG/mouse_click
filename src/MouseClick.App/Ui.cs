// 文件：Ui.cs
// 描述：统一桌面界面的颜色、文本和按钮外观。
// 版本：v1.2
// 日期：2026/11/23
// 历史：v1.2 2026/11/23 统一石墨深色主题、蓝紫高亮、圆角卡片和原生输入框。
// v1.1 2026/11/15 新增秒单位三位小数时间输入。
// v1.0 2026/11/15 新增界面样式。
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
    internal static NumericUpDown Number(decimal min, decimal max, decimal value, string name) => new()
    {
        Minimum = min, Maximum = max, Value = value, Name = name,
        ThousandsSeparator = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4),
        BackColor = InputSurface, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle
    };

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
