// 文件：WindowsClickOutput.cs
// 描述：使用 Windows SendInput 输出完整鼠标动作，并校验物理屏幕坐标。
// 版本：v1.2
// 日期：2026/11/16
// 历史：
// v1.2 2026/11/16 支持按住及双击间隔取消，成功抬起后保留取消异常并禁止后续按下。
// v1.1 2026/11/16 分开按下与抬起并保持五十毫秒，异常路径补发抬起并保留原始错误。
// v1.0 2026/11/15 新增原生点击、虚拟桌面坐标转换和失败抬键补救。

using System.ComponentModel;
using System.Runtime.ExceptionServices;
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

    // 2026/11/16 修改：保留既有三参数调用契约，无令牌调用复用同一分批输出及抬起清理。
    public void Click(MouseButton button, ClickKind kind, ClickPoint? point) =>
        Click(button, kind, point, CancellationToken.None);

    // 2026/11/16 新增：ClickEngine 及桌面验收可在保持期间取消；MainForm 工作线程仍使用同步调用。
    public void Click(MouseButton button, ClickKind kind, ClickPoint? point, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
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

        // [废弃] 2026/11/16 同批次按下/抬起持续时间为零，改用分批保持50ms，计划在 v1.4 清理。
        // inputs[index++] = CreateMouseInput(down);
        // inputs[index++] = CreateMouseInput(up);
        // SendInput((uint)inputs.Length, inputs, InputSize);

        // 2026/11/16 修改：首次定位与按下仍保持同批，坐标先校验；抬起独立发送，单击绝不增加第二次按下。
        NativeInput[] press = point is null
            ? [CreateMouseInput(down)]
            : [CreateMove(point), CreateMouseInput(down)];
        NativeInput[] release = [CreateMouseInput(up)];
        for (int click = 0; click < clickCount; click++)
        {
            // 2026/11/16 新增：取消优先于新按下，权限预检耗时期间发生取消也须在实际发送前再次检查。
            token.ThrowIfCancellationRequested();
            // 2026/11/16 新增：每次按下前检查前台及目标权限，已按下后的抬起清理不得被预检阻断。
            InputAccessGuard.EnsureCanSend(point);
            bool releaseRequired = false;
            Exception? failure = null;
            try
            {
                token.ThrowIfCancellationRequested();
                uint sent = SendInput((uint)press.Length, press, InputSize);
                int pressError = Marshal.GetLastPInvokeError();
                releaseRequired = sent > 0;
                if (sent != press.Length)
                    throw new InvalidOperationException(
                        $"鼠标按下发送失败（已发送 {sent}/{press.Length} 个事件）：{DescribeError(pressError)}。" +
                        " 可能与目标程序权限较高、桌面不可交互或输入被阻止有关。");

                // 2026/11/16 修改：正常按住至少50ms兼容轮询目标，单击节奏额外约50ms；取消立即进入finally抬起。
                WaitWithCancellation(50, token);
                uint released = SendInput(1, release, InputSize);
                int releaseError = Marshal.GetLastPInvokeError();
                if (released != 1)
                    throw new InvalidOperationException(
                        $"鼠标抬起发送失败（已发送 {released}/1 个事件）：{DescribeError(releaseError)}。" +
                        " 可能与目标程序权限较高、桌面不可交互或输入被阻止有关。");
                releaseRequired = false;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                // 2026/11/16 新增：按下已发送后，即使抬起返回0或保持期间异常，也必须尝试释放并保留原始及恢复错误。
                if (releaseRequired)
                {
                    try
                    {
                        uint recovered = SendInput(1, release, InputSize);
                        int recoveryError = Marshal.GetLastPInvokeError();
                        // 2026/11/16 修改：取消后的抬起成功保留原取消类型；抬起失败仍报告原始错误及恢复错误。
                        if (recovered != 1 || failure is not OperationCanceledException)
                        {
                            string recovery = recovered == 1
                                ? " 已补发鼠标抬起事件。"
                                : $" 补发鼠标抬起事件也失败：{DescribeError(recoveryError)}。";
                            failure = new InvalidOperationException(failure!.Message + recovery, failure);
                        }
                    }
                    catch (Exception recoveryException)
                    {
                        failure = new AggregateException("鼠标动作失败，异常清理时抬起也失败。", failure!, recoveryException);
                    }
                }
            }

            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            // 2026/11/16 修改：双击两对间释放20ms且可取消，取消后不发送第二次按下，不额外移动或补点。
            if (click + 1 < clickCount)
            {
                WaitWithCancellation(20, token);
                press = [CreateMouseInput(down)];
            }
        }
    }

    // 2026/11/16 新增：仅阻塞既有输出工作线程，取消唤醒等待并抛出携带原令牌的取消异常。
    private static void WaitWithCancellation(int milliseconds, CancellationToken token)
    {
        token.WaitHandle.WaitOne(milliseconds);
        token.ThrowIfCancellationRequested();
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
