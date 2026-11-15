// 文件：PickerRegressionTests.cs
// 描述：在隐藏窗体上验证选点层等到鼠标抬起后关闭，防止抬起事件透传。
// 版本：v1.0
// 日期：2026/11/15
// 历史：
// v1.0 2026/11/15 新增单点左键及两种模式右键的关闭时机回归。

using System.Reflection;
using MouseClick.App.Interop;
using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/15 新增：直接调用真实内部窗体的受保护处理器，不调用 SendInput、不显示窗口、不操作桌面鼠标。
internal static class PickerRegressionTests
{
    public static int Run()
    {
        (string Name, bool Single, MouseButtons Button, bool ShouldClose, int Picks)[] cases =
        [
            ("固定点左键等待抬起后关闭", true, MouseButtons.Left, true, 1),
            ("固定点右键等待抬起后关闭", true, MouseButtons.Right, true, 0),
            ("多点右键等待抬起后关闭", false, MouseButtons.Right, true, 0),
            ("多点左键抬起后继续选择", false, MouseButtons.Left, false, 1)
        ];
        int failures = 0;
        foreach (var item in cases)
        {
            try
            {
                CheckMouseRelease(item.Single, item.Button, item.ShouldClose, item.Picks);
                Console.WriteLine("通过：" + item.Name + "（隐藏窗体，无输入注入）。");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"失败：{item.Name} — {exception.GetType().Name}: {exception.Message}");
            }
        }
        Console.WriteLine($"选点回归：{cases.Length - failures}/{cases.Length} 项通过。");
        return failures;
    }

    // 2026/11/15 新增：实际消息处理器若在 down 就关闭，独立的 Closed/Disposed 观测会立即失败。
    private static void CheckMouseRelease(bool single, MouseButtons button, bool shouldClose, int expectedPicks)
    {
        Type type = typeof(WindowsClickOutput).Assembly.GetType("MouseClick.App.PointPickerForm", throwOnError: true)!;
        var picks = new List<ClickPoint>();
        using var form = (Form)(Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [single, (Action<ClickPoint>)picks.Add], culture: null)
            ?? throw new InvalidOperationException("无法创建真实选点窗体。"));
        bool closed = false;
        form.FormClosed += (_, _) => closed = true;
        _ = form.Handle;
        Check(!form.Visible, "回归窗体必须始终隐藏。");
        var mouse = new MouseEventArgs(button, 1, 40, 40, 0);
        InvokeMouseHandler(type, form, "OnMouseDown", mouse);
        Check(!form.IsDisposed && !closed, "MouseDown 后窗口已经关闭或释放，鼠标抬起可能透传到下层窗口。");
        InvokeMouseHandler(type, form, "OnMouseUp", mouse);
        Check(form.IsDisposed == shouldClose && closed == shouldClose,
            shouldClose ? "MouseUp 后选点窗口应该关闭并释放。" : "多点模式左键抬起后不应关闭选点窗口。");
        Check(picks.Count == expectedPicks, $"选点回调次数应为 {expectedPicks}，实际 {picks.Count}。");
        Check(!form.Visible, "纯回归不得显示选点覆盖层。");
    }

    // 2026/11/15 新增：反射只用于访问内部 UI 类型，异常包含真实处理器错误，避免被反射包装遮蔽。
    private static void InvokeMouseHandler(Type type, Form form, string methodName, MouseEventArgs mouse)
    {
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("缺少鼠标处理器：" + methodName);
        try
        {
            method.Invoke(form, [mouse]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException($"{methodName} 处理器失败。", exception.InnerException);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
