// 文件：MainFormDesktopTests.cs
// 描述：在独立配置和自有目标窗口内验证真实主窗口的全局启动、停止及再次启动。
// 版本：v1.1
// 日期：2026/11/15
// 历史：
// v1.1 2026/11/15 五秒延时按秒设置并检查 s 状态文案。
// v1.0 2026/11/15 新增 F6、Ctrl+F8、启动延时取消及主窗口生命周期桌面验收。

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using MouseClick.App;
using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/15 新增：通过生产窗体及真实热键处理运行，不读取或覆盖用户的 LocalApplicationData 配置。
internal static class MainFormDesktopTests
{
    internal static async Task RunAsync(TestWindow window, CancellationToken token)
    {
        TestTarget[] targets = window.SnapshotTargets();
        string settingsPath = Path.Combine(Path.GetTempPath(), "MouseClick.WindowsTests",
            Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(settingsPath);
        store.Save(new ClickSettings
        {
            Mode = TargetMode.Sequence,
            Points = targets.Select(target => target.Center).ToArray(),
            RepeatCount = 2,
            StartDelayMilliseconds = 0,
            IntervalMilliseconds = 50,
            ToggleHotkey = 0x75
        });
        Console.WriteLine("主窗口验收独立配置（保留供诊断）：" + settingsPath);

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var form = new MainForm(store, new GuardedOutput(targets, lifetime.Token));
        Button startButton = Find<Button>(form, "StartButton");
        Button stopButton = Find<Button>(form, "StopButton");
        NumericUpDown repeat = Find<NumericUpDown>(form, "RepeatInput");
        NumericUpDown delay = Find<NumericUpDown>(form, "DelayInput");
        Label status = Find<Label>(form, "RunStatus");
        Label counts = Find<Label>(form, "RunCounts");
        Exception? failure = null;
        try
        {
            // 2026/11/15 新增：显示真实主窗口完成热键注册，再把输入焦点交还专用目标；冲突直接失败。
            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            form.Shown += (_, _) => shown.TrySetResult();
            form.Show();
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(2), token);
            Check(startButton.Enabled && !stopButton.Enabled &&
                status.Text.StartsWith("准备就绪。", StringComparison.Ordinal),
                "主窗口未成功注册全部验收热键，不能跳过全局快捷键验证。");
            await ActivateTargetWindowAsync(window, targets, token);

            // 2026/11/15 新增：F6 在主窗口没有焦点时启动三点两轮，独立核对原生消息和界面计数。
            int first = window.Messages.Count;
            DesktopTestKeyboard.SendF6(window.Handle);
            await WaitAsync(() => stopButton.Enabled, "F6 没有启动主窗口", token);
            await WaitAsync(() => !stopButton.Enabled && status.Text == "已完成设定轮数。" &&
                window.Messages.Count >= first + 12, "三点两轮未正常完成", token);
            await Task.Delay(40, token);
            AssertSequence(window, first, targets, 6);
            AssertCounts(counts, 6, 2);
            Check(startButton.Enabled, "完成运行后开始按钮没有恢复。");
            Console.WriteLine("通过：真实 MainForm 在其他窗口前台时由 F6 启动，三点两轮收到 12 条正确消息，界面为 6 次动作 / 2 轮。");

            // 2026/11/15 新增：持续运行至少四个完整动作后发送真实 Ctrl+F8，测量停止延迟并观察静默期。
            repeat.Value = 0;
            first = window.Messages.Count;
            DesktopTestKeyboard.SendF6(window.Handle);
            await WaitAsync(() => stopButton.Enabled && window.Messages.Skip(first)
                .Count(message => message.Phase == MousePhase.Up) >= 4,
                "持续运行未收到至少四个完整动作", token);
            var stopElapsed = Stopwatch.StartNew();
            DesktopTestKeyboard.SendControlF8(window.Handle);
            await WaitAsync(() => !stopButton.Enabled && status.Text == "已停止。",
                "Ctrl+F8 未停止持续运行", token);
            stopElapsed.Stop();
            int stoppedCount = window.Messages.Count;
            await Task.Delay(400, token);
            Check(window.Messages.Count == stoppedCount, "主窗口报告停止后仍收到新的鼠标消息。");
            int stoppedMessages = stoppedCount - first;
            Check(stoppedMessages >= 8 && stoppedMessages % 2 == 0,
                $"停止后应保留至少四组完整 down/up，实际 {stoppedMessages} 条消息。");
            AssertSequence(window, first, targets, stoppedMessages / 2);
            AssertCounts(counts, stoppedMessages / 2, stoppedMessages / 6);
            Console.WriteLine($"通过：真实 MainForm 持续点击由 Ctrl+F8 停止，停止延迟 {stopElapsed.Elapsed.TotalMilliseconds:F1} 毫秒；400 毫秒内无新增鼠标消息。");

            // 2026/11/15 修改：界面以秒设置五秒启动延时，等待计数归零后用 Ctrl+F8 取消，不能产生点击。
            delay.Value = 5;
            first = window.Messages.Count;
            DesktopTestKeyboard.SendF6(window.Handle);
            await WaitAsync(() => stopButton.Enabled && counts.Text == CountText(0, 0) &&
                status.Text.Contains("启动延时 5 s", StringComparison.Ordinal),
                "主窗口未进入五秒启动延时", token);
            stopElapsed.Restart();
            DesktopTestKeyboard.SendControlF8(window.Handle);
            await WaitAsync(() => !stopButton.Enabled && status.Text == "已停止。",
                "Ctrl+F8 未取消启动延时", token);
            stopElapsed.Stop();
            await Task.Delay(400, token);
            Check(window.Messages.Count == first, "启动延时被取消后出现了鼠标点击。");
            AssertCounts(counts, 0, 0);
            Console.WriteLine($"通过：真实 MainForm 的 5 s 启动延时被 Ctrl+F8 取消，停止延迟 {stopElapsed.Elapsed.TotalMilliseconds:F1} 毫秒，零点击。");

            // 2026/11/15 新增：取消后再次由 F6 完成三点一轮，验证主窗口运行状态及计数已正确复位。
            delay.Value = 0;
            repeat.Value = 1;
            first = window.Messages.Count;
            DesktopTestKeyboard.SendF6(window.Handle);
            await WaitAsync(() => stopButton.Enabled, "取消后 F6 不能再次启动", token);
            await WaitAsync(() => !stopButton.Enabled && status.Text == "已完成设定轮数。" &&
                window.Messages.Count >= first + 6, "取消后再次运行未正常完成", token);
            await Task.Delay(40, token);
            AssertSequence(window, first, targets, 3);
            AssertCounts(counts, 3, 1);
            Check(startButton.Enabled, "再次完成后开始按钮没有恢复。");
            Console.WriteLine("通过：真实 MainForm 停止后可再次由 F6 完成三点一轮，界面为 3 次动作 / 1 轮。");
        }
        catch (Exception exception)
        {
            // 2026/11/15 新增：保留原始失败和界面状态，避免启动失败或热键冲突被表述为输入数量异常。
            failure = new InvalidOperationException("MainForm 桌面验收失败：" + exception.Message + " " +
                DescribeUi(startButton, stopButton, status, counts), exception);
        }
        finally
        {
            // 2026/11/15 新增：任何失败先禁止后续输出，再通过真实停止按钮等待结束后关闭；清理不依赖已取消的测试令牌。
            lifetime.Cancel();
            try
            {
                if (!form.IsDisposed)
                {
                    if (stopButton.Enabled) stopButton.PerformClick();
                    await WaitAsync(() => form.IsDisposed || !stopButton.Enabled,
                        "清理时主窗口未结束运行", CancellationToken.None);
                    if (!form.IsDisposed) form.Close();
                    Check(form.IsDisposed, "停止后主窗口未正常关闭，热键资源可能未释放。");
                }
            }
            catch (Exception exception)
            {
                var cleanupFailure = new InvalidOperationException("MainForm 清理失败：" + exception.Message + " " +
                    DescribeUi(startButton, stopButton, status, counts), exception);
                failure = failure is null ? cleanupFailure : new AggregateException(failure, cleanupFailure);
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // 2026/11/15 新增：通过公开控件名称读取真实界面，控件缺失或重名直接使验收失败。
    private static T Find<T>(Control form, string name) where T : Control =>
        form.Controls.Find(name, searchAllChildren: true).SingleOrDefault() as T
        ?? throw new InvalidOperationException($"缺少唯一的 {typeof(T).Name} 控件：{name}。");

    private static string DescribeUi(Button start, Button stop, Label status, Label counts) =>
        $"界面状态：StartButton.Enabled={start.Enabled}，StopButton.Enabled={stop.Enabled}，" +
        $"RunStatus={status.Text}，RunCounts={counts.Text}。";

    // 2026/11/15 新增：同时核对前台窗口和全部目标的实际命中，不把激活调用成功视为桌面已经就绪。
    private static async Task ActivateTargetWindowAsync(TestWindow window, TestTarget[] targets, CancellationToken token)
    {
        window.BringToFront();
        window.Activate();
        await WaitAsync(() => NativeMethods.GetForegroundWindow() == window.Handle && targets.All(target =>
            NativeMethods.WindowFromPoint(new NativeMethods.NativePoint { X = target.Center.X, Y = target.Center.Y }) == target.Handle),
            "专用目标窗口未获得前台或目标被遮挡", token);
    }

    // 2026/11/15 新增：等待可观察状态并保持 UI 消息泵运行，每阶段限时两秒且接受整体验收取消。
    private static async Task WaitAsync(Func<bool> condition, string description, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            token.ThrowIfCancellationRequested();
            if (elapsed.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException(description + "。");
            await Task.Delay(10, token);
        }
        token.ThrowIfCancellationRequested();
    }

    // 2026/11/15 新增：逐条核对目标顺序、按钮相位和屏幕像素，失败保留该阶段全部观测消息。
    private static void AssertSequence(TestWindow window, int start, TestTarget[] targets, int actions)
    {
        ObservedMouseMessage[] observed = window.Messages.Skip(start).ToArray();
        string details = string.Join(" | ", observed.Select(message =>
            $"{message.Target}:{message.Button}/{message.Phase}@{message.ScreenPoint}"));
        Check(observed.Length == actions * 2,
            $"主窗口应产生 {actions * 2} 条按钮消息，实际 {observed.Length}。{details}");
        for (int action = 0; action < actions; action++)
        {
            TestTarget target = targets[action % targets.Length];
            for (int phase = 0; phase < 2; phase++)
            {
                ObservedMouseMessage message = observed[action * 2 + phase];
                Check(message.Target == target.Id && message.Button == MouseButton.Left &&
                    message.Phase == (phase == 0 ? MousePhase.Down : MousePhase.Up) &&
                    message.ScreenPoint.X == target.Center.X && message.ScreenPoint.Y == target.Center.Y,
                    $"主窗口第 {action + 1} 个动作消息不符合目标 {target.Id} 的完整 down/up 或落点。{details}");
            }
        }
    }

    // 2026/11/15 新增：用与界面相同的当前区域数字格式核对完整动作和轮数。
    private static string CountText(int actions, int rounds) => $"{actions:N0} 次动作  /  {rounds:N0} 轮";

    private static void AssertCounts(Label counts, int actions, int rounds) =>
        Check(counts.Text == CountText(actions, rounds),
            $"界面计数应为“{CountText(actions, rounds)}”，实际“{counts.Text}”。");

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}

// 2026/11/15 新增：键盘注入仅供显式桌面验收，要求专用窗口在前台且所有键盘键均处于释放状态。
internal static class DesktopTestKeyboard
{
    private const ushort ControlKey = 0x11;
    private const ushort F6Key = 0x75;
    private const ushort F8Key = 0x77;
    private const uint KeyboardInputType = 1;
    private const uint KeyUp = 0x0002;
    private static readonly int InputSize = Marshal.SizeOf<NativeInput>();

    internal static void SendF6(IntPtr expectedForeground) => Send(expectedForeground, [F6Key]);
    internal static void SendControlF8(IntPtr expectedForeground) => Send(expectedForeground, [ControlKey, F8Key]);

    // 2026/11/15 新增：一次批量发送完整组合键，部分发送后补发反序抬键并报告两个发送结果。
    private static void Send(IntPtr expectedForeground, ushort[] keys)
    {
        if (InputSize != (IntPtr.Size == 8 ? 40 : 28))
            throw new InvalidOperationException($"键盘 INPUT 结构大小错误：{InputSize}。");
        RequireForeground(expectedForeground);
        for (int virtualKey = 0x08; virtualKey <= 0xfe; virtualKey++)
        {
            if ((GetAsyncKeyState(virtualKey) & 0x8000) != 0)
                throw new InvalidOperationException($"检测到键盘键 0x{virtualKey:X2} 已按下，已拒绝注入全局快捷键。");
        }
        RequireForeground(expectedForeground);
        NativeInput[] releases = keys.Reverse().Select(key => CreateKey(key, KeyUp)).ToArray();
        NativeInput[] inputs = keys.Select(key => CreateKey(key, 0)).Concat(releases).ToArray();
        uint sent = SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent == inputs.Length) return;

        int error = Marshal.GetLastPInvokeError();
        string recovery = string.Empty;
        if (sent > 0)
        {
            uint released = SendInput((uint)releases.Length, releases, InputSize);
            int releaseError = Marshal.GetLastPInvokeError();
            recovery = released == releases.Length
                ? " 已补发全部抬键。"
                : $" 补发抬键仅完成 {released}/{releases.Length}，Windows 错误 {releaseError}（{new Win32Exception(releaseError).Message}）。";
        }
        throw new InvalidOperationException($"测试快捷键发送失败：{sent}/{inputs.Length}，" +
            $"Windows 错误 {error}（{new Win32Exception(error).Message}）。" + recovery);
    }

    // 2026/11/15 新增：前台变化时停止键盘注入，不把组合键发送到未知应用。
    private static void RequireForeground(IntPtr expectedForeground)
    {
        IntPtr actual = NativeMethods.GetForegroundWindow();
        if (actual != expectedForeground)
            throw new InvalidOperationException($"键盘注入前台检查失败：期望 {expectedForeground}，实际 {actual}。");
    }

    private static NativeInput CreateKey(ushort key, uint flags) => new()
    {
        Type = KeyboardInputType,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags } }
    };

    // 2026/11/15 新增：联合体保留完整 MOUSEINPUT 的大小及指针对齐，避免 x64 键盘 INPUT 被错误缩短。
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput MousePadding;
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
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    // 2026/11/15 新增：仅使用公开 User32 输入接口，发送失败后立即保存原始错误码。
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
