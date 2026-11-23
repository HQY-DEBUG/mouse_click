// 文件：PhotoWorkspace.cs
// 描述：在主窗口背景中展示本机照片，给标题、参数和工具区留出可读空间。
// 版本：v1.0
// 日期：2026/11/23
// 历史：v1.0 2026/11/23 新增保持原图比例的本机背景及石墨渐变。
using System.Drawing.Drawing2D;

namespace MouseClick.App;

internal sealed class PhotoWorkspace : TableLayoutPanel
{
    private readonly Image _photo;

    // 2026/11/23 新增：图片由 MainForm 持有和释放，本面板仅负责布局与绘制。
    internal PhotoWorkspace(Image photo)
    {
        _photo = photo;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Ui.Background;
    }

    // 2026/11/23 新增：照片靠右展示并按比例缩放，往文字和底部方向渐隐，高对比模式采用系统底色。
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        var backdrop = Ui.Background;
        g.Clear(backdrop);
        if (ClientSize.Width < 2 || ClientSize.Height < 2 || SystemInformation.HighContrast) return;
        float width = Math.Min(ClientSize.Width * .58f, LogicalToDeviceUnits(610));
        float height = width * _photo.Height / _photo.Width;
        float top = (RowStyles.Count > 0 ? RowStyles[0].Height : LogicalToDeviceUnits(165)) * .45f - height * .28f;
        var area = new RectangleF(ClientSize.Width - width, top, width, height);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(_photo, area);
        using var left = new LinearGradientBrush(area, backdrop, Color.FromArgb(0, backdrop), 0f);
        left.InterpolationColors = new ColorBlend
        {
            Positions = [0f, .12f, .30f, 1f],
            Colors = [backdrop, Color.FromArgb(145, backdrop), Color.FromArgb(0, backdrop), Color.FromArgb(0, backdrop)]
        };
        g.FillRectangle(left, area);
        using var bottom = new LinearGradientBrush(area, Color.FromArgb(0, backdrop), backdrop, 90f);
        bottom.InterpolationColors = new ColorBlend
        {
            Positions = [0f, .38f, .68f, 1f],
            Colors = [Color.FromArgb(0, backdrop), Color.FromArgb(0, backdrop), Color.FromArgb(185, backdrop), backdrop]
        };
        g.FillRectangle(bottom, area);
    }
}
