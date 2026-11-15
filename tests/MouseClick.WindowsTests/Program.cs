// 文件：Program.cs
// 描述：执行有显式桌面开关的 Windows 原生鼠标与热键集成验收。
// 版本：v1.0
// 日期：2026/11/15
// 历史：
// v1.0 2026/11/15 新增输入边界、真实多点与按键消息、热键冲突和资源释放测试。

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseClick.App.Interop;
using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/15 新增：默认只检查拒绝非法输入；只有 --desktop 才创建测试窗口并发送鼠标事件。
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(argument => argument is not ("--desktop" or "--picker-only")) || args.Length > 1)
        {
            Console.Error.WriteLine("参数无效。用法：MouseClick.WindowsTests [--desktop | --picker-only]");
            return 1;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // 2026/11/15 新增：选点释放顺序回归只调用隐藏窗体处理器，不发送输入且可单独运行。
        int pickerFailures = PickerRegressionTests.Run();
        if (pickerFailures != 0) return 1;
        if (args is ["--picker-only"]) return 0;
        try
        {
            CheckRejectedInputs();
            Console.WriteLine("通过：屏幕外坐标和非法按钮/点击类型均在发送前被拒绝。");
        }
        catch (Exception exception)
        {
            ReportFailure("纯边界验收", exception);
            return 1;
        }

        if (args.Length == 0)
        {
            Console.WriteLine("桌面验收未运行；添加 --desktop 才发送鼠标输入和检查热键注册。未注入键盘输入。");
            return 0;
        }
        return RunDesktopTests();
    }

    // 2026/11/15 新增：捕获缺少显示器边界校验及枚举校验的问题，这些调用按契约不能到达 SendInput。
    private static void CheckRejectedInputs()
    {
        var output = new WindowsClickOutput();
        ExpectOutOfRange(() => output.Click(MouseButton.Left, ClickKind.Single, new(int.MinValue, int.MinValue)), "负极值坐标");
        ExpectOutOfRange(() => output.Click(MouseButton.Left, ClickKind.Single, new(int.MaxValue, int.MaxValue)), "正极值坐标");
        ExpectOutOfRange(() => output.Click((MouseButton)999, ClickKind.Single, null), "非法鼠标按钮");
        ExpectOutOfRange(() => output.Click(MouseButton.Left, (ClickKind)999, null), "非法点击类型");
    }

    // 2026/11/15 新增：所有桌面步骤共享 10 秒取消期限，finally 关闭专用窗口并恢复运行前的光标位置。
    private static int RunDesktopTests()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.NativePoint originalCursor))
        {
            ReportFailure("保存光标位置", new Win32Exception(Marshal.GetLastPInvokeError()));
            return 1;
        }

        int result = 1;
        TestWindow? window = null;
        bool finished = false;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            window = new TestWindow();
            TestWindow testWindow = window;
            testWindow.FormClosing += (_, args) =>
            {
                if (!finished)
                {
                    args.Cancel = true;
                    cancellation.Cancel();
                }
            };
            testWindow.Shown += async (_, _) =>
            {
                try
                {
                    testWindow.Activate();
                    await Task.Delay(150, cancellation.Token);
                    await CheckDesktopInputsAsync(testWindow, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    CheckHotkeyConflictAndRelease();
                    result = 0;
                }
                catch (Exception exception)
                {
                    ReportFailure("桌面验收（超时或手动关闭也视为未通过）", exception);
                }
                finally
                {
                    finished = true;
                    cancellation.Cancel();
                    testWindow.Close();
                }
            };
            Application.Run(testWindow);
        }
        catch (Exception exception)
        {
            ReportFailure("创建或运行测试窗口", exception);
            result = 1;
        }
        finally
        {
            finished = true;
            cancellation.Cancel();
            window?.Close();
            window?.Dispose();
            if (!NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y))
            {
                ReportFailure("恢复光标位置", new Win32Exception(Marshal.GetLastPInvokeError()));
                result = 1;
            }
        }
        Console.WriteLine(result == 0 ? "结果：桌面鼠标验收通过，窗口已关闭，光标已恢复。" : "结果：验收失败，返回退出码 1。");
        return result;
    }

    // 2026/11/15 新增：真实执行器和原生输出组成完整链路，窗口收到的消息独立验证顺序、按键与精确落点。
    private static async Task CheckDesktopInputsAsync(TestWindow window, CancellationToken token)
    {
        TestTarget[] targets = window.SnapshotTargets();
        var output = new GuardedOutput(targets, token);
        var engine = new ClickEngine(output);
        int start = window.Messages.Count;
        await engine.RunAsync(new ClickSettings
        {
            Mode = TargetMode.Sequence,
            Points = targets.Select(target => target.Center).ToArray(),
            RepeatCount = 2,
            StartDelayMilliseconds = 0,
            IntervalMilliseconds = 50
        }, token);
        await WaitForMessagesAsync(window, start + 12, token);
        int[] expectedTargets = [1, 2, 3, 1, 2, 3];
        Check(window.Messages.Count == start + 12, "三点两轮应只收到 12 条按钮消息。");
        for (int action = 0; action < expectedTargets.Length; action++)
        {
            TestTarget target = targets[expectedTargets[action] - 1];
            AssertMessage(window.Messages[start + action * 2], target.Id, MouseButton.Left, MousePhase.Down, target.Center);
            AssertMessage(window.Messages[start + action * 2 + 1], target.Id, MouseButton.Left, MousePhase.Up, target.Center);
        }
        Check(engine.CompletedClicks == 6 && engine.CompletedRounds == 2 && !engine.IsRunning,
            "实际输入完成后应记录 6 个动作、2 轮并结束运行。");
        Console.WriteLine("通过：三个目标按 1→2→3→1→2→3 收到完整 down/up，屏幕落点逐像素一致。");

        // 2026/11/15 新增：每种按钮先在目标 1 单击，再到目标 2 双击，避免前次点击干扰双击识别。
        foreach (MouseButton button in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle })
        {
            await CheckClickAsync(window, output, targets[0], button, ClickKind.Single,
                [MousePhase.Down, MousePhase.Up], token);
            await CheckClickAsync(window, output, targets[1], button, ClickKind.Double,
                [MousePhase.Down, MousePhase.Up, MousePhase.DoubleClick, MousePhase.Up], token);
            Console.WriteLine($"通过：{button} 单击和双击均收到预期原生消息。");
        }

        // 2026/11/15 新增：固定模式使用目标内部的非中心坐标，捕获忽略坐标或绝对位置换算偏移的问题。
        TestTarget fixedTarget = targets[2];
        var fixedPoint = new ClickPoint(fixedTarget.ScreenBounds.Left + 37, fixedTarget.ScreenBounds.Top + 53);
        start = window.Messages.Count;
        await engine.RunAsync(new ClickSettings
        {
            Mode = TargetMode.Fixed,
            FixedPoint = fixedPoint,
            RepeatCount = 1,
            StartDelayMilliseconds = 0,
            IntervalMilliseconds = 50
        }, token);
        await WaitForMessagesAsync(window, start + 2, token);
        Check(window.Messages.Count == start + 2, "固定模式应只发送一组 down/up。");
        AssertMessage(window.Messages[start], fixedTarget.Id, MouseButton.Left, MousePhase.Down, fixedPoint);
        AssertMessage(window.Messages[start + 1], fixedTarget.Id, MouseButton.Left, MousePhase.Up, fixedPoint);
        Check(NativeMethods.GetCursorPos(out NativeMethods.NativePoint actualCursor), "无法读取固定点击后的光标位置。");
        Check(actualCursor.X == fixedPoint.X && actualCursor.Y == fixedPoint.Y, "固定点击后光标位置与指定屏幕像素不一致。");
        Console.WriteLine("通过：固定模式在非中心指定坐标准确落点。");
        Console.WriteLine($"收到消息：普通 down {window.Messages.Count(message => message.Phase == MousePhase.Down)}、" +
            $"doubleclick {window.Messages.Count(message => message.Phase == MousePhase.DoubleClick)}、" +
            $"up {window.Messages.Count(message => message.Phase == MousePhase.Up)}。");
    }

    // 2026/11/15 新增：验证每个动作的准确事件数量，Windows 双击消息只替代第二次 down。
    private static async Task CheckClickAsync(TestWindow window, GuardedOutput output, TestTarget target,
        MouseButton button, ClickKind kind, MousePhase[] phases, CancellationToken token)
    {
        int start = window.Messages.Count;
        output.Click(button, kind, target.Center);
        await WaitForMessagesAsync(window, start + phases.Length, token);
        Check(window.Messages.Count == start + phases.Length, $"{button} {kind} 产生额外或缺少按钮消息。");
        for (int index = 0; index < phases.Length; index++)
            AssertMessage(window.Messages[start + index], target.Id, button, phases[index], target.Center);
    }

    // 2026/11/15 新增：消息泵保持可响应；局部等待限制 1 秒，总执行另受 10 秒取消期限约束。
    private static async Task WaitForMessagesAsync(TestWindow window, int expectedCount, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (window.Messages.Count < expectedCount)
        {
            token.ThrowIfCancellationRequested();
            if (elapsed.Elapsed > TimeSpan.FromSeconds(1))
                throw new TimeoutException($"未收到预期鼠标消息：需要 {expectedCount}，实际 {window.Messages.Count}。");
            await Task.Delay(10, token);
        }
        await Task.Delay(40, token);
    }

    // 2026/11/15 新增：只注册本测试窗口的热键；外部程序已占用时明确跳过，不向桌面发送任何键盘事件。
    private static void CheckHotkeyConflictAndRelease()
    {
        using var first = new HotkeyWindow();
        using var second = new HotkeyWindow();
        string? initialError = first.Register();
        if (initialError is not null)
        {
            if (initialError.Contains("占用", StringComparison.Ordinal))
            {
                Console.WriteLine("跳过：无法隔离热键注册测试，F6、F7 或 F8 已被外部程序占用；未注销外部注册。 " +
                    initialError.Replace(Environment.NewLine, " ", StringComparison.Ordinal));
                return;
            }
            throw new InvalidOperationException("初次热键注册失败：" + initialError);
        }
        Check(first.CanToggle && first.CanStop && first.CanCapture, "初次注册成功时三个热键均应可用。");
        string? conflict = second.Register();
        Check(conflict is not null && conflict.Contains("占用", StringComparison.Ordinal), "重复注册应明确报告热键冲突。");
        Check(!second.CanToggle && !second.CanCapture, "冲突实例不得声称拥有被占用的启停或取点热键。");
        // 2026/11/15 新增：第二个实例可使用独立备用停止键，但不能声称复用第一个实例的按键。
        if (second.CanStop)
            Check(first.StopHotkeyText == "F8" && second.StopHotkeyText == "Ctrl+Alt+F8", "停止键冲突时应明确使用独立备用组合。");
        first.Dispose();
        string? afterDispose = second.Register();
        Check(afterDispose is null && second.CanToggle && second.CanStop && second.CanCapture,
            "首个窗口 Dispose 后，第二个窗口应能重新注册全部热键：" + afterDispose);
        Console.WriteLine("通过：两个实例的热键注册冲突可见，Dispose 后注册已释放；未注入键盘输入。");
    }

    // 2026/11/15 新增：断言比较独立的窗口观测值，失败时包含实际目标、按键、消息和坐标。
    private static void AssertMessage(ObservedMouseMessage actual, int target, MouseButton button, MousePhase phase, ClickPoint point) =>
        Check(actual.Target == target && actual.Button == button && actual.Phase == phase &&
            actual.ScreenPoint.X == point.X && actual.ScreenPoint.Y == point.Y,
            $"预期目标 {target}/{button}/{phase}/({point.X},{point.Y})，实际 {actual}。");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectOutOfRange(Action action, string description)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException(description + " 未被明确拒绝。");
    }

    private static void ReportFailure(string name, Exception exception) =>
        Console.Error.WriteLine($"失败：{name} — {exception.GetType().Name}: {exception.Message}");
}

// 2026/11/15 新增：每次调用生产输出前重新验证 WindowFromPoint 精确命中自有目标，遮挡或关闭即停止。
internal sealed class GuardedOutput(TestTarget[] targets, CancellationToken token) : IClickOutput
{
    private readonly WindowsClickOutput output = new();

    public void Click(MouseButton button, ClickKind kind, ClickPoint? point)
    {
        token.ThrowIfCancellationRequested();
        if (point is null)
            throw new InvalidOperationException("桌面验收只允许指定测试目标，不允许跟随未知光标位置。");
        TestTarget? target = targets.SingleOrDefault(candidate => candidate.ScreenBounds.Contains(point.X, point.Y));
        if (target is null)
            throw new InvalidOperationException("点击坐标不属于本测试的三个目标。");
        IntPtr hit = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint { X = point.X, Y = point.Y });
        if (hit != target.Handle)
            throw new InvalidOperationException($"目标 {target.Id} 被遮挡或不再可用，已拒绝发送鼠标输入。" +
                $" 坐标 ({point.X},{point.Y})，期望窗口 {target.Handle}，实际窗口 {hit}。");
        token.ThrowIfCancellationRequested();
        output.Click(button, kind, point);
    }
}
