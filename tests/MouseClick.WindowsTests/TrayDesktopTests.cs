// 文件：TrayDesktopTests.cs
// 描述：以真实窗体、托盘菜单和 Ctrl+F8 验证生命周期，仅使用假鼠标输出计数。
// 版本：v1.0
// 日期：2026/11/16
// 历史：
// v1.0 2026/11/16 新增二十秒有界的独立托盘桌面验收，不发送鼠标输入或读取用户配置。

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
        var store = new SettingsStore(settingsPath);
        store.Save(new ClickSettings
        {
            Mode = TargetMode.Cursor,
            RepeatCount = 0,
            StartDelayMilliseconds = 0,
            IntervalMilliseconds = 40,
            CloseBehavior = WindowCloseBehavior.MinimizeToTray
        });
        Console.WriteLine("托盘桌面验收独立配置（保留供诊断）：" + settingsPath);
        Console.WriteLine("本流程显示真实窗口并注入 Ctrl+F8；全部鼠标动作仅计数，绝不发送鼠标输入。");

        using var lifetime = new CancellationTokenSource();
        using var outputLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var output = new CountingOutput(outputLifetime.Token);
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
            Text = "托盘与 Ctrl+F8 自动验收，最长 20 秒。\n不会发送鼠标输入；关闭此窗口可取消。"
        });
        using var deadline = new System.Windows.Forms.Timer { Interval = 20000 };
        MainForm? form = null;
        ClickEngine? engine = null;
        Exception? failure = null;
        bool finished = false;

        // 2026/11/16 新增：单次总期限同时覆盖模态设置窗口；超时主动关闭所属对话框，避免消息循环悬挂。
        deadline.Tick += (_, _) =>
        {
            deadline.Stop();
            failure ??= new TimeoutException("托盘桌面全流程超过二十秒。");
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
                engine = GetField<ClickEngine>(form, "_engine");
                await CheckFlowAsync(host, form, store, output, lifetime.Token);
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

    // 2026/11/16 新增：真实主窗体启动引擎，隐藏后计数继续，再由独立前台宿主发送全局停止组合键。
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
        start.PerformClick();
        await WaitAsync(() => stop.Enabled && output.Count >= 4, "假鼠标输出没有正常启动", token);
        form.Close();
        Check(form.IsInTray && !form.Visible && !form.IsDisposed, "关闭后应隐藏到托盘并保留窗体。");
        long hiddenCount = output.Count;
        await WaitAsync(() => output.Count >= hiddenCount + 4, "隐藏到托盘后假鼠标输出没有继续", token);
        Check(stop.Enabled, "托盘运行期间停止入口应保持可用。");
        Console.WriteLine($"通过：真实引擎进入托盘后继续产生假动作，计数 {hiddenCount} → {output.Count}。");

        host.BringToFront();
        host.Activate();
        await WaitAsync(() => NativeMethods.GetForegroundWindow() == host.Handle,
            "专用宿主未获得前台，已拒绝发送 Ctrl+F8", token);
        DesktopTestKeyboard.SendControlF8(host.Handle);
        await WaitAsync(() => !stop.Enabled && status.Text == "已停止。", "真实 Ctrl+F8 未停止托盘运行", token);
        long stoppedCount = output.Count;
        await Task.Delay(250, token);
        Check(output.Count == stoppedCount && form.IsInTray && !form.Visible,
            "Ctrl+F8 停止后必须保持托盘隐藏，且静默期不能再产生假动作。");
        Console.WriteLine($"通过：真实 Ctrl+F8 停止托盘运行，最终 {stoppedCount} 个假动作，250 毫秒内无新增。");

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

    // 2026/11/16 新增：每个异步观测最多两秒，同时服从整条桌面流程的二十秒取消令牌。
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

    // 2026/11/16 新增：线程安全的纯计数适配器；取消后立即拒绝调用，不持有任何原生鼠标输出对象。
    private sealed class CountingOutput(CancellationToken token) : IClickOutput
    {
        private long count;
        internal long Count => Interlocked.Read(ref count);

        public void Click(MouseButton button, ClickKind kind, ClickPoint? point)
        {
            token.ThrowIfCancellationRequested();
            Check(button == MouseButton.Left && kind == ClickKind.Single && point is null,
                "托盘假输出只接受预设的跟随光标左键单击配置。");
            Interlocked.Increment(ref count);
        }
    }
}
