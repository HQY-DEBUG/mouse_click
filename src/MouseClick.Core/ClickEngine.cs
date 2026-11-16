// 文件：ClickEngine.cs
// 说明：按设置执行可取消的鼠标动作并累计完整轮数。
// 版本：v1.1
// 日期：2026/11/16
// 历史：
// v1.1 2026/11/16 为输入动作传递取消令牌，保留既有三参数输出适配器兼容性。
// v1.0 2026/11/15 新增输入接口与顺序执行器。

namespace MouseClick.Core;

// 2026/11/15 新增：将动作调度与 Windows 输入分离，null 坐标表示当前鼠标。
public interface IClickOutput
{
    void Click(MouseButton button, ClickKind kind, ClickPoint? point);

    // 2026/11/16 新增：引擎调用此入口；WindowsClickOutput 可取消动作内部等待，既有三参数假输出无需修改。
    void Click(MouseButton button, ClickKind kind, ClickPoint? point, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Click(button, kind, point);
    }
}

// 2026/11/15 新增：为窗口提供每次运行的动作数、完整轮数和运行状态。
public sealed class ClickEngine
{
    private readonly IClickOutput output;
    private long completedClicks;
    private long completedRounds;
    private int isRunning;

    // 2026/11/15 新增：输入适配器不能为空，运行计数可安全从界面线程读取。
    public ClickEngine(IClickOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    public long CompletedClicks => Interlocked.Read(ref completedClicks);
    public long CompletedRounds => Interlocked.Read(ref completedRounds);
    public bool IsRunning => Volatile.Read(ref isRunning) != 0;

    // 2026/11/15 新增：原子获得执行权，快照坐标列表，并在完成、取消或失败后释放运行状态。
    public async Task RunAsync(ClickSettings settings, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Interlocked.CompareExchange(ref isRunning, 1, 0) != 0)
            throw new InvalidOperationException("连点器已在运行，请先停止当前任务。");

        try
        {
            var snapshot = settings with { Points = settings.Points?.ToArray()! };
            snapshot.Validate();
            Interlocked.Exchange(ref completedClicks, 0);
            Interlocked.Exchange(ref completedRounds, 0);
            token.ThrowIfCancellationRequested();
            if (snapshot.StartDelayMilliseconds > 0)
                await Task.Delay(snapshot.StartDelayMilliseconds, token).ConfigureAwait(false);

            // 2026/11/15 新增：每轮使用固定快照；跟随鼠标模式由 null 坐标交给适配器执行。
            ClickPoint?[] targets = snapshot.Mode switch
            {
                TargetMode.Fixed => [snapshot.FixedPoint],
                TargetMode.Sequence => snapshot.Points.ToArray(),
                _ => new ClickPoint?[] { null }
            };

            while (true)
            {
                for (int index = 0; index < targets.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    // 2026/11/16 修改：取消贯穿单次输出，未完成的双击抛出取消时不增加动作及轮数。
                    output.Click(snapshot.Button, snapshot.Kind, targets[index], token);
                    Interlocked.Increment(ref completedClicks);
                    bool roundCompleted = index == targets.Length - 1;
                    if (roundCompleted) Interlocked.Increment(ref completedRounds);
                    token.ThrowIfCancellationRequested();

                    // 2026/11/15 新增：相邻动作统一等待，包含轮次边界；完成限定轮数后直接结束。
                    if (roundCompleted && snapshot.RepeatCount != 0 && CompletedRounds >= snapshot.RepeatCount)
                        return;
                    await Task.Delay(snapshot.IntervalMilliseconds, token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Volatile.Write(ref isRunning, 0);
        }
    }
}
