// 文件：HotkeyProbe.cs
// 描述：有界观察控制键低级钩子与现有热键事件，记录必要状态，不发送或吞掉输入。
// 版本：v1.0
// 日期：2026/11/16
// 历史：v1.0 2026/11/16 新增最长两分钟的独立物理热键诊断窗口与异步日志。

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using MouseClick.App.Interop;

namespace MouseClick.WindowsTests;

// 2026/11/16 新增：仅由 --hotkey-probe 进入；不创建 MainForm、点击引擎或任何输入注入器。
internal static class HotkeyProbe
{
    internal static int Run()
    {
        try
        {
            string directory = Path.Combine(FindRepositoryRoot(), "artifacts");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"hotkey-probe-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}.txt");
            using var log = new ProbeLog(path);
            using var window = new ProbeForm(log, path);
            Application.Run(window);
            return window.Result;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"控制键诊断失败：{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    // 2026/11/16 新增：从测试输出目录向上定位现有仓库标记，不扫描其他项目或读取用户配置。
    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "version.props"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests", "MouseClick.WindowsTests")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("无法从测试输出目录定位仓库根目录，未创建诊断日志。");
    }

    // 2026/11/16 新增：钩子只入队，文件和 Console 写入放到后台，避免在键盘回调中执行磁盘操作。
    private sealed class ProbeLog : IDisposable
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        private readonly Task writer;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        internal TimeSpan Elapsed => elapsed.Elapsed;
        internal Exception? Failure => writer.IsFaulted ? writer.Exception?.GetBaseException() : null;

        internal ProbeLog(string path)
        {
            var stream = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            writer = Task.Run(async () =>
            {
                using (stream)
                {
                    await foreach (string line in lines.Reader.ReadAllAsync())
                    {
                        await stream.WriteLineAsync(line);
                        Console.WriteLine(line);
                    }
                }
            });
            Write("开始", $"日志={path}；进程PID={Environment.ProcessId}；只观察控制键，不发送或阻止输入；上限120秒。");
        }

        internal void Write(string source, string detail)
        {
            string line = $"{DateTimeOffset.Now:O} elapsedMs={elapsed.Elapsed.TotalMilliseconds:F3} {source} {detail}";
            if (!lines.Writer.TryWrite(line)) throw new InvalidOperationException("诊断日志已关闭，不能继续记录。");
        }

