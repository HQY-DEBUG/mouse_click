// 文件：WorkspaceHeader.cs
// 描述：绘制主窗口的品牌栏、应用指针与轨迹背景。
// 版本：v1.1
// 日期：2026/11/23
// 历史：v1.1 2026/11/23 使用本机照片时让标题区透出工作区背景。
// v1.0 2026/11/23 新增适配窗口宽度、DPI 与高对比模式的品牌栏。

using System.Drawing.Drawing2D;

namespace MouseClick.App;

// 2026/11/23 新增：在标题与设置入口后绘制背景，保留子控件的原生焦点和可访问文本。
internal sealed class WorkspaceHeader : Panel
{
    private readonly Icon _logo;
    private readonly bool _usesPhoto;

    // 2026/11/23 修改：MainForm 提供本次是否有照片，普通主题保留原品牌栏。
    internal WorkspaceHeader(Icon logo, bool usesPhoto)
    {
        _logo = logo;
        _usesPhoto = usesPhoto;
        DoubleBuffered = true;
        BackColor = usesPhoto ? Color.Transparent :
            SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(35, 35, 43);
    }

    // 2026/11/23 新增：用逻辑像素绘制圆角标题区，窄窗口下让轨迹留在标题右侧。
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        if (_usesPhoto) base.OnPaintBackground(e);
        var state = g.Save();
        try
        {
            float scale = DeviceDpi / 96f;
            g.ScaleTransform(scale, scale);
            float width = ClientSize.Width / scale;
            float height = ClientSize.Height / scale;
            var bounds = new RectangleF(0, 0, width, height);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = new GraphicsPath();
            shape.AddRoundedRectangle(bounds, new SizeF(14, 14));
            bool contrast = SystemInformation.HighContrast;
            if (!_usesPhoto || contrast)
            {
                g.Clear(Parent?.BackColor ?? Ui.Background);
                Color first = contrast ? SystemColors.Window : Color.FromArgb(34, 34, 43);
                Color last = contrast ? SystemColors.Window : Color.FromArgb(57, 48, 86);
                using var fill = new LinearGradientBrush(bounds, first, last, 0f);
                g.FillPath(fill, shape);
            }
            g.SetClip(shape, CombineMode.Intersect);
            if (!_usesPhoto && !contrast && width > 620)
            {
                // 2026/11/23 新增：以淡色折线和点位呼应软件的多位置循环功能。
                float x = width - 310;
                PointF[] path = [new(x - 60, height + 10), new(x + 10, height * .49f),
                    new(x + 65, height * .64f), new(x + 136, height * .22f), new(x + 196, -6)];
                using var line = new Pen(Color.FromArgb(58, 172, 169, 255), 1.4f);
                using var dot = new SolidBrush(Color.FromArgb(148, 187, 184, 255));
                g.DrawLines(line, path);
                foreach (PointF p in path[1..^1]) g.FillEllipse(dot, p.X - 2.5f, p.Y - 2.5f, 5, 5);
            }
            var tile = new RectangleF(19, (height - 46) / 2, 46, 46);
            using var tilePath = new GraphicsPath();
            tilePath.AddRoundedRectangle(tile, new SizeF(11, 11));
            using var tileFill = new SolidBrush(contrast ? SystemColors.Window : Color.FromArgb(200, 200, 252));
            g.FillPath(tileFill, tilePath);
            g.DrawIcon(_logo, new Rectangle(27, (int)(tile.Y + 8), 30, 30));
        }
        finally { g.Restore(state); }
    }
}
