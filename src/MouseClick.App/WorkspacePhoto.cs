// 文件：WorkspacePhoto.cs
// 描述：读取本机的工作区背景照片并释放源文件句柄。
// 版本：v1.0
// 日期：2026/11/23
// 历史：v1.0 2026/11/23 新增本机 JPEG 加载，缺失或无法读取时使用原有主题。
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MouseClick.App;

internal static class WorkspacePhoto
{
    // 2026/11/23 新增：主窗体传入本机配置目录中的文件路径；返回的像素副本由调用方释放。
    internal static Image? Load(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath)) return null;
        try
        {
            using var file = File.OpenRead(localPath);
            using var photo = Image.FromStream(file, useEmbeddedColorManagement: true, validateImageData: true);
            if (photo.RawFormat.Guid != ImageFormat.Jpeg.Guid)
            {
                Trace.TraceWarning("本机工作区背景不是 JPEG 图片，已使用默认主题。");
                return null;
            }
            return new Bitmap(photo);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or ExternalException or OutOfMemoryException)
        {
            // 2026/11/23 新增：无配置是正常状态；GDI+ 解码损坏文件也可能报告内存不足，诊断仅记录异常类型。
            if (error is not (FileNotFoundException or DirectoryNotFoundException))
                Trace.TraceWarning("无法读取本机工作区背景，已使用默认主题（{0}）。", error.GetType().Name);
            return null;
        }
    }
}
