// 文件：PreferencesDialogTests.cs
// 描述：在隐藏对话框中验证关闭动作、记住选择及偏好保存和取消。
// 版本：v1.0
// 日期：2026/11/16
// 历史：
// v1.0 2026/11/16 新增关闭确认与设置对话框的非桌面回归。

using System.Reflection;
using MouseClick.App;
using MouseClick.Core;

namespace MouseClick.WindowsTests;

// 2026/11/16 新增：直接触发真实按钮处理器，不显示窗体、不发送输入，也不写用户配置。
internal static class PreferencesDialogTests
{
    internal static int Run()
    {
        (string Name, Action Check)[] cases =
        [
            ("关闭偏好恢复当前选择", CheckInitialPreference),
            ("保存提交全部关闭偏好", CheckSavePreference),
            ("取消设置不提交编辑", CheckCancelPreference),
            ("关闭确认保留动作和记忆选择", CheckCloseChoices),
            ("关闭确认取消不产生动作", CheckCancelPrompt),
            ("关闭确认标题栏关闭不产生动作", CheckClosePrompt),
            ("对话框布局完整容纳文本与按钮", CheckDialogLayout)
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
        Console.WriteLine($"关闭对话框回归：{cases.Length - failures}/{cases.Length} 项通过。");
        return failures;
    }

    // 2026/11/16 新增：三个持久化值分别恢复到对应中文选项，构造期间不改变当前偏好。
    private static void CheckInitialPreference()
    {
        (WindowCloseBehavior Behavior, string Caption)[] preferences =
        [
            (WindowCloseBehavior.Ask, "每次询问"),
            (WindowCloseBehavior.MinimizeToTray, "最小化到系统托盘"),
            (WindowCloseBehavior.Exit, "直接退出")
        ];
        foreach (var item in preferences)
        {
            using var form = new PreferencesForm(item.Behavior);
            Check(Find<ComboBox>(form, "CloseBehaviorChoice").Text == item.Caption,
                $"{item.Behavior} 应恢复为 {item.Caption}。");
            Check(form.SelectedCloseBehavior == item.Behavior, "打开设置不得改变当前偏好。");
            Check(!form.Visible, "设置检查不得显示窗口。");
        }
    }

    // 2026/11/16 新增：编辑只改变待提交选项，保存按钮才更新返回给主窗口的偏好。
    private static void CheckSavePreference()
    {
        WindowCloseBehavior[] expected = [WindowCloseBehavior.Ask, WindowCloseBehavior.MinimizeToTray, WindowCloseBehavior.Exit];
        for (int index = 0; index < expected.Length; index++)
        {
            using var form = new PreferencesForm(WindowCloseBehavior.Exit);
            Find<ComboBox>(form, "CloseBehaviorChoice").SelectedIndex = index;
            Check(form.SelectedCloseBehavior == WindowCloseBehavior.Exit, "尚未保存时不应提交偏好。");
            Click(Find<Button>(form, "SavePreferencesButton"));
            Check(form.DialogResult == DialogResult.OK && form.SelectedCloseBehavior == expected[index],
                "保存应确认对话框并返回用户选定的关闭行为。");
            Check(!form.Visible, "保存检查不得显示窗口。");
        }
    }

    // 2026/11/16 新增：取消按钮及标题栏关闭均不能把未保存的下拉选择提交给主窗口。
    private static void CheckCancelPreference()
    {
        using var form = new PreferencesForm(WindowCloseBehavior.Ask);
        Find<ComboBox>(form, "CloseBehaviorChoice").SelectedIndex = 2;
        Button cancel = Find<Button>(form, "CancelPreferencesButton");
        Check(ReferenceEquals(form.CancelButton, cancel) && cancel.DialogResult == DialogResult.Cancel,
            "Esc 应绑定取消按钮。");
        Click(cancel);
        Check(form.DialogResult == DialogResult.Cancel && form.SelectedCloseBehavior == WindowCloseBehavior.Ask,
            "取消应保留传入的每次询问偏好。");
        using var closed = new PreferencesForm(WindowCloseBehavior.MinimizeToTray);
        _ = closed.Handle;
        Find<ComboBox>(closed, "CloseBehaviorChoice").SelectedIndex = 2;
        closed.Close();
        Check(closed.SelectedCloseBehavior == WindowCloseBehavior.MinimizeToTray,
            "标题栏关闭应保留传入的托盘偏好。");
    }

    // 2026/11/16 新增：托盘和退出分别携带勾选与未勾选状态，防止选择动作和保存意愿混淆。
    private static void CheckCloseChoices()
    {
        (string Button, WindowCloseBehavior Behavior)[] actions =
        [
            ("MinimizeToTrayButton", WindowCloseBehavior.MinimizeToTray),
            ("ExitApplicationButton", WindowCloseBehavior.Exit)
        ];
        foreach (var action in actions)
        foreach (bool remember in new[] { false, true })
        {
            using var form = new ClosePromptForm();
            Find<CheckBox>(form, "RememberChoice").Checked = remember;
            Check(ReferenceEquals(form.AcceptButton, Find<Button>(form, "MinimizeToTrayButton")),
                "回车默认动作应为最小化到托盘。");
            Click(Find<Button>(form, action.Button));
            Check(form.DialogResult == DialogResult.OK && form.Decision == new CloseChoice(action.Behavior, remember),
                $"{action.Behavior} 应返回准确动作与记忆选项 {remember}。");
            Check(!form.Visible, "关闭选择检查不得显示窗口。");
        }
    }

    // 2026/11/16 新增：即使勾选记忆，取消仍应返回空决策，Esc 使用同一个取消按钮。
    private static void CheckCancelPrompt()
    {
        using var form = new ClosePromptForm();
        Find<CheckBox>(form, "RememberChoice").Checked = true;
        Button cancel = Find<Button>(form, "CancelButton");
        Check(ReferenceEquals(form.CancelButton, cancel) && cancel.DialogResult == DialogResult.Cancel,
            "Esc 应绑定关闭确认的取消按钮。");
        Click(cancel);
        Check(form.DialogResult == DialogResult.Cancel && form.Decision is null,
            "取消不能返回关闭动作或可保存的偏好。");
    }

    // 2026/11/16 新增：创建隐藏句柄并走真实关闭事件，确认标题栏关闭的结果与取消一致。
    private static void CheckClosePrompt()
    {
        using var form = new ClosePromptForm();
        _ = form.Handle;
        Find<CheckBox>(form, "RememberChoice").Checked = true;
        form.Close();
        Check(form.IsDisposed && form.DialogResult == DialogResult.Cancel && form.Decision is null,
            "标题栏关闭应释放窗体并返回取消和空决策。");
        Check(!form.Visible, "关闭检查不得显示窗口。");
    }

    // 2026/11/16 新增：隐藏创建句柄和布局，捕获窗口缩成窄条、按钮越界或文本被裁切的回归。
    private static void CheckDialogLayout()
    {
        using var prompt = new ClosePromptForm();
        using var preferences = new PreferencesForm(WindowCloseBehavior.Ask);
        foreach (Form form in new Form[] { prompt, preferences })
        {
            _ = form.Handle;
            form.PerformLayout();
            Check(form.ClientSize.Width >= 480, $"{form.Name} 客户区异常收窄：{form.ClientSize}。");
            CheckControlLayout(form);
            Check(!form.Visible, "布局检查不得显示对话框。");
        }
    }

    // 2026/11/16 新增：逐层确认控件位于父容器内，按钮完整显示文字，标签保留按当前宽度换行所需高度。
    private static void CheckControlLayout(Control parent)
    {
        parent.PerformLayout();
        foreach (Control control in parent.Controls)
        {
            Check(parent.ClientRectangle.Contains(control.Bounds),
                $"{control.Name}/{control.Text} 超出 {parent.Name} 客户区：{control.Bounds}。");
            if (control is Button or CheckBox)
                Check(control.Width >= control.PreferredSize.Width && control.Height >= control.PreferredSize.Height,
                    $"{control.Text} 未完整容纳按钮或复选框文字。");
            if (control is Label)
                Check(control.Height >= control.GetPreferredSize(new Size(control.Width, 0)).Height,
                    $"{control.Text} 的提示文本高度不足。");
            CheckControlLayout(control);
        }
    }

    // 2026/11/16 新增：隐藏控件不能 PerformClick，直接调用真实 OnClick 事件管道且保留内部异常。
    private static void Click(Button button)
    {
        MethodInfo method = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("缺少按钮事件入口。");
        try
        {
            method.Invoke(button, [EventArgs.Empty]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException("对话框按钮处理失败：" + exception.InnerException.Message, exception.InnerException);
        }
    }

    // 2026/11/16 新增：按稳定控件名称定位实际对话框输入，缺失或断言失败明确报告回归错误。
    private static T Find<T>(Control form, string name) where T : Control =>
        form.Controls.Find(name, searchAllChildren: true).SingleOrDefault() as T
        ?? throw new InvalidOperationException($"缺少唯一的 {typeof(T).Name} 控件：{name}。");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
