// 文件：PreferencesForm.cs
// 描述：编辑点击主窗口关闭按钮时的默认行为。
// 版本：v1.0
// 日期：2026/11/16
// 历史：
// v1.0 2026/11/16 新增每次询问、最小化到托盘和直接退出设置。

using MouseClick.Core;

namespace MouseClick.App;

// 2026/11/16 新增：仅在保存时向 MainForm 提交关闭偏好，取消保留调用方传入的设置。
internal sealed class PreferencesForm : Form
{
    private readonly ComboBox _closeBehavior = Ui.Choice("每次询问", "最小化到系统托盘", "直接退出");

    internal WindowCloseBehavior SelectedCloseBehavior { get; private set; }

    internal PreferencesForm(WindowCloseBehavior current)
    {
        SelectedCloseBehavior = current;
        _closeBehavior.SelectedIndex = current switch
        {
            WindowCloseBehavior.Ask => 0,
            WindowCloseBehavior.MinimizeToTray => 1,
            WindowCloseBehavior.Exit => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(current))
        };
        Text = "设置";
        Name = "PreferencesForm";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        ClientSize = new Size(490, 225);

        // 2026/11/16 新增：固定客户区并填充表格，避免对话框自动尺寸收缩后裁切说明和下拉选项。
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24, 20, 24, 20)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 4; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(Ui.Text("点击主窗口 × 时", 11, true));
        _closeBehavior.Name = "CloseBehaviorChoice";
        _closeBehavior.AccessibleName = "点击主窗口关闭按钮时的行为";
        _closeBehavior.Margin = new Padding(0, 8, 0, 12);
        layout.Controls.Add(_closeBehavior);
        var hint = Ui.Text("最小化到托盘后，可通过托盘菜单退出程序。", 9);
        hint.ForeColor = Ui.Muted;
        hint.MaximumSize = new Size(442, 0);
        hint.Margin = new Padding(0, 0, 0, 20);
        layout.Controls.Add(hint);

        var save = Ui.Button("保存", true);
        save.Name = "SavePreferencesButton";
        save.DialogResult = DialogResult.OK;
        save.Click += (_, _) => SelectedCloseBehavior = _closeBehavior.SelectedIndex switch
        {
            0 => WindowCloseBehavior.Ask,
            1 => WindowCloseBehavior.MinimizeToTray,
            _ => WindowCloseBehavior.Exit
        };
        var cancel = Ui.Button("取消");
        cancel.Name = "CancelPreferencesButton";
        cancel.DialogResult = DialogResult.Cancel;
        layout.Controls.Add(Ui.Row(save, cancel));
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }
}
