// 文件：NoHotkeyDesktopTests.cs
// 描述：在自有前台窗口验证 F6/F8 启停和 Ctrl+F8 停止的原生消息与状态采样。
// 版本：v1.3
// 日期：2026/11/23
// 历史：
// v1.3 2026/11/23 增加单独 F8 与 Ctrl+F8 在普通和 NOHOTKEYS 前台互斥派发的验收。
// v1.2 2026/11/16 验证普通 F6 按住仅触发一次，以及 NOHOTKEYS 下 F6 和 Ctrl+F8 独立状态检测。
// v1.1 2026/11/16 记录派发增量与100ms按住状态，有限观察并恢复后再判断阻断原因。

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseClick.App.Interop;

namespace MouseClick.WindowsTests;

// 2026/11/16 新增：仅供独立显式桌面入口调用，生产热键类和只观察钩子接收同一个受前台保护的 F6。
internal static class NoHotkeyDesktopTests
{
    internal static int Run()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var host = new Form
        {
            Text = "F6 Raw Input 兼容性测试（不发送鼠标输入）",
            Name = "RawHotkeyTestHost",
            StartPosition = FormStartPosition.CenterScreen,
            // 2026/11/16 新增：隔离测试窗口保持可见，仍须确认原生前台后才能发送按键。
            TopMost = true,
            ClientSize = new Size(590, 135)
        };
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "仅向本测试窗口发送 F6、F8、Ctrl+F8，比较 Raw Input 禁用热键前后。\n就绪最多60秒、输入最多10秒；关闭可取消，不发送鼠标输入。"
        });
        HotkeyWindow? hotkeys = null;
        IntPtr hook = IntPtr.Zero;
        bool rawRegistered = false;
        bool finished = false;
        int hookDowns = 0;
        int hookUps = 0;
        int toggles = 0;
        // 2026/11/16 新增：停止组合独立累计，NOHOTKEYS 下只要求事件到达，不要求观察钩子能够收到。
        int stopHookDowns = 0;
        int stopHookUps = 0;
        int stops = 0;
        Exception? failure = null;
        Exception? hookFailure = null;

        // 2026/11/16 新增：保存阶段起点，失败时仍可报告累计值、增量及原生前台，不在钩子回调中写日志。
        string phase = "尚未发送";
        int phaseDowns = 0;
        int phaseUps = 0;
        int phaseToggles = 0;
        int phaseStopDowns = 0;
        int phaseStopUps = 0;
        int phaseStops = 0;

        void BeginPhase(string name)
        {
            phase = name;
            phaseDowns = hookDowns;
            phaseUps = hookUps;
            phaseToggles = toggles;
            phaseStopDowns = stopHookDowns;
            phaseStopUps = stopHookUps;
            phaseStops = stops;
            ReportPhase("发送前");
        }

        void ReportPhase(string position) => Console.WriteLine(
            $"{phase} / {position}：F6 HOOK 增量 down={hookDowns - phaseDowns}, up={hookUps - phaseUps}；"
            + $"ToggleRequested 增量={toggles - phaseToggles}；累计 down={hookDowns}, up={hookUps}, toggle={toggles}；"
            + $"F8 HOOK 增量 down={stopHookDowns - phaseStopDowns}, up={stopHookUps - phaseStopUps}；StopRequested 增量={stops - phaseStops}；"
            + $"前台={NativeMethods.GetForegroundWindow()}，测试窗口={host.Handle}；CanToggle={hotkeys?.CanToggle}；"
            + $"hookFailure={hookFailure?.ToString() ?? "无"}。");

        // 2026/11/16 新增：沿用受前台保护的完整按下/抬起注入，只记录同步返回耗时以辅助辨别消息泵等待。
        void SendPhaseF6()
        {
            var elapsed = Stopwatch.StartNew();
            DesktopTestKeyboard.SendF6(host.Handle);
            ReportPhase($"SendF6 同步返回 {elapsed.Elapsed.TotalMilliseconds:F1}ms");
        }

        // 2026/11/23 新增：选用 F8 时每次先验当前组合，再断言另一个动作没有被误触发。
        async Task CheckSharedF8Async(HotkeyWindow activeHotkeys, string mode)
        {
            string? error = activeHotkeys.Register((int)Keys.F8);
            Check(error is null, $"{mode} 同时注册 F8 和 Ctrl+F8 失败：{error}");
            BeginPhase(mode + " 单独 F8");
            Check(await DesktopTestKeyboard.SendHeldF8Async(host.Handle, cancellation.Token), "未观察到 F8 按住。");
            await Task.Delay(100, cancellation.Token);
            Check(toggles == phaseToggles + 1 && stops == phaseStops, $"{mode} 单独 F8 应只派发一次启停。");
            BeginPhase(mode + " Ctrl+F8");
            Check(await DesktopTestKeyboard.SendHeldControlF8Async(host.Handle, cancellation.Token), "未观察到 Ctrl+F8 按住。");
            await Task.Delay(100, cancellation.Token);
            Check(stops == phaseStops + 1 && toggles == phaseToggles, $"{mode} Ctrl+F8 应只派发一次停止。");
            Console.WriteLine($"通过：{mode} 的 F8 只启停一次，Ctrl+F8 只停止一次。");
        }

        // 2026/11/16 修改：回调仅累计本测试所需的 F6/F8 事件，始终传递，不改变键盘输入行为。
        HookCallback observe = (code, message, data) =>
        {
            try
            {
                if (code >= 0)
                {
                    int key = Marshal.ReadInt32(data);
                    bool down = message.ToInt64() is 0x0100 or 0x0104;
                    bool up = message.ToInt64() is 0x0101 or 0x0105;
                    if (key == 0x75) { if (down) hookDowns++; else if (up) hookUps++; }
                    else if (key == 0x77) { if (down) stopHookDowns++; else if (up) stopHookUps++; }
                }
            }
            catch (Exception exception) { hookFailure = exception; }
            return CallNextHookEx(hook, code, message, data);
        };

        // 2026/11/16 新增：分别尝试全部清理，Raw Input 移除时 hwndTarget 必须为零，失败保留诊断。
        void Cleanup()
        {
            if (rawRegistered)
            {
                if (!RegisterRawInputDevices([Keyboard(0x0001, IntPtr.Zero)], 1, RawDeviceSize))
                    AddFailure(new Win32Exception(Marshal.GetLastPInvokeError(), "移除本测试 Raw Input 注册失败。"));
                else rawRegistered = false;
            }
            if (hook != IntPtr.Zero)
            {
                if (!UnhookWindowsHookEx(hook))
                    AddFailure(new Win32Exception(Marshal.GetLastPInvokeError(), "解除本测试观察钩子失败。"));
                else hook = IntPtr.Zero;
            }
            hotkeys?.Dispose();
            hotkeys = null;
        }

        void AddFailure(Exception exception) => failure = failure is null ? exception : new AggregateException(failure, exception);
        host.FormClosing += (_, args) =>
        {
            if (finished) return;
            args.Cancel = true;
            cancellation.Cancel();
        };
        host.Shown += async (_, _) =>
        {
            try
            {
                Check(ReadRawDevices().Length == 0, "测试要求进程内没有既有 Raw Input 用户，拒绝覆盖原注册。");
                hotkeys = new HotkeyWindow();
                hotkeys.ToggleRequested += () => toggles++;
                hotkeys.StopRequested += () => stops++;
                string? registration = hotkeys.Register();
                Check(hotkeys.CanToggle && hotkeys.CanStop, "现有 F6 / Ctrl+F8 注册失败：" + registration);
                host.Activate();
                Console.WriteLine($"等待测试前台：窗口={host.Handle}，当前前台={NativeMethods.GetForegroundWindow()}，时间={DateTime.Now:HH:mm:ss}。");
                await WaitAsync(() => NativeMethods.GetForegroundWindow() == host.Handle,
                    "自有测试窗口未获得前台", cancellation.Token, TimeSpan.FromSeconds(60));
                // 2026/11/16 新增：允许操作者将自有窗口前置后才开始输入阶段计时。
                cancellation.CancelAfter(TimeSpan.FromSeconds(10));
                // 2026/11/16 修改：前置完成后才安装观察钩子，避免 UI 自动化激活过程等待同一 UI 线程回调。
                hook = SetWindowsHookEx(13, observe, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "安装观察钩子失败。");

                // 2026/11/16 修改：先确认普通基线完整派发，并记录同步发送与消息泵恢复后的不同观察点。
                BeginPhase("普通前台基线");
                SendPhaseF6();
                await WaitAsync(() => hookDowns == 1 && hookUps == 1 && toggles == 1,
                    "普通前台窗口的 F6 基线没有同时到达钩子与热键事件", cancellation.Token);
                Check(hookFailure is null, "观察钩子异常：" + hookFailure?.Message);
                ReportPhase("基线完成");

                // 2026/11/16 新增：普通前台下原生和采样通道都可运行，按住100ms只能触发一次启停。
                BeginPhase("普通前台按住100ms");
                bool normalHeldState = await DesktopTestKeyboard.SendHeldF6Async(host.Handle, cancellation.Token);
                await Task.Delay(500, cancellation.Token);
                int normalHeldToggles = toggles - phaseToggles;
                ReportPhase($"观察完成，GetAsyncKeyState 高位曾置位={normalHeldState}");

                // 2026/11/16 新增：只改变前台键盘的 NOHOTKEYS 标志，不更换热键句柄或生产派发逻辑。
                if (!RegisterRawInputDevices([Keyboard(0x0200, host.Handle)], 1, RawDeviceSize))
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "注册键盘 RIDEV_NOHOTKEYS 失败。");
                rawRegistered = true;
                RawInputDevice[] registered = ReadRawDevices();
                Check(registered.Length == 1 && registered[0].UsagePage == 1 && registered[0].Usage == 6
                    && (registered[0].Flags & 0x0200) != 0, "未读回本测试键盘的 RIDEV_NOHOTKEYS 注册。");
                // 2026/11/16 修改：固定观察500ms，钩子缺失也继续移除注册并验证恢复，不提前归因。
                BeginPhase("NOHOTKEYS 前台");
                SendPhaseF6();
                await Task.Delay(500, cancellation.Token);
                ReportPhase("观察500ms完成");

                // 2026/11/16 新增：在同一 NOHOTKEYS 注册下增加分批按住对照，采样高位状态，不连接生产停止逻辑。
                BeginPhase("NOHOTKEYS 按住100ms");
                bool heldStateObserved = await DesktopTestKeyboard.SendHeldF6Async(host.Handle, cancellation.Token);
                ReportPhase($"按住实验已抬起，GetAsyncKeyState 高位曾置位={heldStateObserved}");
                await Task.Delay(500, cancellation.Token);
                int heldPhaseToggles = toggles - phaseToggles;
                ReportPhase("抬起后观察500ms完成");

                // 2026/11/16 新增：同一 NOHOTKEYS 前台下紧急停止也应由独立状态检测恰好派发一次。
                BeginPhase("NOHOTKEYS Ctrl+F8按住100ms");
                bool heldStopState = await DesktopTestKeyboard.SendHeldControlF8Async(host.Handle, cancellation.Token);
                await Task.Delay(500, cancellation.Token);
                int heldPhaseStops = stops - phaseStops;
                ReportPhase($"观察完成，组合 GetAsyncKeyState 高位曾置位={heldStopState}");

                // 2026/11/23 新增：在状态采样路径与恢复后的原生路径分别验证 F8 同键分流。
                await CheckSharedF8Async(hotkeys, "NOHOTKEYS");
                Check(hotkeys.Register() is null, "切回 F6 基线注册失败。");

                // 2026/11/16 新增：先恢复进程原始空注册，再验证相同热键恢复；最终断言可暴露生产兼容性缺口。
                if (!RegisterRawInputDevices([Keyboard(0x0001, IntPtr.Zero)], 1, RawDeviceSize))
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "恢复 Raw Input 原始空注册失败。");
                rawRegistered = false;
                Check(ReadRawDevices().Length == 0, "移除后仍存在本测试 Raw Input 注册。");
                // 2026/11/16 修改：恢复阶段也先观察500ms再集中判断，区分钩子失效和热键单独缺失。
                BeginPhase("移除后恢复基线");
                SendPhaseF6();
                await Task.Delay(500, cancellation.Token);
                ReportPhase("观察500ms完成，Raw Input 注册已移除");
                Check(hookFailure is null, "观察钩子异常：" + hookFailure?.Message);
                Check(hookDowns - phaseDowns == 1 && hookUps - phaseUps == 1 && toggles - phaseToggles == 1,
                    "移除 NOHOTKEYS 后相同钩子/热键未完整恢复；本次结果不能只归因为 NOHOTKEYS 热键派发。");
                // 2026/11/16 修改：零时长同批按键仅记录；按住实验检查真实事件次数，不把钩子可见性当成通过条件。
                Check(normalHeldState && normalHeldToggles == 1,
                    $"普通前台 F6 按住应恰好触发一次，实际 {normalHeldToggles}；双通道可能重复或遗漏。");
                Check(heldStateObserved && heldPhaseToggles == 1,
                    $"NOHOTKEYS 前台 F6 按住应恰好触发一次，实际 {heldPhaseToggles}，按键状态曾置位={heldStateObserved}。");
                Check(heldStopState && heldPhaseStops == 1,
                    $"NOHOTKEYS 前台 Ctrl+F8 按住应恰好触发一次停止，实际 {heldPhaseStops}，组合状态曾置位={heldStopState}。");
                await CheckSharedF8Async(hotkeys, "普通前台");
            }
            // 2026/11/16 修改：失败先保留阶段计数与前台，随后仍由 finally 解除全部注册。
            catch (Exception exception)
            {
                ReportPhase("异常退出");
                AddFailure(exception);
            }
            finally
            {
                Cleanup();
                finished = true;
                host.Close();
            }
        };

        try { Application.Run(host); }
        catch (Exception exception) { AddFailure(exception); }
        finally
        {
            Cleanup();
            GC.KeepAlive(observe);
        }
        Console.WriteLine(failure is null ? "通过：普通 F6 按住无重复，NOHOTKEYS 前台 F6 与 Ctrl+F8 按住均派发一次。"
            : $"失败：NOHOTKEYS 热键验收 — {failure.GetType().Name}: {failure.Message}");
        return failure is null ? 0 : 1;
    }

    // 2026/11/16 新增：每个观察阶段最多两秒，全部等待同时服从十秒总限时及用户关闭取消。
    private static async Task WaitAsync(Func<bool> ready, string message, CancellationToken token, TimeSpan? timeout = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!ready())
        {
            token.ThrowIfCancellationRequested();
            if (elapsed.Elapsed > (timeout ?? TimeSpan.FromSeconds(2))) throw new TimeoutException(message);
            await Task.Delay(10, token);
        }
        token.ThrowIfCancellationRequested();
    }

    // 2026/11/16 新增：查询当前进程注册验证隔离前提及恢复结果，不读取其他进程或设备输入内容。
    private static RawInputDevice[] ReadRawDevices()
    {
        uint count = 16;
        var devices = new RawInputDevice[count];
        uint copied = GetRegisteredRawInputDevices(devices, ref count, RawDeviceSize);
        if (copied == uint.MaxValue && Marshal.GetLastPInvokeError() == 122)
        {
            devices = new RawInputDevice[count];
            copied = GetRegisteredRawInputDevices(devices, ref count, RawDeviceSize);
        }
        if (copied == uint.MaxValue) throw new Win32Exception(Marshal.GetLastPInvokeError(), "读取进程 Raw Input 注册失败。");
        return devices.Take((int)copied).ToArray();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // 2026/11/16 新增：键盘 UsagePage=1、Usage=6；移除使用相同设备标识并指定空窗口。
    private static RawInputDevice Keyboard(uint flags, IntPtr target) => new()
    {
        UsagePage = 1, Usage = 6, Flags = flags, Target = target
    };
    private static readonly uint RawDeviceSize = (uint)Marshal.SizeOf<RawInputDevice>();
    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal IntPtr Target;
    }
    private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices([Out] RawInputDevice[] devices, ref uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookCallback callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
