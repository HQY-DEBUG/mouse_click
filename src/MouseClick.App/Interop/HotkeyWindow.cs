// 文件：HotkeyWindow.cs
// 描述：使用消息窗口注册全局启停、紧急停止和取点快捷键。
// 版本：v1.1
// 日期：2026/11/15
// 历史：
// v1.1 2026/11/15 新增停止键占用时的组合键回退，并校验消息中的组合键。
// v1.0 2026/11/15 新增快捷键注册、冲突报告、消息分发和关闭注销。

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseClick.App.Interop;

// 2026/11/15 新增：由 UI 线程创建、注册及释放，调用方只应在连点停止时更换快捷键。
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const int ToggleId = 1;
    private const int StopId = 2;
    private const int CaptureId = 3;
    private const uint NoRepeat = 0x4000;
    // 2026/11/15 新增：F8 被占用时使用 Ctrl+Alt+F8，并保存当前停止键的修饰键。
    private const uint StopFallbackModifiers = 0x0003;
    private const int HotkeyAlreadyRegistered = 1409;
    private readonly Dictionary<int, string> _registered = new();
    private int _toggleVirtualKey = 0x75;
    private uint _stopModifiers;
    private bool _disposed;

    public event Action? ToggleRequested;
    public event Action? StopRequested;
    public event Action? CaptureRequested;

    public bool CanToggle { get; private set; }
    public bool CanStop { get; private set; }
    public bool CanCapture { get; private set; }

    // 2026/11/15 新增：供主界面显示实际注册成功的停止快捷键。
    public string StopHotkeyText { get; private set; } = "F8";

    // 2026/11/15 新增：HWND_MESSAGE 创建不显示、不抢焦点的专用消息窗口。
    public HotkeyWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "MouseClick.Hotkeys",
            Parent = new IntPtr(-3)
        });
    }

    // 2026/11/15 修改：先注销旧注册，停止键使用备用组合键回退，其余失败项目保留独立诊断。
    public string? Register(int toggleVirtualKey = 0x75)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        List<string> failures = UnregisterAll();
        if (failures.Count > 0)
        {
            return "旧快捷键未能完全注销，请关闭程序后重试。" + Environment.NewLine +
                string.Join(Environment.NewLine, failures);
        }

        if (toggleVirtualKey is not (0x75 or 0x78 or 0x79))
        {
            return "开始/停止快捷键仅支持 F6、F9 或 F10。";
        }

        string toggleName = toggleVirtualKey switch { 0x75 => "F6", 0x78 => "F9", _ => "F10" };
        _toggleVirtualKey = toggleVirtualKey;
        CanStop = RegisterStop(failures);
        CanCapture = TryRegister(CaptureId, 0x76, "F7（获取鼠标位置）", failures);
        CanToggle = TryRegister(ToggleId, toggleVirtualKey, $"{toggleName}（开始/停止）", failures);
        return failures.Count == 0 ? null : string.Join(Environment.NewLine, failures);
    }

    // 2026/11/15 新增：只有主键与备用键均失败才报告停止键不可用，备用键成功时不保留主键占用错误。
    private bool RegisterStop(List<string> failures)
    {
        StopHotkeyText = "F8";
        _stopModifiers = 0;
        List<string> stopFailures = new();
        if (TryRegister(StopId, 0x77, "F8（紧急停止）", stopFailures))
        {
            return true;
        }

        if (TryRegister(StopId, 0x77, "Ctrl+Alt+F8（紧急停止）", stopFailures, StopFallbackModifiers))
        {
            StopHotkeyText = "Ctrl+Alt+F8";
            _stopModifiers = StopFallbackModifiers;
            return true;
        }

        failures.AddRange(stopFailures);
        return false;
    }

    // 2026/11/15 修改：支持组合键修饰符，所有快捷键仍启用 NoRepeat 并明确报告注册失败。
    private bool TryRegister(int id, int virtualKey, string name, List<string> failures, uint modifiers = 0)
    {
        if (RegisterHotKey(Handle, id, NoRepeat | modifiers, (uint)virtualKey))
        {
            _registered.Add(id, name);
            return true;
        }

        int error = Marshal.GetLastPInvokeError();
        failures.Add(error == HotkeyAlreadyRegistered
            ? $"{name} 已被其他程序占用，请释放该快捷键或更换开始/停止键。"
            : $"{name} 注册失败：Windows 错误 {error}（{new Win32Exception(error).Message}）。");
        return false;
    }

    // 2026/11/15 修改：同时校验虚拟键及组合键，避免切换到备用停止键后分发旧队列消息。
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage && !_disposed)
        {
            int virtualKey = (int)((message.LParam.ToInt64() >> 16) & 0xFFFF);
            uint modifiers = (uint)(message.LParam.ToInt64() & 0x000F);
            switch (message.WParam.ToInt32())
            {
                case ToggleId when CanToggle && virtualKey == _toggleVirtualKey && modifiers == 0:
                    ToggleRequested?.Invoke();
                    return;
                case StopId when CanStop && virtualKey == 0x77 && modifiers == _stopModifiers:
                    StopRequested?.Invoke();
                    return;
                case CaptureId when CanCapture && virtualKey == 0x76 && modifiers == 0:
                    CaptureRequested?.Invoke();
                    return;
            }
        }

        base.WndProc(ref message);
    }

    // 2026/11/15 新增：所有注册均尝试注销；注销失败保留记录供关闭时重试，同时停止消息分发。
    private List<string> UnregisterAll()
    {
        CanToggle = false;
        CanStop = false;
        CanCapture = false;
        List<string> failures = new();
        foreach (KeyValuePair<int, string> registration in _registered.ToArray())
        {
            if (UnregisterHotKey(Handle, registration.Key))
            {
                _registered.Remove(registration.Key);
                continue;
            }

            int error = Marshal.GetLastPInvokeError();
            failures.Add($"{registration.Value} 注销失败：Windows 错误 {error}（{new Win32Exception(error).Message}）。");
        }

        return failures;
    }

    // 2026/11/15 新增：关闭时注销全部快捷键并销毁消息窗口，注销异常写入诊断日志而不阻止清理。
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            foreach (string failure in UnregisterAll())
            {
                Trace.TraceError(failure);
            }
        }
        finally
        {
            DestroyHandle();
            _registered.Clear();
            GC.SuppressFinalize(this);
        }
    }

    // 2026/11/15 新增：窗口句柄绑定注册，销毁前显式成对注销。
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
