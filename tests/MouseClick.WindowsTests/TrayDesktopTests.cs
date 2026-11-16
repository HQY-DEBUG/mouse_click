// 文件：TrayDesktopTests.cs
// 描述：验证托盘热键、三秒间隔取消、启动前隐藏及失败恢复，仅使用假鼠标输出计数。
// 版本：v1.3
// 日期：2026/11/16
// 历史：
// v1.3 2026/11/16 新增四点三秒间隔中真实 F6 停止、重启及 Ctrl+F8 取消回归。
// v1.2 2026/11/16 增加按钮及全部启动热键的首个输出隐藏边界、托盘失败恢复回归。
// v1.1 2026/11/16 新增多点顺序模式先进入托盘再由 F6 启动、停止及再次启动的回归。

using System.Diagnostics;
using System.Reflection;
using MouseClick.App;
using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/16 新增：仅 --tray-desktop 显式入口调用，实际点击引擎注入不连接 Windows 输入的计数器。
internal static class TrayDesktopTests
{
    internal static int Run()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), "MouseClick.WindowsTests", "TrayDesktop",
            Guid.NewGuid().ToString("N"), "settings.json");
        // 2026/11/16 修改：使用主屏幕内三个明确目标匹配多点模式，假输出逐次验证实际引擎的顺序。
        Rectangle bounds = Screen.PrimaryScreen?.Bounds
            ?? throw new InvalidOperationException("托盘桌面验收需要可用的主屏幕。");
        ClickPoint[] points =
        [
            new(bounds.Left + bounds.Width / 4, bounds.Top + bounds.Height / 3),
            new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2),
            new(bounds.Left + bounds.Width * 3 / 4, bounds.Top + bounds.Height * 2 / 3)
        ];
        var store = new SettingsStore(settingsPath);
        store.Save(new ClickSettings
        {
            Mode = TargetMode.Sequence,
            Points = points,
            RepeatCount = 0,
            StartDelayMilliseconds = 0,
            IntervalMilliseconds = 40,
            CloseBehavior = WindowCloseBehavior.MinimizeToTray
        });
        Console.WriteLine("托盘桌面验收独立配置（保留供诊断）：" + settingsPath);
        Console.WriteLine("本流程显示真实窗口并注入 F6/F9/F10/Ctrl+F8；全部鼠标动作仅计数，绝不发送鼠标输入。");

        using var lifetime = new CancellationTokenSource();
        using var outputLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var output = new CountingOutput(outputLifetime.Token, points);
        using var host = new Form
        {
            Name = "TrayDesktopTestHost",
            Text = "托盘验收 · 假鼠标输出",
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(460, 135)
        };
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "托盘与 Ctrl+F8 自动验收，最长 30 秒。\n不会发送鼠标输入；关闭此窗口可取消。"
        });
        // 2026/11/16 修改：新增两次三秒以上的停止后静默观察，将总期限延长十秒并保留有界清理。
        using var deadline = new System.Windows.Forms.Timer { Interval = 30000 };
        MainForm? form = null;
        ClickEngine? engine = null;
        Exception? failure = null;
        bool finished = false;

        // 2026/11/16 新增：单次总期限同时覆盖模态设置窗口；超时主动关闭所属对话框，避免消息循环悬挂。
        deadline.Tick += (_, _) =>
        {
            deadline.Stop();
            failure ??= new TimeoutException("托盘桌面全流程超过三十秒。");
            lifetime.Cancel();
            CloseOwnedPreferences(form);
        };
        host.FormClosing += (_, args) =>
        {
            if (finished) return;
            args.Cancel = true;
            lifetime.Cancel();
            CloseOwnedPreferences(form);
        };
        host.Shown += async (_, _) =>
        {
            try
            {
                form = new MainForm(store, output,
                    () => throw new InvalidOperationException("托盘偏好或明确退出路径不应询问关闭选择。"));
                // 2026/11/16 新增：仅 UI 线程读取控件可见性，后台假输出通过 volatile 快照检查隐藏顺序。
                form.VisibleChanged += (_, _) => output.ObserveVisibility(form.Visible);
                output.ObserveVisibility(form.Visible);
                engine = GetField<ClickEngine>(form, "_engine");
                await CheckFlowAsync(host, form, store, output, lifetime.Token);
                // 2026/11/16 新增：原有三点流程释放热键后，独立重建四点三秒配置验证长等待期间的停止响应。
                await CheckLongIntervalTrayAsync(host, lifetime.Token);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
            finally
            {
                // 2026/11/16 新增：先禁止假输出，再请求实际运行任务取消；清理等待独立限时两秒。
                deadline.Stop();
                outputLifetime.Cancel();
                try
                {
                    CloseOwnedPreferences(form);
                    if (form is not null && !form.IsDisposed) form.RequestExit();
                    await WaitAsync(() => (form is null || form.IsDisposed) && (engine is null || !engine.IsRunning),
                        "清理时主窗体或点击引擎未结束", CancellationToken.None);
                }
                catch (Exception exception)
                {
                    failure = failure is null ? exception : new AggregateException(failure, exception);
                }
                finally
                {
                    form?.Dispose();
                    finished = true;
                    host.Close();
                }
            }
        };

        try
        {
            deadline.Start();
            Application.Run(host);
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }
        finally
        {
            deadline.Stop();
            outputLifetime.Cancel();
            CloseOwnedPreferences(form);
            form?.Dispose();
        }

        if (failure is not null)
        {
            Console.Error.WriteLine($"失败：托盘桌面验收 — {failure.GetType().Name}: {failure.Message}");
            return 1;
        }
        Console.WriteLine("结果：托盘桌面验收通过；主窗体、宿主及托盘资源已释放，零实际鼠标输出。");
        return 0;
    }

    // 2026/11/16 新增：保持真实窗体、热键和引擎，只替换桌面鼠标边界，捕获长间隔无法停止及取消后残留动作。
    private static async Task CheckLongIntervalTrayAsync(Form host, CancellationToken token)
    {
        Rectangle bounds = Screen.PrimaryScreen?.Bounds
            ?? throw new InvalidOperationException("四点长间隔验收需要可用的主屏幕。");
        ClickPoint[] points =
        [
            new(bounds.Left + bounds.Width / 4, bounds.Top + bounds.Height / 4),
            new(bounds.Left + bounds.Width * 3 / 4, bounds.Top + bounds.Height / 4),
            new(bounds.Left + bounds.Width / 4, bounds.Top + bounds.Height * 3 / 4),
            new(bounds.Left + bounds.Width * 3 / 4, bounds.Top + bounds.Height * 3 / 4)
        ];
        string settingsPath = Path.Combine(Path.GetTempPath(), "MouseClick.WindowsTests", "TrayLongInterval",
            Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(settingsPath);
        store.Save(new ClickSettings
        {
            Mode = TargetMode.Sequence,
            Points = points,
            RepeatCount = 0,
            StartDelayMilliseconds = 0,
            IntervalMilliseconds = 3000,
            CloseBehavior = WindowCloseBehavior.MinimizeToTray
        });
        Console.WriteLine("四点三秒间隔独立配置（保留供诊断）：" + settingsPath);
        using var outputLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var output = new CountingOutput(outputLifetime.Token, points);
        using var form = new MainForm(store, output,
            () => throw new InvalidOperationException("长间隔托盘验收不应询问关闭选择。"));
        form.VisibleChanged += (_, _) => output.ObserveVisibility(form.Visible);
        output.ObserveVisibility(form.Visible);
        ClickEngine engine = GetField<ClickEngine>(form, "_engine");
        try
        {
            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            form.Shown += (_, _) => shown.TrySetResult();
            form.Show();
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(2), token);
            Button start = Find<Button>(form, "StartButton");
            Button stop = Find<Button>(form, "StopButton");
            Label status = Find<Label>(form, "RunStatus");
            Check(start.Enabled && status.Text.StartsWith("准备就绪。", StringComparison.Ordinal),
                "四点三秒间隔验收的实际热键注册失败。状态：" + status.Text);
            form.Close();
            Check(form.IsInTray && !form.Visible && !form.IsDisposed && output.Count == 0,
                "四点三秒间隔流程应先进入空闲托盘，再由 F6 启动。");

            // 2026/11/16 新增：首轮真实 F6 停止，随后同一窗体再次 F6 启动并以 Ctrl+F8 取消，均观察超过完整间隔。
            (string Name, Action<IntPtr> SendStop)[] stops =
            [
                ("F6", DesktopTestKeyboard.SendF6),
                ("Ctrl+F8", DesktopTestKeyboard.SendControlF8)
            ];
            foreach (var item in stops)
            {
                await ActivateHostAsync(host, token);
                long previousCount = output.Count;
                output.PrepareRun();
                DesktopTestKeyboard.SendF6(host.Handle);
                await WaitAsync(() => stop.Enabled && output.Count == previousCount + 1,
                    "四点三秒间隔的托盘 F6 启动或再次启动未产生首个假动作", token);
                Check(form.IsInTray && !form.Visible && output.FirstOutputWasHidden,
                    "四点三秒间隔启动应在托盘保持隐藏，首次假动作不能发生于可见主窗口。");
                await Task.Delay(200, token);
                Check(output.Count == previousCount + 1,
                    "三秒间隔尚未到期，首个动作后约两百毫秒不能产生第二个假动作。");

                var stopElapsed = Stopwatch.StartNew();
                item.SendStop(host.Handle);
                await WaitAsync(() => !stop.Enabled && !engine.IsRunning && status.Text == "已停止。",
                    $"四点三秒间隔中真实 {item.Name} 未响应停止", token);
                stopElapsed.Stop();
                Check(stopElapsed.Elapsed < TimeSpan.FromSeconds(1),
                    $"{item.Name} 停止耗时 {stopElapsed.ElapsedMilliseconds} 毫秒，应在一秒内取消三秒等待。");
                long stoppedCount = output.Count;
                Check(stoppedCount == previousCount + 1, $"{item.Name} 取消前后只能产生首个假动作。");
                await Task.Delay(3300, token);
                Check(output.Count == stoppedCount && !engine.IsRunning && form.IsInTray && !form.Visible,
                    $"{item.Name} 停止后三点三秒内出现残留假动作、重新运行或意外恢复窗口。");
                Console.WriteLine($"通过：四点三秒间隔从托盘 F6 启动后，真实 {item.Name} 在 {stopElapsed.ElapsedMilliseconds} 毫秒内停止；随后 3300 毫秒无新增假动作。");
            }
        }
        finally
        {
            // 2026/11/16 新增：局部失败或总超时也先阻断假输出，再等待主窗体释放，防止热键残留影响后续验收。
            outputLifetime.Cancel();
            if (!form.IsDisposed) form.RequestExit();
            await WaitAsync(() => form.IsDisposed && !engine.IsRunning,
                "四点三秒间隔验收清理时主窗体或引擎未结束", CancellationToken.None);
        }
    }

    // 2026/11/16 修改：先关闭空闲主窗体进入托盘，再从独立前台宿主用真实 F6 启动、停止及重启。
    private static async Task CheckFlowAsync(Form host, MainForm form, SettingsStore store,
        CountingOutput output, CancellationToken token)
    {
        // 2026/11/16 修改：等待 Shown 完成热键注册，避免 Show 返回后仍读取构造期的默认提示。
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        form.Shown += (_, _) => shown.TrySetResult();
        form.Show();
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(2), token);
        Button start = Find<Button>(form, "StartButton");
        Button stop = Find<Button>(form, "StopButton");
        Label status = Find<Label>(form, "RunStatus");
        Check(start.Enabled && status.Text.StartsWith("准备就绪。", StringComparison.Ordinal),
            "实际主窗体未注册全部热键，不能执行托盘桌面验收。状态：" + status.Text);
        form.Close();
        Check(form.IsInTray && !form.Visible && !form.IsDisposed, "关闭后应隐藏到托盘并保留窗体。");
        Check(output.Count == 0 && !stop.Enabled, "首次进入托盘前不能提前启动或产生假动作。");

        host.BringToFront();
        host.Activate();
        await WaitAsync(() => NativeMethods.GetForegroundWindow() == host.Handle,
            "专用宿主未获得前台，已拒绝发送 F6", token);
        output.PrepareRun();
        DesktopTestKeyboard.SendF6(host.Handle);
        await WaitAsync(() => stop.Enabled && output.Count >= 4, "先关闭到托盘后，真实 F6 未能启动假鼠标输出", token);
        Check(form.IsInTray && !form.Visible, "托盘 F6 启动不能重新显示主窗口。");
        long firstRunCount = output.Count;
        DesktopTestKeyboard.SendF6(host.Handle);
        await WaitAsync(() => !stop.Enabled && status.Text == "已停止。", "真实 F6 未能停止托盘运行", token);
        long firstStoppedCount = output.Count;
        await Task.Delay(250, token);
        Check(output.Count == firstStoppedCount, "F6 停止后静默期不能再产生假动作。");
        Console.WriteLine($"通过：多点主窗体先进入托盘，真实 F6 启动并再次停止，{firstStoppedCount} 个假动作均按三个坐标顺序执行。");

        output.PrepareRun();
        DesktopTestKeyboard.SendF6(host.Handle);
        await WaitAsync(() => stop.Enabled && output.Count >= firstStoppedCount + 4,
            "F6 停止后未能再次启动托盘中的假鼠标输出", token);
        Check(form.IsInTray && !form.Visible && output.Count > firstRunCount,
            "F6 再次启动应保持托盘隐藏并产生新动作。");
        Console.WriteLine($"通过：真实 F6 在托盘中可再次启动，累计假动作 {firstStoppedCount} → {output.Count}。");
        DesktopTestKeyboard.SendControlF8(host.Handle);
        await WaitAsync(() => !stop.Enabled && status.Text == "已停止。", "真实 Ctrl+F8 未停止托盘运行", token);
        long stoppedCount = output.Count;
        await Task.Delay(250, token);
        Check(output.Count == stoppedCount && form.IsInTray && !form.Visible,
            "Ctrl+F8 停止后必须保持托盘隐藏，且静默期不能再产生假动作。");
        Console.WriteLine($"通过：真实 Ctrl+F8 停止托盘运行，最终 {stoppedCount} 个假动作，250 毫秒内无新增。");

        // 2026/11/16 新增：分别验证可见窗口的四种启动入口，第一份输出必须发生在隐藏之后。
        await CheckVisibleStartAsync(host, form, output, "开始按钮", () => start.PerformClick(), token);
        ComboBox shortcut = Find<ComboBox>(form, "ShortcutChoice");
        (int Index, string Name, Action<IntPtr> Send)[] shortcuts =
        [
            (0, "F6", DesktopTestKeyboard.SendF6),
            (1, "F9", DesktopTestKeyboard.SendF9),
            (2, "F10", DesktopTestKeyboard.SendF10)
        ];
        foreach (var item in shortcuts)
        {
            if (shortcut.SelectedIndex != item.Index)
            {
                shortcut.SelectedIndex = item.Index;
                Check(start.Enabled && status.Text.StartsWith("准备就绪。", StringComparison.Ordinal),
                    $"{item.Name} 热键注册未就绪，不能执行启动验收。状态：{status.Text}");
            }
            await CheckVisibleStartAsync(host, form, output, item.Name, () => item.Send(host.Handle), token);
        }
        await CheckInvalidSequenceAsync(host, form, output, token);
        stoppedCount = output.Count;

        // 2026/11/16 新增：直接触发实际托盘菜单事件，验证恢复、设置取消/保存与明确退出。
        MenuItem(form, "显示主窗口").PerformClick();
        await WaitAsync(() => form.Visible && !form.IsInTray, "托盘菜单未恢复主窗体", token);
        Check(!stop.Enabled, "恢复主窗体不能重新启动已停止任务。");
        Console.WriteLine("通过：托盘菜单恢复真实主窗口，已停止状态保持。");

        form.Close();
        Check(form.IsInTray, "设置取消验收前应回到托盘。");
        CheckPreferencesMenu(host, form, store, selectedIndex: 2, save: false,
            expected: WindowCloseBehavior.MinimizeToTray, token);
        Check(form.Visible && !form.IsInTray, "托盘设置入口应恢复主窗口。");
        form.Close();
        Check(form.IsInTray, "取消设置后应继续按原偏好转入托盘。");
        CheckPreferencesMenu(host, form, store, selectedIndex: 0, save: true,
            expected: WindowCloseBehavior.Ask, token);
        Check(form.Visible && !form.IsInTray, "保存设置后应保留可见主窗口。");
        Console.WriteLine("通过：托盘设置菜单的取消保留原偏好，保存将每次询问写入独立配置。");

        MenuItem(form, "退出").PerformClick();
        await WaitAsync(() => form.IsDisposed, "托盘明确退出菜单未释放主窗体", token);
        Check(output.Count == stoppedCount, "停止后的菜单操作不能重新产生假动作。");
        Check(store.Load().CloseBehavior == WindowCloseBehavior.Ask, "明确退出不能改写保存的每次询问偏好。");
        Console.WriteLine("通过：托盘退出菜单绕过询问偏好并释放真实主窗体。");
    }

    // 2026/11/16 新增：恢复真实窗口后启动，Fake 边界记录第一份输出时的可见性，防止点击后才隐藏。
    private static async Task CheckVisibleStartAsync(Form host, MainForm form, CountingOutput output,
        string source, Action launch, CancellationToken token)
    {
        MenuItem(form, "显示主窗口").PerformClick();
        await WaitAsync(() => form.Visible && !form.IsInTray, source + " 验收前未恢复主窗体", token);
        await ActivateHostAsync(host, token);
        output.PrepareRun();
        long previousCount = output.Count;
        launch();
        await WaitAsync(() => output.FirstOutputObserved, source + " 没有到达首次假输出边界", token);
        Check(output.FirstOutputWasHidden, source + " 的首次输出发生时主窗口仍可见，必须先隐藏再点击。");
        Check(form.IsInTray && !form.Visible, source + " 成功启动后应保持托盘隐藏状态。");
        await WaitAsync(() => output.Count >= previousCount + 4, source + " 隐藏后未持续运行", token);
        DesktopTestKeyboard.SendControlF8(host.Handle);
        Button stop = Find<Button>(form, "StopButton");
        Label status = Find<Label>(form, "RunStatus");
        await WaitAsync(() => !stop.Enabled && status.Text == "已停止。", source + " 启动后 Ctrl+F8 未停止", token);
        long stoppedCount = output.Count;
        await Task.Delay(100, token);
        Check(output.Count == stoppedCount, source + " 停止后仍产生假输出。");
        Console.WriteLine($"通过：可见主窗口由 {source} 启动时先隐藏，首个输出及后续三点顺序正确，Ctrl+F8 可停止。");
    }

    // 2026/11/16 新增：托盘中的空序列必须恢复错误界面且保持零输出，避免热键失败看起来没有反应。
    private static async Task CheckInvalidSequenceAsync(Form host, MainForm form, CountingOutput output,
        CancellationToken token)
    {
        Check(form.IsInTray && !form.Visible, "无效序列回归应从已停止的托盘状态开始。");
        Find<ComboBox>(form, "ShortcutChoice").SelectedIndex = 0;
        Button start = Find<Button>(form, "StartButton");
        Button stop = Find<Button>(form, "StopButton");
        Label status = Find<Label>(form, "RunStatus");
        Check(start.Enabled && status.Text.StartsWith("准备就绪。", StringComparison.Ordinal),
            "空序列回归的 F6 热键注册失败。状态：" + status.Text);
        ListView list = Find<ListView>(form, "PointList");
        ListViewItem[] originalItems = list.Items.Cast<ListViewItem>().ToArray();
        long previousCount = output.Count;
        try
        {
            list.Items.Clear();
            await ActivateHostAsync(host, token);
            DesktopTestKeyboard.SendF6(host.Handle);
            await WaitAsync(() => status.Text.Contains("至少需要一个坐标", StringComparison.Ordinal),
                "托盘中空序列的 F6 启动没有给出坐标错误", token);
            Check(form.Visible && !form.IsInTray && !stop.Enabled,
                "托盘启动校验失败后必须恢复主窗口显示错误，并保持停止状态。");
            await Task.Delay(100, token);
            Check(output.Count == previousCount, "空序列启动失败不能产生任何假输出。");
            Console.WriteLine("通过：托盘中空序列按 F6 恢复主窗口显示错误，零新增假输出。");
        }
        finally
        {
            list.Items.AddRange(originalItems);
        }
    }

    // 2026/11/16 新增：实际功能键均发送给专用前台宿主，窗口切换后重新等待原生前台确认。
    private static async Task ActivateHostAsync(Form host, CancellationToken token)
    {
        host.BringToFront();
        host.Activate();
        await WaitAsync(() => NativeMethods.GetForegroundWindow() == host.Handle,
            "专用宿主未获得前台，已拒绝发送启动热键", token);
    }

    // 2026/11/16 新增：先排队一次实际对话框操作，再进入模态菜单事件；全流程期限负责兜底退出。
    private static void CheckPreferencesMenu(Form host, MainForm form, SettingsStore store,
        int selectedIndex, bool save, WindowCloseBehavior expected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Exception? callbackFailure = null;
        bool callbackCompleted = false;
        host.BeginInvoke((Action)(() =>
        {
            PreferencesForm? dialog = null;
            try
            {
                token.ThrowIfCancellationRequested();
                dialog = Application.OpenForms.OfType<PreferencesForm>().SingleOrDefault(item => item.Owner == form)
                    ?? throw new InvalidOperationException("托盘设置菜单没有显示所属设置对话框。");
                Find<ComboBox>(dialog, "CloseBehaviorChoice").SelectedIndex = selectedIndex;
                Find<Button>(dialog, save ? "SavePreferencesButton" : "CancelPreferencesButton").PerformClick();
                Check(dialog.DialogResult == (save ? DialogResult.OK : DialogResult.Cancel),
                    "实际设置按钮没有返回预期模态结果。");
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                if (dialog is not null && !dialog.IsDisposed) dialog.Close();
            }
            finally
            {
                callbackCompleted = true;
            }
        }));
        MenuItem(form, "设置").PerformClick();
        token.ThrowIfCancellationRequested();
        if (callbackFailure is not null)
            throw new InvalidOperationException("设置菜单回调失败：" + callbackFailure.Message, callbackFailure);
        Check(callbackCompleted, "模态设置关闭前没有执行预先排队的测试回调。");
        Check(store.Load().CloseBehavior == expected, "设置菜单保存或取消后的持久化偏好不符合预期。");
    }

    // 2026/11/16 新增：只关闭本测试主窗体所属的设置对话框，超时与清理不操作其他应用窗口。
    private static void CloseOwnedPreferences(MainForm? form)
    {
        if (form is null) return;
        foreach (PreferencesForm dialog in Application.OpenForms.OfType<PreferencesForm>()
            .Where(item => item.Owner == form).ToArray())
            dialog.Close();
    }

    // 2026/11/16 修改：每个异步观测最多两秒，同时服从整条桌面流程的三十秒取消令牌。
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

    // 2026/11/16 新增：按实际菜单文字及控件名称定位，结构变化时明确报告而不触发未知入口。
    private static ToolStripMenuItem MenuItem(MainForm form, string caption) =>
        GetField<ContextMenuStrip>(form, "_trayMenu").Items.OfType<ToolStripMenuItem>()
            .SingleOrDefault(item => item.Text == caption)
        ?? throw new InvalidOperationException("缺少唯一的托盘菜单项：" + caption + "。");

    private static T GetField<T>(MainForm form, string name) where T : class =>
        typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) as T
        ?? throw new InvalidOperationException("主窗体缺少字段：" + name + "。");

    private static T Find<T>(Control form, string name) where T : Control =>
        form.Controls.Find(name, searchAllChildren: true).SingleOrDefault() as T
        ?? throw new InvalidOperationException("缺少唯一的控件：" + name + "。");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // 2026/11/16 修改：假输出检查传入坐标的循环顺序并累计动作，覆盖三点与四点；每次重启从第一点开始。
    private sealed class CountingOutput(CancellationToken token, ClickPoint[] points) : IClickOutput
    {
        private long count;
        private int sequenceIndex;
        private int windowVisible;
        private int firstOutputVisibility = -1;
        internal long Count => Interlocked.Read(ref count);
        internal bool FirstOutputObserved => Volatile.Read(ref firstOutputVisibility) >= 0;
        internal bool FirstOutputWasHidden => Volatile.Read(ref firstOutputVisibility) == 0;

        // 2026/11/16 新增：可见状态由 UI 事件更新，后台输出只读原子值，不访问任何 Control。
        internal void ObserveVisibility(bool visible) => Volatile.Write(ref windowVisible, visible ? 1 : 0);

        internal void PrepareRun()
        {
            Interlocked.Exchange(ref sequenceIndex, 0);
            Interlocked.Exchange(ref firstOutputVisibility, -1);
        }

        public void Click(MouseButton button, ClickKind kind, ClickPoint? point)
        {
            token.ThrowIfCancellationRequested();
            int index = Interlocked.Increment(ref sequenceIndex) - 1;
            // 2026/11/16 新增：在首次假输出边界独立记录并拒绝可见窗口，捕获先点击后隐藏的时序错误。
            if (index == 0)
            {
                int visible = Volatile.Read(ref windowVisible);
                Interlocked.Exchange(ref firstOutputVisibility, visible);
                Check(visible == 0, "首个假鼠标输出发生前主窗口仍可见。");
            }
            ClickPoint expected = points[index % points.Length];
            Check(button == MouseButton.Left && kind == ClickKind.Single && point == expected,
                $"多点假输出第 {index + 1} 次应为左键单击 ({expected.X},{expected.Y})，实际 {button}/{kind}/{point}。");
            Interlocked.Increment(ref count);
        }
    }
}
