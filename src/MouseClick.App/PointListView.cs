// 文件：PointListView.cs
// 描述：在 Windows 原生坐标列表上绘制表头、分隔线和选区。
// 版本：v1.1
// 日期：2026/11/23
// 历史：v1.1 2026/11/23 修复首次显示列宽，并清除空白区多余的原生列线。
// v1.0 2026/11/23 新增自适应坐标表格，保留系统多选和键盘焦点。
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseClick.App;

internal sealed class PointListView : ListView
{
    private readonly ImageList _rowHeight = new();
    private bool _fitPending;
    private bool? _darkScrollbar;
    private const TextFormatFlags CellText = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    // 2026/11/23 新增：继续由原生 ListView 管理行、列、焦点、滚动与多选状态。
    internal PointListView()
    {
        Name = "PointList";
        AccessibleName = "循环坐标列表";
        View = View.Details;
        FullRowSelect = MultiSelect = DoubleBuffered = OwnerDraw = true;
        HideSelection = false;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        BorderStyle = BorderStyle.None;
        BackColor = Ui.Surface;
        ForeColor = Ui.Ink;
        Columns.Add("顺序");
        Columns.Add("X 坐标");
        Columns.Add("Y 坐标");
        SetRowHeight();
        SmallImageList = _rowHeight;
    }

    // 2026/11/23 新增：使用空图像列表指定原生行距，随显示器缩放调整。
    private void SetRowHeight() => _rowHeight.ImageSize = new Size(1, LogicalToDeviceUnits(29));

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        SetRowHeight();
        QueueColumnFit();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _darkScrollbar = null;
        SetRowHeight();
        FitColumns();
        // WinForms 在整棵窗口树建立后会应用进程原生主题，待其完成再同步一次本列表的滚动条。
        BeginInvoke((Action)(() => { if (IsHandleCreated && !IsDisposed) SetScrollbarTheme(refresh: true); }));
        QueueColumnFit();
    }

    // 2026/11/23 新增：空白行和系统滚动条同用深色主题，切换到高对比时恢复 Windows 配色。
    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        BackColor = Ui.Surface;
        ForeColor = Ui.Ink;
        if (IsHandleCreated) SetScrollbarTheme(refresh: true);
        Invalidate();
    }

    private void SetScrollbarTheme(bool refresh = false)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        const int windowStyle = -16, verticalScroll = 0x00200000;
        bool dark = !SystemInformation.HighContrast && (GetWindowLong(Handle, windowStyle) & verticalScroll) != 0;
        if (!refresh && _darkScrollbar == dark) return;
        _darkScrollbar = dark;
        // 只有实际出现滚动条才启用其深色原生主题，普通列表的空白区保持面板本色。
        int result = SetWindowTheme(Handle, dark ? "DarkMode_Explorer" : null, null);
        if (result < 0) Trace.TraceWarning("设置坐标列表系统滚动条主题失败，HRESULT 0x{0:X8}。", result);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(nint handle, string? subAppName, string? subIdList);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    // 2026/11/23 新增：等系统重算滚动条后再分配列宽，先缩窄再拉宽避免横向滚动条滞留。
    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (IsHandleCreated) QueueColumnFit();
        else FitColumns();
    }

    // 2026/11/23 新增：首次显示和切回循环模式时立即铺满表头，无需等下一次消息派发。
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) FitColumns();
    }

    private void QueueColumnFit()
    {
        if (!IsHandleCreated || _fitPending) return;
        _fitPending = true;
        BeginInvoke((Action)(() =>
        {
            _fitPending = false;
            FitColumns();
        }));
    }

    private void FitColumns()
    {
        if (IsDisposed || Columns.Count != 3 || ClientSize.Width == 0) return;
        if (IsHandleCreated) SetScrollbarTheme();
        int width = ClientSize.Width;
        int indexWidth = Math.Min(LogicalToDeviceUnits(78), width / 3);
        int xWidth = (width - indexWidth) / 2;
        int[] widths = [indexWidth, xWidth, width - indexWidth - xWidth];
        for (int i = 0; i < widths.Length; i++)
            if (Columns[i].Width > widths[i]) Columns[i].Width = widths[i];
        for (int i = 0; i < widths.Length; i++)
            if (Columns[i].Width < widths[i]) Columns[i].Width = widths[i];
    }

    // 2026/11/23 新增：表头和行使用一致的内边距；高对比主题交由系统绘制。
    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        if (SystemInformation.HighContrast) { e.DrawDefault = true; return; }
        PaintCell(e.Graphics, e.Bounds, e.Header?.Text ?? "", Ui.Background, Ui.Muted);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        e.DrawDefault = SystemInformation.HighContrast;
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (SystemInformation.HighContrast) { e.DrawDefault = true; return; }
        if (e.Item is not { } item || e.SubItem is not { } cell) return;
        var bounds = e.Bounds;
        bounds.Width = Math.Min(bounds.Width, Columns[e.ColumnIndex].Width);
        Color foreground = e.ColumnIndex == 0 ? (item.Selected ? Ui.Accent : Ui.Muted) : Ui.Ink;
        PaintCell(e.Graphics, bounds, cell.Text, item.Selected ? Ui.AccentSoft : Ui.Surface, foreground);
        if (e.ColumnIndex == 0 && item.Focused && Focused)
        {
            using var marker = new SolidBrush(Ui.Accent);
            e.Graphics.FillRectangle(marker, bounds.Left, bounds.Top, LogicalToDeviceUnits(3), bounds.Height - 1);
        }
    }

    private void PaintCell(Graphics graphics, Rectangle bounds, string text, Color background, Color foreground)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var fill = new SolidBrush(background);
        using var rule = new Pen(Ui.Line);
        graphics.FillRectangle(fill, bounds);
        graphics.DrawLine(rule, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
        bounds.Inflate(-LogicalToDeviceUnits(9), 0);
        if (bounds.Width > 0) TextRenderer.DrawText(graphics, text, Font, bounds, foreground, CellText);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { SmallImageList = null; _rowHeight.Dispose(); }
        base.Dispose(disposing);
    }
}
