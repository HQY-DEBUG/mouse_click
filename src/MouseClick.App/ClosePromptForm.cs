// 文件：ClosePromptForm.cs
// 描述：询问主窗口关闭后最小化到托盘或退出，并返回是否记住选择。
// 版本：v1.0
// 日期：2026/11/16
// 历史：
// v1.0 2026/11/16 新增关闭选择与记住偏好的对话框。

using MouseClick.Core;

namespace MouseClick.App;

// 2026/11/16 新增：向 MainForm 返回本次关闭动作和独立的记忆选项，取消不产生选择。
internal readonly record struct CloseChoice(WindowCloseBehavior Action, bool Remember);

// 2026/11/16 新增：复用主窗口配色，以固定客户区和随 DPI 缩放的布局完整显示提示及操作按钮。
internal sealed class ClosePromptForm : Form
{
    private readonly CheckBox _remember = new()
    {
        Name = "RememberChoice",
        Text = "记住我的选择（可在设置中修改）",
        AutoSize = true,
        Margin = new Padding(0, 16, 0, 20)
    };

    internal CloseChoice? Decision { get; private set; }

    internal ClosePromptForm()
    {
        Text = "关闭钰宇鼠标连点器";
        Name = "ClosePromptForm";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        ClientSize = new Size(570, 210);

        // 2026/11/16 新增：固定窗体客户区并让表格填充，避免 AutoSize 与顶部停靠相互缩小窗口宽度。
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 20, 24, 20)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 3; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var message = Ui.Text("最小化到托盘后会继续运行；退出会停止连点。");
        message.MaximumSize = new Size(522, 0);
        layout.Controls.Add(message);
        layout.Controls.Add(_remember);

        var minimize = Ui.Button("最小化到托盘", true);
        minimize.Name = "MinimizeToTrayButton";
        minimize.Click += (_, _) => Choose(WindowCloseBehavior.MinimizeToTray);
        var exit = Ui.Button("退出");
        exit.Name = "ExitApplicationButton";
        exit.ForeColor = Ui.Danger;
        exit.Click += (_, _) => Choose(WindowCloseBehavior.Exit);
        var cancel = Ui.Button("取消");
        cancel.Name = "CancelButton";
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Click += (_, _) => Decision = null;
        layout.Controls.Add(Ui.Row(minimize, exit, cancel));
        Controls.Add(layout);
        AcceptButton = minimize;
        CancelButton = cancel;
    }

    // 2026/11/16 新增：只有明确选择托盘或退出才向调用方提交动作，记忆选项与本次动作一起返回。
    private void Choose(WindowCloseBehavior action)
    {
        Decision = new CloseChoice(action, _remember.Checked);
        DialogResult = DialogResult.OK;
    }

    // 2026/11/16 新增：标题栏关闭和取消统一返回空决策，避免把取消误记为默认的托盘或退出选择。
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
        {
            Decision = null;
            DialogResult = DialogResult.Cancel;
        }
        base.OnFormClosing(e);
    }
}
