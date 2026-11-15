// 文件：MainFormSettingsRegressionTests.cs
// 描述：在隐藏主窗体中验证秒制小数输入与既有毫秒配置之间的加载、读取和保存。
// 版本：v1.0
// 日期：2026/11/15
// 历史：
// v1.0 2026/11/15 新增旧配置兼容、小数回存、精度与上下限及整数控件回归。

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
            ("超过三位的小数按显示精度执行和保存", CheckDisplayedPrecision)
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
        Console.WriteLine($"主窗体时间设置回归：{cases.Length - failures}/{cases.Length} 项通过。");
        return failures;
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
            form.Close();
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
