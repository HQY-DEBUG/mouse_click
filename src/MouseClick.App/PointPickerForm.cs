// 文件：PointPickerForm.cs
// 描述：全屏点选一个或多个物理屏幕坐标。
// 版本：v1.0
// 日期：2026/11/15
// 历史：v1.0 2026/11/15 新增多屏坐标选择层。
using MouseClick.Core;

namespace MouseClick.App;

internal sealed class PointPickerForm : Form
{
    private readonly Action<ClickPoint> _picked;
    private readonly bool _single;
    private readonly List<Point> _markers = [];
    private Point _pointer;
    private MouseButtons _finishButton = MouseButtons.None;

    // 2026/11/15 新增：覆盖虚拟桌面，以鼠标左键实际点击位置作为坐标。
    internal PointPickerForm(bool single, Action<ClickPoint> picked)
    {
        _single = single;
        _picked = picked;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        BackColor = Color.FromArgb(10, 27, 39);
        Opacity = 0.55;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        Text = "选取点击位置";
        AccessibleName = Text;
    }

    // 2026/11/15 新增：在主屏幕显示选点说明，在已选位置绘制编号。
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var primary = Screen.PrimaryScreen?.Bounds ?? Bounds;
        var origin = PointToClient(new Point(primary.Left + 30, primary.Top + 32));
        using var titleFont = new Font("Microsoft YaHei UI", 20, FontStyle.Bold);
        using var bodyFont = new Font("Microsoft YaHei UI", 12);
        using var pen = new Pen(Color.White, 3);
        var title = _single ? "左键点击：选择固定位置" : "左键依次点击：添加循环位置";
        e.Graphics.DrawString(title, titleFont, Brushes.White, origin);
        e.Graphics.DrawString($"右键 / Esc 完成    已选 {_markers.Count} 个    当前 ({_pointer.X}, {_pointer.Y})",
            bodyFont, Brushes.White, origin.X, origin.Y + 44);
        for (var i = 0; i < _markers.Count; i++)
        {
            var p = PointToClient(_markers[i]);
            e.Graphics.DrawEllipse(pen, p.X - 13, p.Y - 13, 26, 26);
            e.Graphics.DrawString((i + 1).ToString(), bodyFont, Brushes.White, p.X + 18, p.Y - 12);
        }
    }

    // 2026/11/15 新增：截获选点点击，避免传递给下方应用；Esc 或右键退出。
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_finishButton != MouseButtons.None) return;
        if (e.Button == MouseButtons.Right)
        {
            _finishButton = e.Button;
            Capture = true;
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        var point = PointToScreen(e.Location);
        _picked(new ClickPoint(point.X, point.Y));
        _markers.Add(point);
        if (_single) { _finishButton = e.Button; Capture = true; }
        Invalidate();
    }

    // 2026/11/15 新增：接收完整按下及抬起后再关闭，避免右键菜单或抬起事件落入下方窗口。
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != _finishButton) return;
        Capture = false;
        Close();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _pointer = PointToScreen(e.Location);
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