        // 2026/11/16 新增：窗口与钩子释放后收尾日志；后台文件错误向入口传播，不静默丢失证据。
        public void Dispose()
        {
            lines.Writer.TryComplete();
            writer.GetAwaiter().GetResult();
        }
    }

    // 2026/11/16 新增：用户自行切换游戏并按键，独立窗口仅显示状态及期限，关闭即释放全部观察资源。
    private sealed class ProbeForm : Form
    {
        private readonly ProbeLog log;
        private readonly Label state = new() { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(16, 4, 16, 4) };
        private readonly System.Windows.Forms.Timer refresh = new() { Interval = 100 };
        private readonly HookCallback callback;
        private HotkeyWindow? hotkeys;
        private IntPtr hook;
        private bool released;
        private int observedDowns;
        private int toggleEvents;
        private int stopEvents;
        private string? registrationError;
        private string? callbackFailure;
        private string endReason = "用户关闭诊断窗口";
        internal int Result { get; private set; }

        internal ProbeForm(ProbeLog log, string path)
        {
            this.log = log;
            callback = ObserveKeyboard;
            Text = "F6 / Ctrl+F8 控制键诊断（不发送输入）";
            Name = "HotkeyProbeForm";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 330);
            MinimumSize = new Size(680, 350);
            Font = new Font("Microsoft YaHei UI", 10);
            var instructions = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                Text = "请先退出鼠标连点器，避免它占用 F6 / Ctrl+F8。\n\n"
                    + "切换到欢乐斗地主游戏窗口，手动物理按 F6 两次，再按 Ctrl+F8。\n"
                    + "可依次切换四个游戏窗口重复；本窗口无需保持前台。\n\n"
                    + "诊断不会发送鼠标或键盘输入，也不会吞键。\n"
                    + "只记录 F6/F8/F9/F10 与必要修饰键状态，不记录输入文字。\n"
                    + "最多 120 秒自动结束；关闭本窗口可立即结束并解除注册。\n\n"
                    + "日志：" + path
            };
            Controls.Add(instructions);
            Controls.Add(state);
            refresh.Tick += (_, _) => UpdateState();
        }

        // 2026/11/16 新增：沿用生产 HotkeyWindow 的注册及派发契约，额外钩子始终向下一项传递事件。
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                hotkeys = new HotkeyWindow();
                hotkeys.ToggleRequested += () => RecordHotkey("F6", ref toggleEvents);
                hotkeys.StopRequested += () => RecordHotkey("Ctrl+F8", ref stopEvents);
                registrationError = hotkeys.Register();
                if (registrationError is not null) Result = 1;
                log.Write("注册", $"hwnd=0x{hotkeys.Handle.ToInt64():X} CanToggle={hotkeys.CanToggle} CanStop={hotkeys.CanStop} "
                    + $"CanCapture={hotkeys.CanCapture}；{registrationError ?? "成功"}；现有实现同时注册F7，诊断不订阅其动作。");
                hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "安装只观察键盘钩子失败。");
                log.Write("观察就绪", $"hook=0x{hook.ToInt64():X}；{ForegroundDescription()}；修饰键={ReadModifiers()}");
                refresh.Start();
                UpdateState();
            }
            catch (Exception exception)
            {
                Result = 1;
                endReason = "初始化失败";
                log.Write("错误", $"{exception.GetType().Name}: {exception.Message}");
                Close();
            }
        }

        // 2026/11/16 新增：记录生产类合并原生消息与状态采样后的事件，不能将计数直接等同于 WM_HOTKEY。
        private void RecordHotkey(string key, ref int count)
        {
            count++;
            log.Write("生产启停事件", $"key={key} observedTick={unchecked((uint)Environment.TickCount)} "
                + $"修饰键={ReadModifiers()} {ForegroundDescription()} 次数={count}");
        }

        // 2026/11/16 新增：白名单限定控制键和修饰键；回调不写磁盘、不等待、不返回拦截值。
        private IntPtr ObserveKeyboard(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code >= 0 && !released)
                {
                    KeyboardData key = Marshal.PtrToStructure<KeyboardData>(data);
                    string? name = KeyName(key.VirtualKey);
                    if (name is not null)
                    {
                        bool down = message.ToInt64() is 0x0100 or 0x0104;
                        if (down && key.VirtualKey is 0x75 or 0x77 or 0x78 or 0x79) observedDowns++;
                        log.Write("HOOK", $"key={name} phase={(down ? "down" : "up")} msgTime={key.Time} "
                            + $"flags=0x{key.Flags:X2} injected={(key.Flags & 0x10) != 0} lowerIL={(key.Flags & 0x02) != 0} "
                            + $"修饰键={ReadModifiers(key.VirtualKey, down)} {ForegroundDescription()}");
                    }
                }
            }
            catch (Exception exception)
            {
                // 2026/11/16 新增：异常留给定时器报告并关闭，绝不让托管异常越过原生钩子边界。
                callbackFailure = $"{exception.GetType().Name}: {exception.Message}";
            }
            return CallNextHookEx(hook, code, message, data);
        }

        // 2026/11/16 新增：窗口消息泵显示剩余时间，发现日志或回调错误立即结束，上限为120秒。
        private void UpdateState()
        {
            int remaining = Math.Max(0, 120 - (int)log.Elapsed.TotalSeconds);
            state.Text = $"剩余 {remaining} 秒　控制键按下：{observedDowns}　F6 热键事件：{toggleEvents}　Ctrl+F8 热键事件：{stopEvents}\n"
                + (registrationError is null ? "请在游戏窗口前台按键；生产启停事件包含原生消息与状态采样，不能仅据此判断消息来源。" : "热键存在注册冲突，日志已注明；请结束诊断、退出占用程序后重新运行。");
            Exception? loggingFailure = log.Failure;
            if (callbackFailure is not null || loggingFailure is not null)
            {
                Result = 1;
                endReason = "观察或日志错误";
                if (loggingFailure is null) log.Write("错误", callbackFailure!);
                else Console.Error.WriteLine("诊断日志写入失败：" + loggingFailure.Message);
                Close();
            }
            else if (log.Elapsed >= TimeSpan.FromSeconds(120))
            {
                endReason = "达到120秒期限";
                Close();
            }
        }

        // 2026/11/16 新增：普通关闭、系统关闭和直接释放均走同一清理，不保留全局钩子或热键。
        protected override void Dispose(bool disposing)
        {
            if (disposing && !released)
            {
                released = true;
                refresh.Stop();
                refresh.Dispose();
                if (hook != IntPtr.Zero)
                {
                    if (!UnhookWindowsHookEx(hook))
                    {
                        Result = 1;
                        log.Write("解除钩子失败", new Win32Exception(Marshal.GetLastPInvokeError()).Message);
                    }
                    hook = IntPtr.Zero;
                }
                hotkeys?.Dispose();
                log.Write("结束", $"原因={endReason}；控制键按下={observedDowns}；F6热键事件={toggleEvents}；Ctrl+F8热键事件={stopEvents}；"
                    + "已执行钩子与热键释放；观察结果不等同于目标程序兼容性验证。");
            }
            base.Dispose(disposing);
        }
    }

    // 2026/11/16 新增：仅允许以下键名进入日志，其他键盘事件直接传递且不记录。
    private static string? KeyName(uint key) => key switch
    {
        0x75 => "F6", 0x77 => "F8", 0x78 => "F9", 0x79 => "F10",
        0x10 => "Shift", 0x11 => "Ctrl", 0x12 => "Alt",
        0xA0 => "LShift", 0xA1 => "RShift", 0xA2 => "LCtrl", 0xA3 => "RCtrl",
        0xA4 => "LAlt", 0xA5 => "RAlt", 0x5B => "LWin", 0x5C => "RWin",
        _ => null
    };

    // 2026/11/16 新增：低级回调早于当前键异步状态更新，用本次down/up覆盖该键，其他修饰键读取现状。
    private static string ReadModifiers(uint changedKey = 0, bool down = false)
    {
        bool Pressed(uint key) => key == changedKey ? down : (GetAsyncKeyState((int)key) & 0x8000) != 0;
        bool shift = changedKey == 0x10 ? down : Pressed(0xA0) || Pressed(0xA1);
        bool control = changedKey == 0x11 ? down : Pressed(0xA2) || Pressed(0xA3);
        bool alt = changedKey == 0x12 ? down : Pressed(0xA4) || Pressed(0xA5);
        bool windows = Pressed(0x5B) || Pressed(0x5C);
        return $"Ctrl:{control},Alt:{alt},Shift:{shift},Win:{windows}";
    }

    // 2026/11/16 新增：只记录前台窗口句柄和PID，不读取标题、应用文本或用户输入内容。
    private static string ForegroundDescription()
    {
        IntPtr foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint processId);
        return $"foreground=0x{foreground.ToInt64():X} foregroundPid={processId}";
    }

    // 2026/11/16 新增：公开User32观察接口及完整原生结构；本文件没有输入注入API。
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        internal uint VirtualKey;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookCallback callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    private static extern int GetMessageTime();
}
