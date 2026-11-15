// 文件：Ui.cs
// 描述：统一桌面界面的颜色、文本和按钮外观。
// 版本：v1.1
// 日期：2026/11/15
// 历史：v1.1 2026/11/15 新增秒单位三位小数时间输入。
// v1.0 2026/11/15 新增界面样式。
using System.Drawing;

namespace MouseClick.App;

internal static class Ui
{
    // 2026/11/15 新增：使用高对比文字和统一留白呈现操作界面。
    internal static readonly Color Ink = Color.FromArgb(26, 45, 57);
    internal static readonly Color Muted = Color.FromArgb(98, 116, 129);
    internal static readonly Color Accent = Color.FromArgb(0, 119, 111);
    internal static readonly Color Background = Color.FromArgb(241, 245, 247);
    internal static readonly Color Danger = Color.FromArgb(172, 48, 56);

    internal static Label Text(string text, float size = 10, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = Ink,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 4, 0, 5)
    };

    internal static Button Button(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new Size(72, 34),
        Padding = new Padding(8, 3, 8, 3), FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Accent : Color.White,
        ForeColor = primary ? Color.White : Ink,
        Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0),
        UseVisualStyleBackColor = false
    };

    internal static ComboBox Choice(params string[] items)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        box.Items.AddRange(items);
        box.SelectedIndex = 0;
        return box;
    }

    internal static NumericUpDown Number(decimal min, decimal max, decimal value, string name) => new()
    {
        Minimum = min, Maximum = max, Value = value, Name = name,
        ThousandsSeparator = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4)
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
