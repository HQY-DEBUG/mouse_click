// 文件：MultiWindowDesktopTests.cs
// 描述：在四个自有顶层窗口验证真实鼠标消息及至少三十毫秒按住的接收边界。
// 版本：v1.4
// 日期：2026/11/16
// 历史：
// v1.4 2026/11/16 守卫适配器转发动作内取消令牌，保持真实首次按下取消验收链路完整。
// v1.3 2026/11/16 新增首次按下后取消双击的独立入口，验证抬起、无第二次按下及零动作计数。
// v1.2 2026/11/16 仅在真实输出开始后于关闭自有窗口前恢复光标，拒绝抢夺外部前台。

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseClick.App.Interop;
using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/16 新增：仅由显式桌面入口运行，鼠标目标限定四个无业务操作的测试窗口，任何失败都停止输入。
internal static class MultiWindowDesktopTests
{
    internal static int Run() => Run(cancelAfterFirstDown: false);

    // 2026/11/16 新增：独立红测试入口，仍复用自有前台/坐标守卫及安全光标恢复，不运行普通按住验收。
    internal static int RunCancellation() => Run(cancelAfterFirstDown: true);

    private static int Run(bool cancelAfterFirstDown)
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.NativePoint originalCursor))
        {
            Console.Error.WriteLine("失败：无法保存光标位置 — " + new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            return 1;
        }

        var messages = new List<MouseReceipt>();
        var windows = new List<HoldTargetWindow>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        bool finished = false;
        Exception? failure = null;
        WindowTarget[] targets = [];
        int inputStarted = 0;
        bool cursorRestoreAttempted = false;

        // 2026/11/16 新增：窗口仍存在且保持自有前台时恢复光标；未发送输入不改光标，外部接管时明确报告。
        void RestoreCursorIfNeeded()
        {
            if (Volatile.Read(ref inputStarted) == 0 || cursorRestoreAttempted) return;
            cursorRestoreAttempted = true;
            Exception? restoreFailure = null;
            if (!IsOwnedForeground(targets))
                restoreFailure = new InvalidOperationException(
                    $"恢复光标时前台已离开自有测试窗口，未抢占前台或移动光标。实际前台={NativeMethods.GetForegroundWindow()}。");
            else if (!NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y))
                restoreFailure = new Win32Exception(Marshal.GetLastPInvokeError(), "关闭测试窗口前恢复光标失败。");
            if (restoreFailure is not null)
                failure = failure is null ? restoreFailure : new AggregateException(failure, restoreFailure);
        }

        Console.WriteLine(cancelAfterFirstDown
            ? "双击取消真实输入验收：目标首次收到 down 即取消，必须仅抬起且不发送第二次 down。"
            : "四窗口真实输入验收：四点两轮，每个计划动作仅发送一次，不补点。");
        Console.WriteLine(cancelAfterFirstDown
            ? "只在第一个自有目标测试一次双击取消；就绪最多 30 秒，输入最多 15 秒。"
            : "使用 150 毫秒动作间隔提速；本项不是用户现场的 3 秒间隔验收。就绪最多 30 秒，输入最多 15 秒。");

        try
        {
            Rectangle desktop = Screen.PrimaryScreen?.WorkingArea
                ?? throw new InvalidOperationException("没有可用主屏幕，不能隔离四个测试窗口。");
            int width = Math.Min(430, (desktop.Width - 60) / 2);
            int height = Math.Min(250, (desktop.Height - 60) / 2);
            if (width < 220 || height < 140)
                throw new InvalidOperationException("主屏幕工作区过小，无法完整放置四个隔离测试窗口。");

            for (int index = 0; index < 4; index++)
            {
                var window = new HoldTargetWindow(index + 1, messages)
                {
                    Bounds = new Rectangle(desktop.Left + 20 + (index % 2) * (width + 20),
                        desktop.Top + 20 + (index / 2) * (height + 20), width, height)
                };
                window.FormClosing += (_, args) =>
                {
                    if (finished) return;
                    args.Cancel = true;
                    cancellation.Cancel();
                };
                windows.Add(window);
            }

            windows[0].Shown += async (_, _) =>
            {
                try
                {
                    foreach (HoldTargetWindow window in windows.Skip(1)) window.Show();
                    // 2026/11/16 新增：仅正常请求激活测试窗口；系统拒绝焦点切换时继续等待用户切回。
                    windows[0].BringToFront();
                    windows[0].Activate();
                    targets = windows.Select(window => window.Snapshot()).ToArray();
                    WriteTargetDiagnostics(targets);
                    await WaitForReadyAsync(targets, cancellation.Token);
                    cancellation.CancelAfter(TimeSpan.FromSeconds(15));

                    var engine = new ClickEngine(new GuardedMultiWindowOutput(targets, cancellation.Token,
                        () => Interlocked.Exchange(ref inputStarted, 1),
                        cancelAfterFirstDown ? ClickKind.Double : ClickKind.Single));
                    // 2026/11/16 新增：接收方触发实际取消，避免仅在发送前取消而漏掉按住期间的第二次按下。
                    if (cancelAfterFirstDown)
                    {
                        windows[0].FirstDownAction = cancellation.Cancel;
                        await CheckCancellationAsync(engine, windows, targets[0], messages, cancellation.Token);
                        return;
                    }
                    // 2026/11/16 新增：输出在工作线程执行，按住期间 UI 消息泵继续处理四个接收窗口的消息。
                    await Task.Run(() => engine.RunAsync(new ClickSettings
                    {
                        Mode = TargetMode.Sequence,
                        Points = targets.Select(target => target.Point).ToArray(),
                        RepeatCount = 2,
                        StartDelayMilliseconds = 0,
                        IntervalMilliseconds = 150
                    }, cancellation.Token), cancellation.Token);
                    await WaitForMessagesAsync(messages, cancellation.Token);
                    ReportReceipts(windows, engine.CompletedClicks, engine.CompletedRounds);
                    CheckMessages(messages, targets);
                    Check(engine.CompletedClicks == 8 && engine.CompletedRounds == 2 && !engine.IsRunning,
                        "四点两轮应提交八个完整动作并完成两轮。");

                    string insufficient = string.Join("；", windows.Where(window => window.BusinessClicks != 2)
                        .Select(window => $"窗口 {window.Id}：接收成功 {window.BusinessClicks}/2，按住 {window.HoldDescription}"));
                    Check(insufficient.Length == 0,
                        "按住时长不足或接收失败：每次 down→up 必须至少 30 毫秒，不能把输入提交当作目标接收成功。" + insufficient);
                    Console.WriteLine("通过：四个顶层窗口均按顺序收到两组完整 down/up，八次按住均达到 30 毫秒。");
                }
                catch (Exception exception)
                {
                    failure = exception;
                    WriteTargetDiagnostics(targets);
                    ReportReceipts(windows, completedClicks: null, completedRounds: null);
                }
                finally
                {
                    // 2026/11/16 新增：等待引擎任务退出后才关闭窗口；手动关闭及超时都通过同一令牌停止后续输入。
                    cancellation.Cancel();
                    finished = true;
                    // 2026/11/16 修改：测试窗口仍持有前台时先恢复光标，关闭后前台可能回到更高权限应用。
                    RestoreCursorIfNeeded();
                    foreach (HoldTargetWindow window in windows.Where(window => !window.IsDisposed)) window.Close();
                }
            };
            Application.Run(windows[0]);
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }
        finally
        {
            cancellation.Cancel();
            finished = true;
            RestoreCursorIfNeeded();
            foreach (HoldTargetWindow window in windows) window.Dispose();
        }

        if (failure is not null)
        {
            string scenario = cancelAfterFirstDown ? "双击取消边界" : "四窗口按住边界";
            Console.Error.WriteLine($"失败：{scenario} — {failure.GetType().Name}: {failure.Message}");
            return 1;
        }
        Console.WriteLine(cancelAfterFirstDown
            ? "结果：双击取消验收通过，测试窗口已释放，光标已恢复。"
            : "结果：四窗口真实输入验收通过，测试窗口已释放，光标已恢复。");
        return 0;
    }

    // 2026/11/16 新增：第一份真实 down 由目标取消令牌，独立断言只有对应 up、无第二次 down、未完成动作不计数。
    private static async Task CheckCancellationAsync(ClickEngine engine, List<HoldTargetWindow> windows,
        WindowTarget target, List<MouseReceipt> messages, CancellationToken token)
    {
        bool observedCancellation = false;
        try
        {
            await Task.Run(() => engine.RunAsync(new ClickSettings
            {
                Mode = TargetMode.Fixed,
                FixedPoint = target.Point,
                Kind = ClickKind.Double,
                RepeatCount = 1,
                StartDelayMilliseconds = 0,
                IntervalMilliseconds = 150
            }, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            observedCancellation = true;
        }

        // 2026/11/16 新增：被测令牌已取消，独立等待100ms让已排队的抬起到达，不允许任何新输入。
        await Task.Delay(100);
        ReportReceipts(windows, engine.CompletedClicks, engine.CompletedRounds);
        Check(observedCancellation, "首次 down 后取消应保留 OperationCanceledException 类型。");
        Check(windows[0].DownCount == 1 && windows[0].UpCount == 1 && messages.Count == 2,
            $"首次 down 后取消仍产生了额外输入或缺少抬起：down={windows[0].DownCount}，up={windows[0].UpCount}，总消息={messages.Count}。");
        Check(messages[0].Target == target.Id && messages[0].IsDown && messages[0].Point == target.Point &&
            messages[1].Target == target.Id && !messages[1].IsDown && messages[1].Point == target.Point,
            "取消后的两条消息必须是同一自有目标上的首次 down 及其对应 up。");
        Check(engine.CompletedClicks == 0 && engine.CompletedRounds == 0 && !engine.IsRunning,
            $"被取消的双击不能计为完整动作，实际 {engine.CompletedClicks} 动作/{engine.CompletedRounds} 轮，运行状态={engine.IsRunning}。");
        Console.WriteLine("通过：真实首次 down 后取消只收到对应 up，没有第二次 down，引擎零动作零轮并退出。");
    }

    // 2026/11/16 修改：连续三次确认精确命中和自有前台，外部窗口占据前台时不发送任何鼠标输入。
    private static async Task WaitForReadyAsync(WindowTarget[] targets, CancellationToken token)
    {
        int stableSamples = 0;
        while (stableSamples < 3)
        {
            token.ThrowIfCancellationRequested();
            stableSamples = IsOwnedForeground(targets) && targets.All(IsExactTarget) ? stableSamples + 1 : 0;
            await Task.Delay(50, token);
        }
    }

    // 2026/11/16 新增：操作系统投递消息另给两秒，保持 UI 泵运行且仍受输入总期限约束。
    private static async Task WaitForMessagesAsync(List<MouseReceipt> messages, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (messages.Count < 16)
        {
            token.ThrowIfCancellationRequested();
            if (elapsed.Elapsed > TimeSpan.FromSeconds(2))
                throw new TimeoutException($"原始鼠标消息不足，预期 16，实际 {messages.Count}。");
            await Task.Delay(10, token);
        }
        await Task.Delay(100, token);
    }

    // 2026/11/16 新增：逐条核对接收窗口、按下/抬起及真实像素落点，不以发送参数替代接收证据。
    private static void CheckMessages(List<MouseReceipt> messages, WindowTarget[] targets)
    {
        Check(messages.Count == 16, $"四点两轮应只收到 16 条鼠标消息，实际 {messages.Count}。");
        for (int index = 0; index < 16; index++)
        {
            MouseReceipt receipt = messages[index];
            WindowTarget target = targets[(index / 2) % 4];
            Check(receipt.Target == target.Id && receipt.IsDown == (index % 2 == 0) && receipt.Point == target.Point,
                $"第 {index + 1} 条鼠标消息不符合窗口 {target.Id} 的顺序及落点：{receipt}。");
        }
    }

    // 2026/11/16 新增：分别报告提交动作、原始消息和按住敏感接收数；窗口消息时间与实际分派时间并列保留。
    private static void ReportReceipts(IEnumerable<HoldTargetWindow> windows, long? completedClicks, long? completedRounds)
    {
        if (completedClicks is not null)
            Console.WriteLine($"引擎提交：{completedClicks} 个动作 / {completedRounds} 轮。");
        foreach (HoldTargetWindow window in windows)
            Console.WriteLine($"窗口 {window.Id}：down={window.DownCount}，up={window.UpCount}，" +
                $"至少30ms接收成功={window.BusinessClicks}，按住[消息时间/分派时间]={window.HoldDescription}。");
    }

    private static void WriteTargetDiagnostics(IEnumerable<WindowTarget> targets)
    {
        foreach (WindowTarget target in targets)
            Console.WriteLine($"四窗口诊断：目标={target.Id}，窗口={target.Handle}，可见={NativeMethods.IsWindowVisible(target.Handle)}，" +
                $"坐标=({target.Point.X},{target.Point.Y})，命中={WindowAt(target.Point)}，前台={NativeMethods.GetForegroundWindow()}。");
    }

    private static bool IsExactTarget(WindowTarget target) =>
        NativeMethods.IsWindowVisible(target.Handle) && WindowAt(target.Point) == target.Handle;

    // 2026/11/16 新增：精确命中不代表输入桌面可交互，前台必须属于本测试四个窗口之一。
    private static bool IsOwnedForeground(WindowTarget[] targets)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        return targets.Any(target => target.Handle == foreground);
    }

    private static IntPtr WindowAt(ClickPoint point) =>
        NativeMethods.WindowFromPoint(new NativeMethods.NativePoint { X = point.X, Y = point.Y });

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private sealed record WindowTarget(int Id, IntPtr Handle, ClickPoint Point);
    private sealed record MouseReceipt(int Target, bool IsDown, ClickPoint Point);

    // 2026/11/16 新增：每次发送前重新验证目标归属和取消状态；仅调用一次生产输出，绝不重试或补点。
    private sealed class GuardedMultiWindowOutput(WindowTarget[] targets, CancellationToken token,
        Action markInputStarted, ClickKind expectedKind) : IClickOutput
    {
        private readonly WindowsClickOutput output = new();

        // 2026/11/16 修改：保留三参数入口，真实引擎四参数调用将同一令牌继续传入生产输出。
        public void Click(MouseButton button, ClickKind kind, ClickPoint? point) =>
            Click(button, kind, point, token);

        public void Click(MouseButton button, ClickKind kind, ClickPoint? point, CancellationToken cancellationToken)
        {
            token.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            // 2026/11/16 新增：每次发送重新确认前台归属，外部窗口或高权限应用抢占前台时立即拒绝。
            Check(IsOwnedForeground(targets),
                $"前台不是自有测试窗口，已拒绝鼠标输入。实际前台={NativeMethods.GetForegroundWindow()}。");
            WindowTarget target = targets.SingleOrDefault(target => target.Point == point)
                ?? throw new InvalidOperationException("指定坐标不是本测试的自有目标，已拒绝鼠标输入。");
            Check(button == MouseButton.Left && kind == expectedKind,
                "真实输入验收只允许当前用例预设的左键点击类型。");
            Check(IsExactTarget(target), $"目标 {target.Id} 被遮挡、移动或关闭，已拒绝鼠标输入。");
            token.ThrowIfCancellationRequested();
            Check(IsOwnedForeground(targets), "发送前前台已离开四个自有测试窗口，已拒绝鼠标输入。");
            // 2026/11/16 新增：越过全部守卫后才标记真实输出开始；即使发送部分失败也需尝试恢复原光标。
            markInputStarted();
            output.Click(button, kind, point, cancellationToken);
        }
    }

    // 2026/11/16 新增：四个普通激活策略的顶层窗口没有业务操作；完整 down/up 至少保持30ms才累计接收成功。
    private sealed class HoldTargetWindow : Form
    {
        private readonly List<MouseReceipt> messages;
        private readonly List<(uint MessageMilliseconds, double DispatchMilliseconds)> holds = [];
        private uint? downMessageTime;
        private long downDispatchTime;
        internal int Id { get; }
        internal int DownCount { get; private set; }
        internal int UpCount { get; private set; }
        internal int BusinessClicks { get; private set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Action? FirstDownAction { get; set; }
        internal string HoldDescription => string.Join(", ", holds.Select(hold =>
            $"{hold.MessageMilliseconds}/{hold.DispatchMilliseconds:F1}ms"));

        internal HoldTargetWindow(int id, List<MouseReceipt> messages)
        {
            Id = id;
            this.messages = messages;
            Text = $"真实输入验收 · 自有目标 {id}";
            Name = "MultiWindowTarget" + id;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(230, 238, 248);
        }

        internal WindowTarget Snapshot()
        {
            Point center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
            return new WindowTarget(Id, Handle, new ClickPoint(center.X, center.Y));
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            TextRenderer.DrawText(args.Graphics,
                $"目标 {Id}：仅记录鼠标输入\n按住至少30ms才计入接收成功\n关闭任一测试窗口可取消", Font, ClientRectangle,
                Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        // 2026/11/16 新增：鼠标双击的第二次按下也记为 down；时间戳来自消息而非后台发送者。
        protected override void WndProc(ref Message message)
        {
            bool isDown = message.Msg is 0x0201 or 0x0203;
            bool isUp = message.Msg == 0x0202;
            if (isDown || isUp)
            {
                uint messageTime = unchecked((uint)GetMessageTime());
                long dispatchTime = Stopwatch.GetTimestamp();
                long packed = message.LParam.ToInt64();
                Point point = PointToScreen(new Point(unchecked((short)(packed & 0xffff)),
                    unchecked((short)((packed >> 16) & 0xffff))));
                messages.Add(new MouseReceipt(Id, isDown, new ClickPoint(point.X, point.Y)));
                if (isDown)
                {
                    DownCount++;
                    downMessageTime = messageTime;
                    downDispatchTime = dispatchTime;
                    // 2026/11/16 新增：仅首次接收 down 时触发取消探针，后续重复 down 必须保留以暴露缺陷。
                    if (DownCount == 1) FirstDownAction?.Invoke();
                }
                else
                {
                    UpCount++;
                    if (downMessageTime is uint started)
                    {
                        uint duration = unchecked(messageTime - started);
                        holds.Add((duration, Stopwatch.GetElapsedTime(downDispatchTime, dispatchTime).TotalMilliseconds));
                        if (duration >= 30) BusinessClicks++;
                        downMessageTime = null;
                    }
                }
            }
            base.WndProc(ref message);
        }

        [DllImport("user32.dll")]
        private static extern int GetMessageTime();
    }
}
