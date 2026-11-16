// 文件：TrayRegressionTests.cs
// 描述：在隐藏主窗体中验证关闭选择、托盘偏好持久化及退出生命周期。
// 版本：v1.0
// 日期：2026/11/16
// 历史：
// v1.0 2026/11/16 新增无桌面输入、无真实托盘图标的关闭行为回归。

using System.Reflection;
using MouseClick.App;
using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/16 新增：仅创建隐藏窗口句柄，提示使用委托替身，禁止显示窗口、注册热键或发送输入。
internal static class TrayRegressionTests
{
    internal static int Run()
    {
        (string Name, Action Check)[] cases =
        [
            ("默认关闭询问一次并按选择退出", CheckAskExit),
            ("取消关闭保留窗体及询问偏好", CheckCancel),
            ("未记住的托盘选择保留询问偏好", CheckMinimizeWithoutRemembering),
            ("记住托盘选择后新窗体不再询问", () => CheckRememberedChoice(WindowCloseBehavior.MinimizeToTray)),
            ("记住退出选择后新窗体不再询问", () => CheckRememberedChoice(WindowCloseBehavior.Exit)),
            ("显式退出绕过托盘偏好", CheckExplicitExit),
            ("托盘关闭保留正在运行的取消令牌", CheckMinimizeWhileRunning),
            ("显式退出先取消并等待运行结束", CheckExplicitExitWhileRunning),
            ("系统关闭不询问且不转入托盘", CheckSystemClosing),
            ("关机查询取消运行且保留可用状态", CheckShutdownWhileRunning),
            ("直接释放窗体不询问关闭选择", CheckDirectDispose),
            ("直接释放窗体取消活动运行令牌", CheckDirectDisposeWhileRunning)
        ];
        int failures = 0;
        foreach (var item in cases)
        {
            try
            {
                item.Check();
                Console.WriteLine("通过：" + item.Name + "（隐藏窗体，无输入及真实托盘图标）。");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"失败：{item.Name} — {exception.GetType().Name}: {exception.Message}");
            }
        }
        Console.WriteLine($"主窗体托盘回归：{cases.Length - failures}/{cases.Length} 项通过。");
        return failures;
    }

    // 2026/11/16 新增：实际 Close 事件只能询问一次，未记住的退出不能改写默认询问行为。
    private static void CheckAskExit()
    {
        SettingsStore store = CreateStore();
        int prompts = 0;
        using var form = CreateHiddenForm(store, () =>
        {
            prompts++;
            return new CloseChoice(WindowCloseBehavior.Exit, false);
        });
        form.Close();
        Check(prompts == 1, $"默认关闭应询问一次，实际 {prompts} 次。");
        Check(form.IsDisposed, "选择退出后主窗体应释放。");
        Check(store.Load().CloseBehavior == WindowCloseBehavior.Ask, "未记住退出时应保留 Ask 偏好。");
    }

    // 2026/11/16 新增：取消自定义提示只取消当前关闭，不写入退出或托盘偏好。
    private static void CheckCancel()
    {
        SettingsStore store = CreateStore();
        int prompts = 0;
        using var form = CreateHiddenForm(store, () => { prompts++; return null; });
        form.Close();
        Check(prompts == 1, "取消关闭也应恰好询问一次。");
        Check(!form.IsDisposed && !form.IsInTray && !form.Visible,
            "取消关闭应保留隐藏测试窗体，且不能转入托盘或显示窗口。");
        Check(store.Load().CloseBehavior == WindowCloseBehavior.Ask, "取消关闭不能修改询问偏好。");
        form.RequestExit();
        Check(form.IsDisposed && prompts == 1, "取消后的显式退出应直接释放且不再次询问。");
    }

    // 2026/11/16 新增：托盘选择保留窗口及句柄；未记住时下次关闭仍要询问。
    private static void CheckMinimizeWithoutRemembering()
    {
        SettingsStore store = CreateStore();
        int prompts = 0;
        using var form = CreateHiddenForm(store, () =>
        {
            prompts++;
            return new CloseChoice(WindowCloseBehavior.MinimizeToTray, false);
        });
        IntPtr originalHandle = form.Handle;
        form.Close();
        Check(prompts == 1, "首次托盘关闭应恰好询问一次。");
        AssertInTray(form);
        Check(form.Handle == originalHandle, "托盘分支应保留既有窗口句柄。");
        Check(store.Load().CloseBehavior == WindowCloseBehavior.Ask, "未记住托盘选择时应保留 Ask 偏好。");
        form.Close();
        Check(prompts == 2, "未记住选择时再次关闭仍应询问。");
        AssertInTray(form);
        form.RequestExit();
        Check(form.IsDisposed && prompts == 2, "托盘中的显式退出不应再询问。");
    }

    // 2026/11/16 新增：跨两个实际窗体复用同一配置，验证记住选择已持久化而非只保存于内存。
    private static void CheckRememberedChoice(WindowCloseBehavior behavior)
    {
        SettingsStore store = CreateStore();
        int prompts = 0;
        using (var first = CreateHiddenForm(store, () =>
        {
            prompts++;
            return new CloseChoice(behavior, true);
        }))
        {
            first.Close();
            Check(prompts == 1, "记住选择前应询问一次。");
            if (behavior == WindowCloseBehavior.MinimizeToTray) AssertInTray(first);
            else Check(first.IsDisposed, "记住退出选择后应立即退出。");
            Check(store.Load().CloseBehavior == behavior, "关闭选择应保存到原设置文件。");
            if (!first.IsDisposed) first.RequestExit();
        }

        using var second = CreateHiddenForm(store, () => { prompts++; return null; });
        second.Close();
        Check(prompts == 1, "重新创建窗体后应读取记住的偏好，不再询问。");
        if (behavior == WindowCloseBehavior.MinimizeToTray)
        {
            AssertInTray(second);
            second.RequestExit();
        }
        Check(second.IsDisposed, "最终显式退出或记住的退出选择应释放窗体。");
        Check(store.Load().CloseBehavior == behavior, "显式退出不能改写已记住的关闭偏好。");
    }

    // 2026/11/16 新增：托盘菜单的显式退出语义优先于持久化的最小化偏好。
    private static void CheckExplicitExit()
    {
        SettingsStore store = CreateStore(WindowCloseBehavior.MinimizeToTray);
        int prompts = 0;
        using var form = CreateHiddenForm(store, () => { prompts++; return null; });
        form.RequestExit();
        Check(form.IsDisposed && prompts == 0, "显式退出必须直接释放，不能询问或转入托盘。");
        Check(store.Load().CloseBehavior == WindowCloseBehavior.MinimizeToTray,
            "显式退出不能覆盖原托盘偏好。");
    }

    // 2026/11/16 新增：只注入生命周期令牌，验证隐藏到托盘不会停止运行，不启动任何点击引擎。
    private static void CheckMinimizeWhileRunning()
    {
        SettingsStore store = CreateStore(WindowCloseBehavior.MinimizeToTray);
        using var cancellation = new CancellationTokenSource();
        using var form = CreateHiddenForm(store, () => throw new InvalidOperationException("记住托盘偏好后不应询问。"));
        SetCancellation(form, cancellation);
        try
        {
            form.Close();
            AssertInTray(form);
            Check(!cancellation.IsCancellationRequested, "最小化到托盘不能取消正在运行的令牌。");
        }
        finally
        {
            SetCancellation(form, null);
            form.RequestExit();
        }
        Check(form.IsDisposed, "模拟运行测试结束后必须释放隐藏窗体。");
    }

    // 2026/11/16 新增：显式退出先发出取消且保留窗体，运行完成后才允许真正关闭。
    private static void CheckExplicitExitWhileRunning()
    {
        SettingsStore store = CreateStore(WindowCloseBehavior.MinimizeToTray);
        using var cancellation = new CancellationTokenSource();
        int prompts = 0;
        using var form = CreateHiddenForm(store, () => { prompts++; return null; });
        SetCancellation(form, cancellation);
        try
        {
            form.RequestExit();
            Check(cancellation.IsCancellationRequested, "显式退出必须取消正在运行的令牌。");
            Check(!form.IsDisposed && !form.IsInTray && prompts == 0,
                "运行未结束时必须等待，且不能询问或进入托盘。");
        }
        finally
        {
            SetCancellation(form, null);
            form.RequestExit();
        }
        Check(form.IsDisposed && prompts == 0, "运行结束后的再次退出应释放窗体且不询问。");
    }

    // 2026/11/16 新增：反射关闭处理器模拟系统原因，避免真的注销、关机或退出测试消息循环。
    private static void CheckSystemClosing()
    {
        foreach (WindowCloseBehavior behavior in new[] { WindowCloseBehavior.Ask, WindowCloseBehavior.MinimizeToTray })
        foreach (CloseReason reason in new[] { CloseReason.WindowsShutDown, CloseReason.TaskManagerClosing, CloseReason.ApplicationExitCall })
        {
            SettingsStore store = CreateStore(behavior);
            int prompts = 0;
            using var form = CreateHiddenForm(store, () => { prompts++; return null; });
            var args = new FormClosingEventArgs(reason, false);
            InvokeClosing(form, args);
            Check(prompts == 0 && !args.Cancel && !form.IsInTray && !form.Visible,
                $"{reason} / {behavior} 应允许关闭，不能询问、显示窗口或进入托盘。");
        }
    }

    // 2026/11/16 新增：系统关机查询取消活动任务但不能阻止关机，查询后恢复退出标志以允许取消关机后继续使用。
    private static void CheckShutdownWhileRunning()
    {
        foreach (WindowCloseBehavior behavior in new[] { WindowCloseBehavior.Ask, WindowCloseBehavior.MinimizeToTray })
        {
            using var cancellation = new CancellationTokenSource();
            int prompts = 0;
            using var form = CreateHiddenForm(CreateStore(behavior), () => { prompts++; return null; });
            SetCancellation(form, cancellation);
            try
            {
                Check(!ReadExitRequested(form), "关机查询前不应存在显式退出请求。");
                var args = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
                InvokeClosing(form, args);
                Check(cancellation.IsCancellationRequested, "关机查询必须取消活动运行令牌。");
                Check(!args.Cancel, "存在活动任务时也不能在关机查询中取消系统关闭。");
                Check(!ReadExitRequested(form), "关机查询返回后必须恢复退出标志，避免取消关机后无法继续使用。");
                Check(!form.IsDisposed && !form.IsInTray && !form.Visible && prompts == 0,
                    $"{behavior} 的关机查询不能询问、显示窗口、进入托盘或提前释放窗体。");
            }
            finally
            {
                SetCancellation(form, null);
                form.RequestExit();
            }
            Check(form.IsDisposed, "关机查询回归结束后必须释放隐藏窗体。");
        }
    }

    // 2026/11/16 新增：直接 Dispose 是资源清理操作，不能隐式显示自定义关闭提示。
    private static void CheckDirectDispose()
    {
        int prompts = 0;
        using var form = CreateHiddenForm(CreateStore(), () => { prompts++; return null; });
        form.Dispose();
        Check(form.IsDisposed && prompts == 0, "直接 Dispose 应释放窗体且不询问关闭选择。");
    }

    // 2026/11/16 新增：异常或外部直接释放窗体也必须取消活动任务，防止失去停止入口后继续执行。
    private static void CheckDirectDisposeWhileRunning()
    {
        using var cancellation = new CancellationTokenSource();
        int prompts = 0;
        using var form = CreateHiddenForm(CreateStore(), () => { prompts++; return null; });
        SetCancellation(form, cancellation);
        try
        {
            form.Dispose();
            Check(form.IsDisposed && cancellation.IsCancellationRequested,
                "直接 Dispose 必须释放窗体并取消活动运行令牌。");
            Check(prompts == 0, "直接 Dispose 活动窗体不能询问关闭选择。");
        }
        finally
        {
            SetCancellation(form, null);
        }
    }

    // 2026/11/16 新增：每项回归使用独立临时配置；保留文件供失败诊断，不读取用户设置。
    private static SettingsStore CreateStore(WindowCloseBehavior behavior = WindowCloseBehavior.Ask)
    {
        string path = Path.Combine(Path.GetTempPath(), "MouseClick.WindowsTests", "Tray",
            Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        store.Save(new ClickSettings { CloseBehavior = behavior });
        return store;
    }

    // 2026/11/16 新增：禁止真实托盘图标，只创建句柄以覆盖 Form.Close 的实际事件路径。
    private static MainForm CreateHiddenForm(SettingsStore store, Func<CloseChoice?> prompt)
    {
        var form = new MainForm(store, new RejectClickOutput(), prompt, enableTrayIcon: false);
        _ = form.Handle;
        Check(!form.Visible, "托盘回归不得显示主窗体。");
        return form;
    }

    // 2026/11/16 新增：核对可观察的托盘状态、隐藏状态及窗体生命周期。
    private static void AssertInTray(MainForm form) =>
        Check(form.IsInTray && !form.Visible && !form.IsDisposed && form.IsHandleCreated,
            "托盘分支应保留隐藏窗体和窗口句柄，不能 Dispose。");

    // 2026/11/16 新增：测试只替换取消令牌字段；每个调用方均在 finally 清除，不运行输入引擎。
    private static void SetCancellation(MainForm form, CancellationTokenSource? cancellation)
    {
        FieldInfo field = typeof(MainForm).GetField("_cancellation", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("主窗体缺少运行取消令牌字段。");
        field.SetValue(form, cancellation);
    }

    // 2026/11/16 新增：读取退出标志核对关机查询的可恢复性，不通过显示窗口或注册热键观察状态。
    private static bool ReadExitRequested(MainForm form)
    {
        FieldInfo field = typeof(MainForm).GetField("_exitRequested", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("主窗体缺少显式退出状态字段。");
        return field.GetValue(form) is true;
    }

    // 2026/11/16 新增：明确选择实际 FormClosing 事件签名，避免与继承的关闭重载混淆。
    private static void InvokeClosing(MainForm form, FormClosingEventArgs args)
    {
        MethodInfo method = typeof(MainForm).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: [typeof(object), typeof(FormClosingEventArgs)], modifiers: null)
            ?? throw new InvalidOperationException("主窗体缺少关闭事件处理器。");
        try
        {
            method.Invoke(form, [form, args]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException("关闭事件处理失败：" + exception.InnerException.Message, exception.InnerException);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // 2026/11/16 新增：意外输入直接使测试失败，绝不连接 Windows 原生输入接口。
    private sealed class RejectClickOutput : IClickOutput
    {
        public void Click(MouseButton button, ClickKind kind, ClickPoint? point) =>
            throw new InvalidOperationException("托盘回归不允许发送鼠标输入。");
    }
}
