// 文件：WindowsClickOutput.cs
// 描述：使用 Windows SendInput 输出完整鼠标动作，并校验物理屏幕坐标。
// 版本：v1.0
// 日期：2026/11/15
// 历史：
// v1.0 2026/11/15 新增原生点击、虚拟桌面坐标转换和失败抬键补救。

using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseClick.Core;

namespace MouseClick.App.Interop;

// 2026/11/15 新增：调用方须在创建窗口前启用 PerMonitorV2，使屏幕范围与坐标统一为物理像素。
public sealed class WindowsClickOutput : IClickOutput
{
    private const uint InputMouse = 0;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const int VirtualScreenX = 76;
    private const int VirtualScreenY = 77;
    private const int VirtualScreenWidth = 78;
    private const int VirtualScreenHeight = 79;
    private static readonly int InputSize = Marshal.SizeOf<NativeInput>();

    // 2026/11/15 新增：将定位和完整按下、抬起序列放入同一批次，双击发送两对按钮事件。
    public void Click(MouseButton button, ClickKind kind, ClickPoint? point)
    {
        (uint down, uint up) = button switch
        {
            MouseButton.Left => (MouseLeftDown, MouseLeftUp),
            MouseButton.Right => (MouseRightDown, MouseRightUp),
            MouseButton.Middle => (MouseMiddleDown, MouseMiddleUp),
            _ => throw new ArgumentOutOfRangeException(nameof(button), "不支持的鼠标按钮。")
        };
        int clickCount = kind switch
        {
            ClickKind.Single => 1,
            ClickKind.Double => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "不支持的点击类型。")
        };

        NativeInput[] inputs = new NativeInput[(clickCount * 2) + (point is null ? 0 : 1)];
        int index = 0;
        if (point is not null)
        {
            inputs[index++] = CreateMove(point);
        }

        for (int click = 0; click < clickCount; click++)
        {
            inputs[index++] = CreateMouseInput(down);
            inputs[index++] = CreateMouseInput(up);
        }

        uint sent = SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent == inputs.Length)
        {
            return;
        }

        // 2026/11/15 新增：立即保留原始错误；部分发送后补发抬键，避免失败时留下按住状态。
        int error = Marshal.GetLastPInvokeError();
        string recovery = string.Empty;
        if (sent > 0)
        {
            NativeInput[] release = [CreateMouseInput(up)];
            uint released = SendInput(1, release, InputSize);
            recovery = released == 1
                ? " 已补发鼠标抬起事件。"
                : $" 补发鼠标抬起事件也失败：{DescribeError(Marshal.GetLastPInvokeError())}。";
        }

        throw new InvalidOperationException(
            $"鼠标输入发送失败（已发送 {sent}/{inputs.Length} 个事件）：{DescribeError(error)}。" +
            " 可能与目标程序权限较高、桌面不可交互或输入被阻止有关。" + recovery);
    }

    // 2026/11/15 新增：同时检查实际显示器及虚拟桌面，支持负坐标并拒绝显示器之间的空白区域。
    private static NativeInput CreateMove(ClickPoint point)
    {
        if (MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, 0) == IntPtr.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(point),
                $"坐标（{point.X}, {point.Y}）不在当前连接的显示器内，请重新选取位置。");
        }

        int left = GetSystemMetrics(VirtualScreenX);
        int top = GetSystemMetrics(VirtualScreenY);
        int width = GetSystemMetrics(VirtualScreenWidth);
        int height = GetSystemMetrics(VirtualScreenHeight);
        long relativeX = (long)point.X - left;
        long relativeY = (long)point.Y - top;
        if (width <= 0 || height <= 0 || relativeX < 0 || relativeX >= width || relativeY < 0 || relativeY >= height)
        {
            throw new InvalidOperationException("屏幕布局已改变或无法读取屏幕范围，请重新选取位置。");
        }

        return CreateMouseInput(MouseMove | MouseAbsolute | MouseVirtualDesk,
            NormalizeCoordinate(relativeX, width), NormalizeCoordinate(relativeY, height));
    }

    // 2026/11/15 新增：映射到目标像素中心，使用 64 位运算避免多显示器偏移乘法溢出。
    private static int NormalizeCoordinate(long coordinate, int length) =>
        (int)Math.Clamp(((coordinate * 65536L) + 32768L) / length, 0L, 65535L);

    // 2026/11/15 新增：其余字段保持零值，由 Windows 填写事件时间，不伪装输入来源。
    private static NativeInput CreateMouseInput(uint flags, int x = 0, int y = 0) => new()
    {
        Type = InputMouse,
        Data = new InputUnion
        {
            Mouse = new MouseInput { X = x, Y = y, Flags = flags }
        }
    };

    // 2026/11/15 新增：Windows 未提供错误码时仍给出明确说明，不把失败直接断言为权限问题。
    private static string DescribeError(int error) => error == 0
        ? "Windows 未提供具体错误码"
        : $"Windows 错误 {error}（{new Win32Exception(error).Message}）";

    // 2026/11/15 新增：通过原生对齐和联合体保留 INPUT 在 x86 为 28 字节、x64 为 40 字节的布局。
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    // 2026/11/15 新增：仅调用公开的 User32 输入与显示器接口，错误码在调用后立即读取。
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
}
