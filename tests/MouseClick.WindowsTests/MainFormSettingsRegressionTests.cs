// 文件：MainFormSettingsRegressionTests.cs
// 描述：在隐藏主窗体中验证时间、快捷键和坐标设置的布局与保存。
// 版本：v1.3
// 日期：2026/11/23
// 历史：
// v1.3 2026/11/23 新增三种位置模式的按钮布局、手动追加和切换保存回归。
// v1.2 2026/11/23 覆盖新增 F8 和所有已有启停键的配置回读。
// v1.1 2026/11/16 清理改用显式退出，避免默认关闭询问干扰隐藏设置回归。

using System.Globalization;
using System.Reflection;
using MouseClick.App;
using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/15 新增：实际主窗体始终隐藏，使用独立配置和拒绝输出适配器，不注册热键或注入输入。
internal static class MainFormSettingsRegressionTests
{
    internal static int Run()
    {
        (string Name, Action Check)[] cases =
        [
            ("旧毫秒配置按秒显示并保留原时长", CheckLegacyMilliseconds),
            ("默认间隔和延时按秒显示", CheckDefaults),
            ("小数秒回存为毫秒且整数设置不变", CheckFractionalRoundTrip),
            ("秒制输入上下限保留原时长范围", CheckBoundaries),
            ("超过三位的小数按显示精度执行和保存", CheckDisplayedPrecision),
            // 2026/11/23 新增：独立配置检查新增选项，避免旧快捷键随选项索引变化。
            ("四种启停键加载、显示和回存一致", CheckToggleHotkeys),
            // 2026/11/23 新增：通过实际布局和按钮检查三种位置模式。
            ("单点同行选点，多点在列表上方添加，切换后保留位置", CheckPositionModes)
        ];
        int failures = 0;
        foreach (var item in cases)
        {
            try
            {
                item.Check();
                Console.WriteLine("通过：" + item.Name + "（隐藏窗体，无输入注入）。");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"失败：{item.Name} — {exception.GetType().Name}: {exception.Message}");
            }
        }
        Console.WriteLine($"主窗体设置回归：{cases.Length - failures}/{cases.Length} 项通过。");
        return failures;
    }

    // 2026/11/23 新增：借用无顶层窗口的面板布局真实控件，让 Visible 和 PerformClick 按当前模式生效。
    private static void CheckPositionModes()
    {
        var first = new ClickPoint(17, 25);
        var second = new ClickPoint(-40, 82);
        var initial = new ClickSettings { Mode = TargetMode.Fixed, FixedPoint = second, Points = [first] };
        ClickSettings saved = WithHiddenForm(initial, form =>
        {
            ComboBox mode = Find<ComboBox>(form, "ModeChoice");
            NumericUpDown x = Find<NumericUpDown>(form, "XInput");
            NumericUpDown y = Find<NumericUpDown>(form, "YInput");
            Button capture = Find<Button>(form, "CaptureButton");
            Button add = Find<Button>(form, "AddPointButton");
            ListView list = Find<ListView>(form, "PointList");
            Control layout = form.Controls[0];
            using var host = new Panel { Size = form.ClientSize, Font = form.Font };
            host.Controls.Add(layout);
            try
            {
                host.CreateControl();
                Check(x.Visible && y.Visible && capture.Visible && capture.Enabled && capture.Text.Contains("F7") &&
                    !add.Visible && !list.Visible, "固定位置应只显示坐标和选点入口。");
                CheckBeside(y, capture, "固定位置的选点按钮");
                mode.SelectedIndex = (int)TargetMode.Sequence;
                Check(x.Visible && y.Visible && add.Visible && list.Visible && capture.Visible && capture.Enabled,
                    "多点模式应显示手动输入、添加、选点与坐标列表。");
                CheckBeside(y, add, "多点模式的添加按钮");
                Check(add.RectangleToScreen(add.ClientRectangle).Bottom <= list.PointToScreen(Point.Empty).Y,
                    "添加坐标应在列表上方。");
                Check(list.Items.Count == 1, "已有一个位置的循环仍应允许添加。");
                add.PerformClick();
                Check(list.Items.Count == 2 && Equals(list.Items[1].Tag, second),
                    "添加按钮应在原有位置后追加输入的坐标。");
                mode.SelectedIndex = (int)TargetMode.Cursor;
                Check(capture.Visible && !x.Visible && !y.Visible && !add.Visible && !list.Visible,
                    "跟随鼠标时应保留选点入口并收起坐标编辑控件。");
                mode.SelectedIndex = (int)TargetMode.Fixed;
                Check(x.Value == second.X && y.Value == second.Y, "切回固定模式应保留输入坐标。");
            }
            finally
            {
                form.Controls.Add(layout);
            }
        });
        Check(saved.FixedPoint == second && saved.Points.SequenceEqual([first, second]),
            "退出后应同时保存固定位置和原有顺序的循环列表。");
    }

    // 2026/11/23 新增：比较控件实际矩形，捕获按钮因换行、重叠或行顺序错误导致的位置偏差。
    private static void CheckBeside(Control input, Button action, string name)
    {
        Rectangle box = input.RectangleToScreen(input.ClientRectangle);
        Rectangle button = action.RectangleToScreen(action.ClientRectangle);
        Check(button.Left >= box.Right && button.Top < box.Bottom && box.Top < button.Bottom,
            $"{name}应在 Y 坐标右侧同一行，实际坐标框 {box}、按钮 {button}。");
    }

    // 2026/11/23 新增：从配置加载每个合法键，核对显示项和退出后实际保存的虚拟键。
    private static void CheckToggleHotkeys()
    {
        foreach (Keys key in new[] { Keys.F6, Keys.F8, Keys.F9, Keys.F10 })
        {
            ClickSettings saved = WithHiddenForm(new ClickSettings { ToggleHotkey = (int)key }, form =>
                Check(Find<ComboBox>(form, "ShortcutChoice").Text == key.ToString(), $"未选中保存的 {key}。"));
            Check(saved.ToggleHotkey == (int)key, $"关闭后 {key} 应原样回存。");
        }
    }

    // 2026/11/15 新增：捕获直接把旧毫秒值显示为秒或在关闭保存时重复换算的错误。
    private static void CheckLegacyMilliseconds()
    {
        ClickSettings saved = WithHiddenForm(new ClickSettings
        {
            IntervalMilliseconds = 5000,
            StartDelayMilliseconds = 1250
        }, form =>
        {
            NumericUpDown interval = Find<NumericUpDown>(form, "IntervalInput");
            NumericUpDown delay = Find<NumericUpDown>(form, "DelayInput");
            Check(interval.Value == 5m && delay.Value == 1.25m,
                $"5000/1250 毫秒应显示为 5/1.25 秒，实际 {interval.Value}/{delay.Value}。");
            Check(interval.Text == 5m.ToString("F3", CultureInfo.CurrentCulture), "旧间隔应以三位小数显示为 5.000 秒。");
            Check(interval.AccessibleName == "点击间隔（s）" && delay.AccessibleName == "启动延时（s）",
                "两个时间输入的可访问说明应明确使用 s 单位。");
            AssertTimes(ReadSettings(form), 5000, 1250);
        });
        AssertTimes(saved, 5000, 1250);
    }

    // 2026/11/15 新增：配置不存在时仍保留原先 100 毫秒间隔与 1000 毫秒启动延时。
    private static void CheckDefaults()
    {
        ClickSettings saved = WithHiddenForm(null, form =>
        {
            Check(Find<NumericUpDown>(form, "IntervalInput").Value == 0.1m,
                "默认点击间隔应显示为 0.1 秒。");
            Check(Find<NumericUpDown>(form, "DelayInput").Value == 1m,
                "默认启动延时应显示为 1 秒。");
        });
        AssertTimes(saved, 100, 1000);
    }

    // 2026/11/15 新增：真实文本输入经 ReadSettings 与关闭保存回存，坐标和轮数继续使用整数。
    private static void CheckFractionalRoundTrip()
    {
        ClickSettings saved = WithHiddenForm(new ClickSettings { Mode = TargetMode.Fixed }, form =>
        {
            NumericUpDown interval = Find<NumericUpDown>(form, "IntervalInput");
            NumericUpDown delay = Find<NumericUpDown>(form, "DelayInput");
            interval.Text = 0.125m.ToString(CultureInfo.CurrentCulture);
            delay.Text = 1.25m.ToString(CultureInfo.CurrentCulture);
            NumericUpDown repeat = Find<NumericUpDown>(form, "RepeatInput");
            NumericUpDown x = Find<NumericUpDown>(form, "XInput");
            NumericUpDown y = Find<NumericUpDown>(form, "YInput");
            repeat.Value = 7;
            x.Value = -123;
            y.Value = 456;
            foreach (NumericUpDown input in new[] { repeat, x, y })
                Check(input.DecimalPlaces == 0 && input.Increment == 1,
                    $"{input.Name} 应保持整数精度和步进。");
            foreach (NumericUpDown input in new[] { interval, delay })
                Check(input.DecimalPlaces == 3 && input.Increment == 0.1m && !input.ThousandsSeparator,
                    $"{input.Name} 应支持三位小数、0.1 秒步进且不显示千位分隔符。");
            AssertTimes(ReadSettings(form), 125, 1250);
        });
        AssertTimes(saved, 125, 1250);
        Check(saved.Mode == TargetMode.Fixed && saved.FixedPoint == new ClickPoint(-123, 456) && saved.RepeatCount == 7,
            "秒制小数保存不应改变固定模式、整数坐标或重复轮数。");
        WithHiddenForm(saved, form =>
        {
            Check(Find<NumericUpDown>(form, "IntervalInput").Value == 0.125m &&
                Find<NumericUpDown>(form, "DelayInput").Value == 1.25m,
                "重新打开保存的配置应恢复相同小数秒。");
        });
    }

    // 2026/11/15 新增：首尾合法秒数必须准确转换为既有毫秒上下限，避免缩短范围或产生零间隔。
    private static void CheckBoundaries()
    {
        ClickSettings saved = WithHiddenForm(null, form =>
        {
            NumericUpDown interval = Find<NumericUpDown>(form, "IntervalInput");
            NumericUpDown delay = Find<NumericUpDown>(form, "DelayInput");
            Check(interval.Minimum == 0.001m && interval.Maximum == 86400m,
                "点击间隔应允许 0.001～86400 秒。");
            Check(delay.Minimum == 0 && delay.Maximum == 3600m,
                "启动延时应允许 0～3600 秒。");
            interval.Value = 0.001m;
            delay.Value = 0;
            AssertTimes(ReadSettings(form), 1, 0);
            interval.Value = 86400m;
            delay.Value = 3600m;
            AssertTimes(ReadSettings(form), 86400000, 3600000);
        });
        AssertTimes(saved, 86400000, 3600000);
    }

    // 2026/11/15 新增：第四位小数按四舍五入归一，禁止界面显示的值与实际执行时长不一致。
    private static void CheckDisplayedPrecision()
    {
        ClickSettings saved = WithHiddenForm(null, form =>
        {
            NumericUpDown interval = Find<NumericUpDown>(form, "IntervalInput");
            NumericUpDown delay = Find<NumericUpDown>(form, "DelayInput");
            interval.Text = 0.1235m.ToString(CultureInfo.CurrentCulture);
            delay.Text = 1.2346m.ToString(CultureInfo.CurrentCulture);
            AssertTimes(ReadSettings(form), 124, 1235);
            Check(interval.Value == 0.124m && delay.Value == 1.235m,
                "超过三位的小数应归一为 0.124/1.235 秒。");
            Check(interval.Text == 0.124m.ToString("F3", CultureInfo.CurrentCulture) &&
                delay.Text == 1.235m.ToString("F3", CultureInfo.CurrentCulture),
                "时间输入显示应与三位小数的执行设置一致。");
        });
        AssertTimes(saved, 124, 1235);
    }

    // 2026/11/15 新增：创建句柄以覆盖真实关闭事件，但不调用 Show，确保热键注册的 Shown 事件不触发。
    private static ClickSettings WithHiddenForm(ClickSettings? initialSettings, Action<MainForm> check)
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), "MouseClick.WindowsTests",
            Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(settingsPath);
        if (initialSettings is not null) store.Save(initialSettings);
        using var form = new MainForm(store, new RejectClickOutput());
        _ = form.Handle;
        try
        {
            Check(!form.Visible, "设置回归的主窗体不得显示。");
            check(form);
            Check(!form.Visible, "设置读取不得显示主窗体。");
        }
        finally
        {
            // 2026/11/16 修改：设置回归结束明确退出，绕过默认关闭询问并正常保存配置。
            form.RequestExit();
        }
        Check(form.IsDisposed, "隐藏主窗体关闭后应释放窗口和热键资源。");
        return store.Load();
    }

    // 2026/11/15 新增：只反射实际运行和保存共用的设置快照入口，保留原始异常以定位换算失败。
    private static ClickSettings ReadSettings(MainForm form)
    {
        MethodInfo method = typeof(MainForm).GetMethod("ReadSettings", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("主窗体缺少设置读取入口。");
        try
        {
            return (ClickSettings)(method.Invoke(form, null)
                ?? throw new InvalidOperationException("设置读取入口未返回快照。"));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException("主窗体设置读取失败：" + exception.InnerException.Message, exception.InnerException);
        }
    }

    // 2026/11/15 新增：断言独立的期望毫秒值，并运行核心校验确认保存与执行均能接受该设置。
    private static void AssertTimes(ClickSettings settings, int interval, int delay)
    {
        Check(settings.IntervalMilliseconds == interval && settings.StartDelayMilliseconds == delay,
            $"设置应为间隔 {interval} / 延时 {delay} 毫秒，实际 {settings.IntervalMilliseconds}/{settings.StartDelayMilliseconds}。");
        settings.Validate();
    }

    private static T Find<T>(Control form, string name) where T : Control =>
        form.Controls.Find(name, searchAllChildren: true).SingleOrDefault() as T
        ?? throw new InvalidOperationException($"缺少唯一的 {typeof(T).Name} 控件：{name}。");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // 2026/11/15 新增：任何意外启动直接报告失败，绝不把测试动作转发到 Windows 输入接口。
    private sealed class RejectClickOutput : IClickOutput
    {
        public void Click(MouseButton button, ClickKind kind, ClickPoint? point) =>
            throw new InvalidOperationException("设置回归不允许发送鼠标输入。");
    }
}
