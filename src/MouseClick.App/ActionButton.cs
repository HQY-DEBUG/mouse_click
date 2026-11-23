// 文件：ActionButton.cs
// 描述：为原生 WinForms 按钮绘制圆角、操作图标和键盘焦点。
// 版本：v1.0
// 日期：2026/11/23
// 历史：v1.0 2026/11/23 新增石墨深色和系统高对比模式的操作按钮。
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MouseClick.App;

// 2026/11/23 新增：使用 Windows 自带 Segoe MDL2 Assets 图标，文字标签仍由按钮向辅助技术提供。
internal enum UiIcon
{
    None, Settings, Start, Stop, Pick, Add, Remove, Up, Down, Save, Tray, Exit
}

internal sealed class ActionButton : Button
{
    private readonly bool _primary;
    private UiIcon _icon;
    private bool _hovered;
    private bool _spaceDown;

    // 2026/11/23 新增：Ui.Button 供主窗体与对话框构建普通、主要和带图标的原生可聚焦按钮。
    internal ActionButton(string text, bool primary, UiIcon icon)
    {
        _primary = primary;
        _icon = icon;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Color.Transparent;
        ForeColor = primary ? Color.White : Ui.Ink;
        MinimumSize = new Size(60, 34);
        Padding = new Padding(10, 4, 10, 4);
        Margin = new Padding(0, 0, 8, 0);
        Cursor = Cursors.Hand;
        Text = text;
    }

    // 2026/11/23 新增：运行状态切换图标后重新测量按钮，支持正文和 DPI 一起变化。
    [DefaultValue(UiIcon.None)]
    internal UiIcon Icon
    {
        get => _icon;
        set
        {
            if (_icon == value) return;
            _icon = value;
            Parent?.PerformLayout();
            Invalidate();
        }
    }

    private int IconSize => _icon == UiIcon.None ? 0 : LogicalToDeviceUnits(15);
    private int IconGap => IconSize > 0 && Text.Length > 0 ? LogicalToDeviceUnits(6) : 0;
    private const TextFormatFlags Format = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, Format);
        return new Size(Math.Max(MinimumSize.Width, text.Width + IconSize + IconGap + Padding.Horizontal),
            Math.Max(MinimumSize.Height, Math.Max(text.Height, IconSize) + Padding.Vertical));
    }

    // 2026/11/23 新增：在石墨和蓝紫底色上保持图文对比，并为禁用、悬停、按下及键盘焦点提供可见反馈。
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 1 || Height <= 1) return;
        bool highContrast = SystemInformation.HighContrast;
        bool pressed = Enabled && (_spaceDown || (_hovered && Capture && MouseButtons.HasFlag(MouseButtons.Left)));
        Color fill, ink, line;
        if (!Enabled)
        {
            fill = highContrast ? SystemColors.Control : Color.FromArgb(43, 43, 51);
            ink = highContrast ? SystemColors.GrayText : Color.FromArgb(159, 157, 171);
            line = highContrast ? SystemColors.GrayText : Ui.Line;
        }
        else if (highContrast)
        {
            bool highlighted = _primary || _hovered || pressed;
            fill = highlighted ? SystemColors.Highlight : SystemColors.Control;
            ink = highlighted ? SystemColors.HighlightText : SystemColors.ControlText;
            line = SystemColors.ControlText;
        }
        else if (_primary)
        {
            fill = pressed ? Color.FromArgb(88, 67, 169) : _hovered ? Color.FromArgb(104, 83, 201) : Ui.AccentFill;
            line = fill;
            ink = ForeColor;
        }
        else
        {
            fill = pressed ? Color.FromArgb(61, 55, 78) : _hovered ? Ui.AccentSoft : Color.FromArgb(40, 41, 48);
            ink = ForeColor;
            line = _hovered || IsDefault ? Ui.Accent : Color.FromArgb(116, 116, 131);
        }

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = new SizeF(LogicalToDeviceUnits(7), LogicalToDeviceUnits(7));
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(line);
        graphics.FillRoundedRectangle(brush, bounds, radius);
        graphics.DrawRoundedRectangle(pen, bounds, radius);

        int available = Math.Max(0, Width - Padding.Horizontal);
        int textWidth = TextRenderer.MeasureText(Text, Font, Size.Empty, Format).Width;
        int content = Math.Min(available, IconSize + IconGap + textWidth);
        int left = Padding.Left + (available - content) / 2;
        int height = Math.Max(0, Height - Padding.Vertical);
        if (_icon != UiIcon.None)
        {
            using var symbols = new Font("Segoe MDL2 Assets", IconSize, FontStyle.Regular, GraphicsUnit.Pixel);
            var iconBounds = new Rectangle(left, Padding.Top, IconSize, height);
            TextRenderer.DrawText(graphics, Glyph(_icon), symbols, iconBounds, ink, Format | TextFormatFlags.NoPrefix);
            left += IconSize + IconGap;
        }
        var textBounds = new Rectangle(left, Padding.Top, Math.Max(0, content - IconSize - IconGap), height);
        TextFormatFlags prefix = !UseMnemonic ? TextFormatFlags.NoPrefix : ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(graphics, Text, Font, textBounds, ink, Format | prefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            int inset = LogicalToDeviceUnits(4);
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -inset, -inset), ink, fill);
        }
    }

    // 2026/11/23 新增：保留 Button 对鼠标、空格、回车和辅助技术的原生点击处理，仅更新绘制状态。
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyData == Keys.Space) { _spaceDown = true; Invalidate(); }
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space) { _spaceDown = false; Invalidate(); }
    }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _spaceDown = false; Invalidate(); }

    // 2026/11/23 新增：使用相同线宽的系统字形，避免图标像素化或依赖额外文件。
    private static string Glyph(UiIcon icon) => icon switch
    {
        UiIcon.Settings => "\uE713", UiIcon.Start => "\uE768", UiIcon.Stop => "\uE71A",
        UiIcon.Pick => "\uE707", UiIcon.Add => "\uE710", UiIcon.Remove => "\uE74D",
        UiIcon.Up => "\uE70E", UiIcon.Down => "\uE70D", UiIcon.Save => "\uE74E",
        UiIcon.Tray => "\uE921", UiIcon.Exit => "\uE8BB",
        _ => string.Empty
    };
}
