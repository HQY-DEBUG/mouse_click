// 文件：NativeMethods.cs
// 描述：为桌面集成测试提供光标恢复和测试窗口命中检查。
// 版本：v1.0
// 日期：2026/11/15
// 历史：
// v1.0 2026/11/15 新增公开 User32 接口声明，不提供键盘输入注入。

using System.Runtime.InteropServices;

namespace MouseClick.WindowsTests;

// 2026/11/15 新增：测试只读取窗口归属并恢复光标，鼠标点击统一使用生产代码发送。
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(NativePoint point);
}
