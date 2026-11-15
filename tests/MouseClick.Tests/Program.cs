// 文件：Program.cs
// 说明：运行无第三方测试框架的核心行为验证，失败时返回非零退出码。
// 版本：v1.0
// 日期：2026/11/15
// 历史：v1.0 2026/11/15 新增设置、执行器及存储行为验证入口。

using System.Diagnostics;
using MouseClick.Core;

namespace MouseClick.Tests;

// 2026/11/15 新增：通过独立输入记录器验证真实执行器，不向桌面发送点击。
internal static class Program
{
    private static readonly ClickSettings Quick = new()
    {
        StartDelayMilliseconds = 0,
        IntervalMilliseconds = 1,
        RepeatCount = 1
    };

    // 2026/11/15 新增：逐项报告行为结果，保留所有失败并向调用脚本返回失败状态。
    private static async Task<int> Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            ("多点两轮保持顺序并准确计数", SequenceRunsTwoRounds),
            ("固定坐标转发中键双击且每轮只算一个动作", FixedPointCountsActions),
            ("跟随模式发送当前光标语义", CursorUsesNullPoint),
            ("启动延时取消不会发送点击", CancelDuringStartDelay),
            ("运行间隔取消后不继续发送", CancelDuringInterval),
            ("取消后可以重新启动且重置计数", RestartAfterCancellation),
            ("同一执行器拒绝并发启动", RejectConcurrentRun),
            ("输出失败向上传播且不虚增计数", PropagateOutputFailure),
            ("开始后坐标列表修改不影响本次顺序", SnapshotPointList),
            ("点间及轮间等待且最后一个动作无需等待", IntervalBetweenActionsOnly),
            ("参数边界可运行并拒绝非法设置", ValidateSettingBoundaries),
            ("空设置与空输出被明确拒绝", RejectNullInputs),
            .. StorageTests.All
        ];
        int failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"通过：{test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"失败：{test.Name} — {exception.GetType().Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"结果：{tests.Length - failed}/{tests.Length} 项通过");
        return failed == 0 ? 0 : 1;
    }

    // 2026/11/15 新增：捕获漏点、多点、错误顺序和把单点错误计作完整轮次的问题。
    private static async Task SequenceRunsTwoRounds()
    {
        var output = new RecordingOutput();
        var engine = new ClickEngine(output);
        await engine.RunAsync(Quick with
        {
            Mode = TargetMode.Sequence,
            RepeatCount = 2,
            Points = [new(10, 20), new(-30, 40), new(50, -60)]
        }, CancellationToken.None);
        EqualPoints([new(10, 20), new(-30, 40), new(50, -60), new(10, 20), new(-30, 40), new(50, -60)], output.Calls);
        Equal(6L, engine.CompletedClicks, "动作数");
        Equal(2L, engine.CompletedRounds, "完整轮数");
        Check(!engine.IsRunning, "完成后应退出运行状态");
    }

    // 2026/11/15 新增：防止固定模式使用错误按键、丢失双击或重复计算双击动作数。
    private static async Task FixedPointCountsActions()
    {
        var output = new RecordingOutput();
        var engine = new ClickEngine(output);
        await engine.RunAsync(Quick with
        {
            Mode = TargetMode.Fixed,
            FixedPoint = new(-100, 200),
            Button = MouseButton.Middle,
            Kind = ClickKind.Double,
            RepeatCount = 2
        }, CancellationToken.None);
        EqualPoints([new(-100, 200), new(-100, 200)], output.Calls);
        Check(output.Calls.All(call => call.Button == MouseButton.Middle && call.Kind == ClickKind.Double), "应转发中键双击");
        Equal(2L, engine.CompletedClicks, "双击动作数");
        Equal(2L, engine.CompletedRounds, "固定坐标轮数");
    }

    // 2026/11/15 新增：防止跟随鼠标模式错误复用已保存的固定坐标。
    private static async Task CursorUsesNullPoint()
    {
        var output = new RecordingOutput();
        await new ClickEngine(output).RunAsync(Quick with { Button = MouseButton.Right }, CancellationToken.None);
        EqualPoints([null], output.Calls);
        Equal(MouseButton.Right, output.Calls[0].Button, "右键");
    }

    // 2026/11/15 新增：取消长启动等待必须及时结束，且不能产生第一下点击。
    private static async Task CancelDuringStartDelay()
    {
        var output = new RecordingOutput();
        var engine = new ClickEngine(output);
        using var cancellation = new CancellationTokenSource();
        Task run = engine.RunAsync(Quick with { StartDelayMilliseconds = 60000 }, cancellation.Token);
        Check(engine.IsRunning, "启动延时也属于运行状态");
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
        Equal(0, output.Calls.Count, "取消启动的点击数");
        Equal(0L, engine.CompletedRounds, "取消启动的轮数");
        Check(!engine.IsRunning, "取消后应释放运行状态");
    }

    // 2026/11/15 新增：取消长动作间隔后只保留已经完成的动作和完整轮次。
    private static async Task CancelDuringInterval()
    {
        var firstClick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new RecordingOutput(_ => firstClick.TrySetResult());
        var engine = new ClickEngine(output);
        using var cancellation = new CancellationTokenSource();
        Task run = engine.RunAsync(Quick with
        {
            IntervalMilliseconds = 60000,
            RepeatCount = 0,
            Mode = TargetMode.Sequence,
            Points = [new(1, 2), new(3, 4)]
        }, cancellation.Token);
        await firstClick.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
        Equal(1, output.Calls.Count, "取消后的点击数");
        Equal(1L, engine.CompletedClicks, "已完成动作数");
        Equal(0L, engine.CompletedRounds, "未完成一轮不得计数");
    }

    // 2026/11/15 新增：防止取消后互斥状态残留或把前次计数带入新运行。
    private static async Task RestartAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var output = new RecordingOutput(_ => cancellation.Cancel());
        var engine = new ClickEngine(output);
        await ThrowsAsync<OperationCanceledException>(() => engine.RunAsync(Quick with { RepeatCount = 0 }, cancellation.Token));
        output.AfterClick = null;
        await engine.RunAsync(Quick with { RepeatCount = 2 }, CancellationToken.None);
        Equal(3, output.Calls.Count, "两次运行合计输出数");
        Equal(2L, engine.CompletedClicks, "重新运行后的动作数");
        Equal(2L, engine.CompletedRounds, "重新运行后的轮数");
    }

    // 2026/11/15 新增：防止同一执行器出现两个活动循环或拒绝并发时破坏原循环。
    private static async Task RejectConcurrentRun()
    {
        var output = new RecordingOutput();
        var engine = new ClickEngine(output);
        using var cancellation = new CancellationTokenSource();
        Task first = engine.RunAsync(Quick with { StartDelayMilliseconds = 60000 }, cancellation.Token);
        await ThrowsAsync<InvalidOperationException>(() => engine.RunAsync(Quick, CancellationToken.None));
        Check(engine.IsRunning, "拒绝并发不得改变原运行状态");
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => first);
        Equal(0, output.Calls.Count, "被拒绝运行不得输出");
    }

    // 2026/11/15 新增：捕获输入失败被吞掉、错误动作计数或失败后无法再启动的问题。
    private static async Task PropagateOutputFailure()
    {
        var failure = new IOException("预期输入失败");
        var output = new RecordingOutput(index => { if (index == 2) throw failure; });
        var engine = new ClickEngine(output);
        Exception actual = await ThrowsAsync<IOException>(() => engine.RunAsync(Quick with
        {
            Mode = TargetMode.Sequence,
            Points = [new(1, 2), new(3, 4)]
        }, CancellationToken.None));
        Check(ReferenceEquals(failure, actual), "应保留原始异常");
        Equal(1L, engine.CompletedClicks, "成功输出数");
        Equal(0L, engine.CompletedRounds, "失败轮次不得计数");
        Check(!engine.IsRunning, "输出失败后应释放运行状态");
        output.AfterClick = null;
        await engine.RunAsync(Quick, CancellationToken.None);
    }

    // 2026/11/15 新增：运行中编辑界面列表不应改写或中断已开始的坐标序列。
    private static async Task SnapshotPointList()
    {
        var points = new List<ClickPoint> { new(1, 2), new(3, 4) };
        var output = new RecordingOutput(index => { if (index == 1) points.Clear(); });
        await new ClickEngine(output).RunAsync(Quick with { Mode = TargetMode.Sequence, Points = points }, CancellationToken.None);
        EqualPoints([new(1, 2), new(3, 4)], output.Calls);
    }

    // 2026/11/15 新增：用宽容时间下界检查点间和轮间间隔，取消末次输入可探测多余等待。
    private static async Task IntervalBetweenActionsOnly()
    {
        var output = new RecordingOutput();
        await new ClickEngine(output).RunAsync(Quick with
        {
            Mode = TargetMode.Sequence,
            Points = [new(1, 2), new(3, 4)],
            RepeatCount = 2,
            IntervalMilliseconds = 80
        }, CancellationToken.None);
        for (int index = 1; index < output.Calls.Count; index++)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(output.Calls[index - 1].Timestamp, output.Calls[index].Timestamp);
            Check(elapsed >= TimeSpan.FromMilliseconds(60), $"第 {index + 1} 下过早输出");
        }

        var finalOutput = new RecordingOutput();
        await new ClickEngine(finalOutput).RunAsync(Quick with { IntervalMilliseconds = 60000 }, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Equal(1, finalOutput.Calls.Count, "最后一个动作无需额外等待");
    }

    // 2026/11/15 新增：覆盖文件和界面输入的数值、枚举、热键及空坐标边界。
    private static Task ValidateSettingBoundaries()
    {
        (Quick with { IntervalMilliseconds = 1, StartDelayMilliseconds = 0, RepeatCount = 0 }).Validate();
        (Quick with { IntervalMilliseconds = 86400000, StartDelayMilliseconds = 3600000, RepeatCount = 1000000, ToggleHotkey = 0x78 }).Validate();
        (Quick with { Mode = TargetMode.Fixed, FixedPoint = new(int.MinValue, int.MaxValue), ToggleHotkey = 0x79 }).Validate();
        ClickSettings[] invalid =
        [
            Quick with { IntervalMilliseconds = 0 }, Quick with { IntervalMilliseconds = 86400001 },
            Quick with { StartDelayMilliseconds = -1 }, Quick with { StartDelayMilliseconds = 3600001 },
            Quick with { RepeatCount = -1 }, Quick with { RepeatCount = 1000001 },
            Quick with { Button = (MouseButton)999 }, Quick with { Kind = (ClickKind)999 },
            Quick with { Mode = (TargetMode)999 }, Quick with { ToggleHotkey = 0x76 },
            Quick with { FixedPoint = null! }, Quick with { Points = null! },
            Quick with { Mode = TargetMode.Sequence }, Quick with { Points = new ClickPoint[] { null! } }
        ];
        foreach (ClickSettings settings in invalid)
        {
            Throws<ArgumentException>(settings.Validate);
        }
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：空调用参数应在输入边界报错而不是进入不确定的运行状态。
    private static async Task RejectNullInputs()
    {
        Throws<ArgumentNullException>(() => new ClickEngine(null!));
        await ThrowsAsync<ArgumentNullException>(() => new ClickEngine(new RecordingOutput()).RunAsync(null!, CancellationToken.None));
    }

    // 2026/11/15 新增：使用通用断言报告实际行为与预期差异，不依赖外部测试包。
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string description) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{description}：预期 {expected}，实际 {actual}");

    private static void EqualPoints(IReadOnlyList<ClickPoint?> expected, IReadOnlyList<ClickCall> calls)
    {
        Equal(expected.Count, calls.Count, "动作序列长度");
        for (int index = 0; index < expected.Count; index++) Equal(expected[index], calls[index].Point, $"第 {index + 1} 个坐标");
    }

    private static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"应抛出 {typeof(TException).Name}");
    }

    private static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"应抛出 {typeof(TException).Name}");
    }
}

// 2026/11/15 新增：记录执行器提交的实际参数和时间，允许模拟外部输入错误与取消。
internal sealed record ClickCall(MouseButton Button, ClickKind Kind, ClickPoint? Point, long Timestamp);

internal sealed class RecordingOutput(Action<int>? afterClick = null) : IClickOutput
{
    public List<ClickCall> Calls { get; } = [];
    public Action<int>? AfterClick { get; set; } = afterClick;

    public void Click(MouseButton button, ClickKind kind, ClickPoint? point)
    {
        Calls.Add(new(button, kind, point, Stopwatch.GetTimestamp()));
        AfterClick?.Invoke(Calls.Count);
    }
}
