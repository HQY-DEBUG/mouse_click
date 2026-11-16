// 文件：HotkeyWindow.cs
// 描述：注册全局启停、紧急停止和取点快捷键，并以按键状态补充被前台禁用的启停消息。
// 版本：v1.4
// 日期：2026/11/16
// 历史：
// v1.4 2026/11/16 将去重时间戳放在状态读取之后，避免读取期间新按下被重复派发。
// v1.3 2026/11/16 增加启停键状态采样及双通道去重，保留注册冲突和取点行为。
// v1.2 2026/11/15 固定 Ctrl+F8 紧急停止，移除自动换键并拒绝旧组合消息。

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
    // 2026/11/15 修改：紧急停止固定使用 Ctrl，不再根据快捷键占用情况自动换键。
    private const uint StopModifiers = 0x0002;
    private const int HotkeyAlreadyRegistered = 1409;
    private readonly Dictionary<int, string> _registered = new();
    // 2026/11/16 新增：仅采样两个已注册的启停组合，单独去重；不安装键盘钩子、不采集文字或扩展 F7。
    private readonly System.Windows.Forms.Timer _keyStateTimer = new() { Interval = 10 };
    private readonly HotkeyPressGate _toggleGate = new();
    private readonly HotkeyPressGate _stopGate = new();
    private int _toggleVirtualKey = 0x75;
    private bool _disposed;

    public event Action? ToggleRequested;
    public event Action? StopRequested;
    public event Action? CaptureRequested;

    public bool CanToggle { get; private set; }
    public bool CanStop { get; private set; }
    public bool CanCapture { get; private set; }

    // 2026/11/15 修改：注册成功或失败时均显示固定的 Ctrl+F8，主界面无需推测备用组合。
    public string StopHotkeyText => "Ctrl+F8";

    // 2026/11/15 新增：HWND_MESSAGE 创建不显示、不抢焦点的专用消息窗口。
    public HotkeyWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "MouseClick.Hotkeys",
            Parent = new IntPtr(-3)
        });
        // 2026/11/16 新增：使用现有 UI 消息泵调度状态读取，事件仍沿 MainForm 已有启停订阅派发。
        _keyStateTimer.Tick += PollKeyStates;
    }

    // 2026/11/15 修改：先注销旧注册，再注册固定 Ctrl+F8；失败时保留独立诊断并禁止启动。
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
        CanStop = TryRegister(StopId, 0x77, "Ctrl+F8（紧急停止）", failures, StopModifiers);
        CanCapture = TryRegister(CaptureId, 0x76, "F7（获取鼠标位置）", failures);
        CanToggle = TryRegister(ToggleId, toggleVirtualKey, $"{toggleName}（开始/停止）", failures);
        // 2026/11/16 新增：已按住的键先等待释放，注册失败的组合不可经采样绕过冲突限制。
        _toggleGate.Reset(IsKeyDown(_toggleVirtualKey));
        _stopGate.Reset(IsKeyDown(0x77));
        if (CanToggle || CanStop) _keyStateTimer.Start();
        return failures.Count == 0 ? null : string.Join(Environment.NewLine, failures);
    }

    // [废弃] 2026/11/15 用户指定固定 Ctrl+F8，替代旧 F8 及 Ctrl+Alt+F8 回退，计划在 v1.5 清理。
    // TryRegister(StopId, 0x77, "F8（紧急停止）", stopFailures);
    // TryRegister(StopId, 0x77, "Ctrl+Alt+F8（紧急停止）", stopFailures, 0x0003);

    // 2026/11/15 修改：保留 NoRepeat，固定停止键占用时明确提示禁止启动且不会自动换键。
    private bool TryRegister(int id, int virtualKey, string name, List<string> failures, uint modifiers = 0)
    {
        if (RegisterHotKey(Handle, id, NoRepeat | modifiers, (uint)virtualKey))
        {
            _registered.Add(id, name);
            return true;
        }

        int error = Marshal.GetLastPInvokeError();
        failures.Add(error == HotkeyAlreadyRegistered
            ? id == StopId
                ? $"{name} 已被其他程序占用，已禁止启动；请释放后重新选择开始/停止快捷键重试。不会自动更换停止键。"
                : $"{name} 已被其他程序占用，请释放该快捷键或更换开始/停止键。"
            : $"{name} 注册失败：Windows 错误 {error}（{new Win32Exception(error).Message}）。");
        return false;
    }

    // 2026/11/16 修改：保留注册及精确组合校验，只去掉被状态采样覆盖的迟到消息，F7 仍仅由原生派发。
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage && !_disposed)
        {
            int virtualKey = (int)((message.LParam.ToInt64() >> 16) & 0xFFFF);
            uint modifiers = (uint)(message.LParam.ToInt64() & 0x000F);
            switch (message.WParam.ToInt32())
            {
                case ToggleId when CanToggle && virtualKey == _toggleVirtualKey && modifiers == 0:
                    if (_toggleGate.Native(unchecked((uint)GetMessageTime()), IsKeyDown(_toggleVirtualKey)))
                        ToggleRequested?.Invoke();
                    return;
                case StopId when CanStop && virtualKey == 0x77 && modifiers == StopModifiers:
                    if (_stopGate.Native(unchecked((uint)GetMessageTime()), IsKeyDown(0x77)))
                        StopRequested?.Invoke();
                    return;
                case CaptureId when CanCapture && virtualKey == 0x76 && modifiers == 0:
                    CaptureRequested?.Invoke();
                    return;
            }
        }

        base.WndProc(ref message);
    }

    // 2026/11/16 新增：状态采样只识别无修饰配置 Fn 与精确 Ctrl+F8，Fn/F8 释放之前不重复派发。
    private void PollKeyStates(object? sender, EventArgs args)
    {
        if (_disposed) return;
        uint modifiers = (IsKeyDown(0x12) ? 1u : 0u) | (IsKeyDown(0x11) ? 2u : 0u)
            | (IsKeyDown(0x10) ? 4u : 0u) | (IsKeyDown(0x5B) || IsKeyDown(0x5C) ? 8u : 0u);
        // 2026/11/16 修改：先读取状态再记采样时间，避免读取期间新按下的原生消息比采样时间更新而重复派发。
        bool toggleDown = IsKeyDown(_toggleVirtualKey);
        bool stopDown = IsKeyDown(0x77);
        uint timestamp = unchecked((uint)Environment.TickCount);
        bool toggle = _toggleGate.Sample(toggleDown, CanToggle && modifiers == 0, timestamp);
        bool stop = _stopGate.Sample(stopDown, CanStop && modifiers == StopModifiers, timestamp);
        if (stop) StopRequested?.Invoke();
        if (toggle) ToggleRequested?.Invoke();
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    // 2026/11/15 新增：所有注册均尝试注销；注销失败保留记录供关闭时重试，同时停止消息分发。
    private List<string> UnregisterAll()
    {
        // 2026/11/16 新增：注销前先停止采样并清空去重，旧注册失败也不能继续由状态检测派发。
        _keyStateTimer.Stop();
        _toggleGate.Reset();
        _stopGate.Reset();
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
            // 2026/11/16 新增：停止后释放定时器，防止消息窗口销毁后保留采样回调。
            _keyStateTimer.Dispose();
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

    // 2026/11/16 新增：仅读取按键高位与消息毫秒时间；两条派发通道使用相同系统开机时钟去重。
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern int GetMessageTime();
}
