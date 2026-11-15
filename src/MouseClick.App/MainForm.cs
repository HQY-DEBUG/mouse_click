// 文件：MainForm.cs
// 描述：中文连点器主窗口、坐标列表及运行生命周期。
// 版本：v1.2
// 日期：2026/11/15
// 历史：
// v1.2 2026/11/15 分离配置及输入依赖，允许真实窗口验收使用独立配置。
// v1.1 2026/11/15 固定 Ctrl+F8 停止提示，并将软件名称改为钰宇鼠标连点器。
// v1.0 2026/11/15 新增设置、多点循环、点选和快捷键控制。
using MouseClick.App.Interop;
using MouseClick.Core;

namespace MouseClick.App;

public sealed class MainForm : Form
{
    private readonly ComboBox _button = Ui.Choice("鼠标左键", "鼠标右键", "鼠标中键");
    private readonly ComboBox _kind = Ui.Choice("单击", "双击");
    private readonly ComboBox _mode = Ui.Choice("跟随鼠标当前位置", "固定一个位置", "多个位置顺序循环");
    private readonly ComboBox _shortcut = Ui.Choice("F6", "F9", "F10");
    private readonly NumericUpDown _interval = Ui.Number(1, 86400000, 100, "IntervalInput");
    private readonly NumericUpDown _delay = Ui.Number(0, 3600000, 1000, "DelayInput");
    private readonly NumericUpDown _repeat = Ui.Number(0, 1000000, 0, "RepeatInput");
    private readonly NumericUpDown _x = Ui.Number(int.MinValue, int.MaxValue, 0, "XInput");
    private readonly NumericUpDown _y = Ui.Number(int.MinValue, int.MaxValue, 0, "YInput");
    private readonly ListView _points = new();
    private readonly Label _status = Ui.Text("准备就绪，设置完成后按 F6 开始。");
    private readonly Label _counts = Ui.Text("0 次动作  /  0 轮", 17, true);
    private readonly Label _pointHint = Ui.Text("");
    private readonly Label _modeHint = Ui.Text("");
    // 2026/11/15 修改：界面初始提示和停止按钮统一显示固定 Ctrl+F8。
    private readonly Label _emergencyHint = Ui.Text("Ctrl+F8 紧急停止  ·  0 轮表示持续点击  ·  多位置全部走完计一轮", 9);
    private readonly Button _start = Ui.Button("开始连点 · F6", true);
    private readonly Button _stop = Ui.Button("停止 · Ctrl+F8");
    private readonly Button _capture = Ui.Button("屏幕点选 · F7", true);
    private readonly Button _add = Ui.Button("添加坐标");
    private readonly Button _remove = Ui.Button("移除");
    private readonly Button _up = Ui.Button("上移");
    private readonly Button _down = Ui.Button("下移");
    private readonly Panel _settingsPanel = new();
    private readonly Panel _positionsPanel = new();
    private readonly ClickEngine _engine;
    private readonly HotkeyWindow _hotkeys = new();
    private readonly SettingsStore _store;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 100 };
    private CancellationTokenSource? _cancellation;
    private PointPickerForm? _picker;
    private bool _loading = true;
    private bool _closeAfterStop;
    private string? _loadWarning;

    // 2026/11/15 修改：正常启动仍使用原配置目录和 Windows 输入；调用方 Program 无需变更。
    public MainForm() : this(new SettingsStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HQY.MouseClick", "settings.json")),
        new WindowsClickOutput())
    {
    }

    // 2026/11/15 新增：由构造方提供持久化与输入边界，使隔离验收运行同一套真实界面逻辑。
    internal MainForm(SettingsStore store, IClickOutput output)
    {
        _store = store;
        _engine = new ClickEngine(output);
        // 2026/11/15 修改：窗口标题采用用户指定的软件名称，保留既有设置保存位置。
        Text = "钰宇鼠标连点器";
        Name = "MouseClickMainForm";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(960, 760);
        MinimumSize = new Size(880, 760);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        BuildLayout();
        WireEvents();
        LoadSettings();
        UpdateMode();
        _loading = false;
        Shown += (_, _) =>
        {
            RegisterHotkeys();
            if (_loadWarning is not null) ShowStatus(_loadWarning, true);
        };
    }

    // 2026/11/15 新增：两栏设置与坐标列表布局，底部始终保留停止操作。
    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24, 14, 24, 18) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 91));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        Controls.Add(root);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        // 2026/11/15 修改：界面大标题与窗口标题统一使用钰宇鼠标连点器。
        header.Controls.Add(Ui.Text("钰宇鼠标连点器", 25, true));
        var subtitle = Ui.Text("设置节奏，点选位置，让重复点击自动完成。", 10);
        subtitle.ForeColor = Ui.Muted;
        header.Controls.Add(subtitle);
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        root.Controls.Add(body, 0, 1);
        _settingsPanel.Dock = DockStyle.Fill;
        _settingsPanel.BackColor = Color.White;
        _settingsPanel.Padding = new Padding(20, 12, 20, 12);
        _settingsPanel.Margin = new Padding(0, 0, 12, 0);
        _settingsPanel.AutoScroll = true;
        _positionsPanel.Dock = DockStyle.Fill;
        _positionsPanel.BackColor = Color.White;
        _positionsPanel.Padding = new Padding(20, 12, 20, 12);
        _positionsPanel.Margin = Padding.Empty;
        body.Controls.Add(_settingsPanel, 0, 0);
        body.Controls.Add(_positionsPanel, 1, 0);
        BuildSettings();
        BuildPositions();

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(0, 14, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _counts.Name = "RunCounts";
        footer.Controls.Add(_counts, 0, 0);
        _start.Name = "StartButton";
        _stop.Name = "StopButton";
        _stop.ForeColor = Ui.Danger;
        _stop.Enabled = false;
        footer.Controls.Add(Ui.Row(_start, _stop), 1, 0);
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.Name = "RunStatus";
        footer.Controls.Add(_status, 0, 1);
        footer.SetColumnSpan(_status, 2);
        _emergencyHint.ForeColor = Ui.Muted;
        footer.Controls.Add(_emergencyHint, 0, 2);
        footer.SetColumnSpan(_emergencyHint, 2);
        root.Controls.Add(footer, 0, 2);
    }

    // 2026/11/15 新增：约束数值输入范围，明确毫秒单位与无限轮数含义。
    private void BuildSettings()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        table.Controls.Add(Ui.Text("01  点击设置", 13, true));
        void Field(string caption, Control control, string name)
        {
            table.Controls.Add(Ui.Text(caption, 9));
            control.Name = name;
            control.AccessibleName = caption;
            control.Margin = new Padding(0, 0, 0, 6);
            table.Controls.Add(control);
        }
        Field("鼠标按键", _button, "ButtonChoice");
        Field("点击方式", _kind, "KindChoice");
        Field("点击间隔（毫秒）", _interval, "IntervalInput");
        Field("启动延时（毫秒）", _delay, "DelayInput");
        Field("重复轮数（0 = 持续）", _repeat, "RepeatInput");
        Field("全局开始 / 停止快捷键", _shortcut, "ShortcutChoice");
        _settingsPanel.Controls.Add(table);
    }

    // 2026/11/15 新增：列表长度就是循环位置数量，支持屏幕点选及手工调整。
    private void BuildPositions()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        for (var i = 0; i < 8; i++) table.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        table.Controls.Add(Ui.Text("02  点击位置", 13, true), 0, 0);
        _mode.Name = "ModeChoice";
        _mode.AccessibleName = "点击位置模式";
        table.Controls.Add(_mode, 0, 1);
        _modeHint.Font = new Font(Font.FontFamily, 9);
        _modeHint.ForeColor = Ui.Muted;
        _modeHint.AutoSize = false;
        _modeHint.Height = 47;
        _modeHint.Dock = DockStyle.Top;
        table.Controls.Add(_modeHint, 0, 2);
        _capture.Name = "CaptureButton";
        table.Controls.Add(Ui.Row(_capture, _up, _down, _remove), 0, 3);
        _points.Name = "PointList";
        _points.AccessibleName = "循环坐标列表";
        _points.Dock = DockStyle.Fill;
        _points.View = View.Details;
        _points.FullRowSelect = true;
        _points.MultiSelect = false;
        _points.HideSelection = false;
        _points.BorderStyle = BorderStyle.FixedSingle;
        _points.Margin = new Padding(0, 12, 0, 8);
        _points.Columns.Add("顺序", 72);
        _points.Columns.Add("X 坐标", 145);
        _points.Columns.Add("Y 坐标", 145);
        table.Controls.Add(_points, 0, 4);
        _pointHint.ForeColor = Ui.Muted;
        _pointHint.Font = new Font(Font.FontFamily, 9);
        table.Controls.Add(_pointHint, 0, 5);
        _x.Dock = _y.Dock = DockStyle.None;
        _x.Width = _y.Width = 104;
        _x.AccessibleName = "X 坐标";
        _y.AccessibleName = "Y 坐标";
        _add.Name = "AddPointButton";
        table.Controls.Add(Ui.Row(Ui.Text("X"), _x, Ui.Text("Y"), _y, _add), 0, 6);
        var hint = Ui.Text("可用负坐标；屏幕点选时右键或 Esc 完成。", 9);
        hint.ForeColor = Ui.Muted;
        table.Controls.Add(hint, 0, 7);
        _positionsPanel.Controls.Add(table);
    }

    // 2026/11/15 新增：所有界面及全局热键均复用同一套启停逻辑。
    private void WireEvents()
    {
        _start.Click += async (_, _) => await ToggleAsync();
        _stop.Click += (_, _) => StopRun();
        _capture.Click += (_, _) => CapturePoints();
        _add.Click += (_, _) => AddPoint(new ClickPoint((int)_x.Value, (int)_y.Value));
        _remove.Click += (_, _) =>
        {
            if (_points.SelectedItems.Count > 0) _points.Items.Remove(_points.SelectedItems[0]);
            RenumberPoints();
        };
        _up.Click += (_, _) => MovePoint(-1);
        _down.Click += (_, _) => MovePoint(1);
        _mode.SelectedIndexChanged += (_, _) => UpdateMode();
        _shortcut.SelectedIndexChanged += (_, _) => { if (!_loading) RegisterHotkeys(); };
        _hotkeys.ToggleRequested += async () => { if (_picker is null) await ToggleAsync(); };
        _hotkeys.StopRequested += () => { _picker?.Close(); StopRun(); };
        _hotkeys.CaptureRequested += CapturePoints;
        _refresh.Tick += (_, _) => RefreshCounts();
        FormClosing += OnClosing;
        FormClosed += (_, _) => { _refresh.Dispose(); _hotkeys.Dispose(); };
    }

    // 2026/11/15 新增：保存和恢复用户的完整坐标列表，不借用其他软件配置。
    private void LoadSettings()
    {
        ClickSettings settings;
        try { settings = _store.Load(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            settings = new ClickSettings();
            _loadWarning = "设置读取失败，已使用默认值；原文件保留。" + ex.Message;
        }
        _button.SelectedIndex = (int)settings.Button;
        _kind.SelectedIndex = (int)settings.Kind;
        _mode.SelectedIndex = (int)settings.Mode;
        _interval.Value = settings.IntervalMilliseconds;
        _delay.Value = settings.StartDelayMilliseconds;
        _repeat.Value = settings.RepeatCount;
        _x.Value = settings.FixedPoint.X;
        _y.Value = settings.FixedPoint.Y;
        _shortcut.SelectedIndex = settings.ToggleHotkey == 0x78 ? 1 : settings.ToggleHotkey == 0x79 ? 2 : 0;
        foreach (var point in settings.Points) AddPoint(point);
    }

    private ClickSettings ReadSettings() => new()
    {
        Button = (Core.MouseButton)_button.SelectedIndex,
        Kind = (ClickKind)_kind.SelectedIndex,
        Mode = (TargetMode)_mode.SelectedIndex,
        IntervalMilliseconds = (int)_interval.Value,
        StartDelayMilliseconds = (int)_delay.Value,
        RepeatCount = (int)_repeat.Value,
        FixedPoint = new ClickPoint((int)_x.Value, (int)_y.Value),
        Points = _points.Items.Cast<ListViewItem>().Select(item => (ClickPoint)item.Tag!).ToArray(),
        ToggleHotkey = _shortcut.SelectedIndex == 1 ? 0x78 : _shortcut.SelectedIndex == 2 ? 0x79 : 0x75
    };

    // 2026/11/15 新增：开始前检查全部目标；生命周期结束前禁止重入和退出。
    private async Task ToggleAsync()
    {
        if (_cancellation is not null) { StopRun(); return; }
        if (_picker is not null) return;
        // 2026/11/15 修改：固定 Ctrl+F8 不可用时禁止启动，提示释放后重试且不再建议备用组合。
        if (!_hotkeys.CanStop) { ShowStatus("Ctrl+F8 紧急停止键不可用，请释放后重新选择开始/停止快捷键重试；不会自动更换停止键。", true); return; }
        ClickSettings settings;
        try
        {
            settings = ReadSettings();
            settings.Validate();
            var targets = settings.Mode == TargetMode.Fixed ? [settings.FixedPoint] : settings.Mode == TargetMode.Sequence ? settings.Points : Array.Empty<ClickPoint>();
            foreach (var p in targets)
                if (!Screen.AllScreens.Any(screen => screen.Bounds.Contains(p.X, p.Y)))
                    throw new ArgumentException($"坐标 ({p.X}, {p.Y}) 不在当前屏幕内，请重新选取。");
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(ex.Message, true);
            return;
        }
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        SetRunning(true);
        ShowStatus($"运行中 · 启动延时 {settings.StartDelayMilliseconds} 毫秒，按 {_hotkeys.StopHotkeyText} 随时停止。");
        try
        {
            await Task.Run(() => _engine.RunAsync(settings, token), token);
            ShowStatus("已完成设定轮数。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { ShowStatus("已停止。"); }
        catch (Exception ex) { ShowStatus("运行失败：" + ex.Message, true); }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            SetRunning(false);
            RefreshCounts();
            if (_closeAfterStop) Close();
        }
    }

    private void StopRun()
    {
        if (_cancellation is null) return;
        _cancellation.Cancel();
        ShowStatus("正在停止…");
    }

    private void SetRunning(bool running)
    {
        _settingsPanel.Enabled = _positionsPanel.Enabled = !running;
        _stop.Enabled = running;
        _start.Text = running ? "停止连点" : "开始连点 · " + _shortcut.Text;
        if (running) _refresh.Start(); else _refresh.Stop();
    }

    private void RefreshCounts() => _counts.Text = $"{_engine.CompletedClicks:N0} 次动作  /  {_engine.CompletedRounds:N0} 轮";

    // 2026/11/15 新增：明确反馈热键冲突，紧急停止注册失败时禁止运行。
    private void RegisterHotkeys()
    {
        var error = _hotkeys.Register(ReadSettings().ToggleHotkey);
        _start.Text = "开始连点 · " + _shortcut.Text;
        _start.Enabled = _hotkeys.CanStop;
        _stop.Text = "停止 · " + _hotkeys.StopHotkeyText;
        _emergencyHint.Text = $"{_hotkeys.StopHotkeyText} 紧急停止  ·  0 轮表示持续点击  ·  多位置全部走完计一轮";
        ShowStatus(error ?? $"准备就绪。{_shortcut.Text} 开始 / 停止，F7 取点，{_hotkeys.StopHotkeyText} 紧急停止。", error is not null);
    }

    private void ShowStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Ui.Danger : Ui.Accent;
        _status.AccessibleDescription = text;
    }

    // 2026/11/15 新增：点选层隐藏主窗口，结束选择后恢复窗口及列表。
    private void CapturePoints()
    {
        if (_cancellation is not null || _picker is not null) return;
        if (_mode.SelectedIndex == (int)TargetMode.Cursor) _mode.SelectedIndex = (int)TargetMode.Sequence;
        var single = _mode.SelectedIndex == (int)TargetMode.Fixed;
        _picker = new PointPickerForm(single, point =>
        {
            if (single) { _x.Value = point.X; _y.Value = point.Y; }
            else AddPoint(point);
        });
        _picker.FormClosed += (_, _) =>
        {
            _picker = null;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            ShowStatus(single ? "固定坐标已更新；未点选则保留原坐标。" : $"当前列表有 {_points.Items.Count} 个位置，运行时按顺序循环。");
        };
        Hide();
        _picker.Show();
        _picker.Activate();
    }

    private void AddPoint(ClickPoint point)
    {
        var item = new ListViewItem([(_points.Items.Count + 1).ToString(), point.X.ToString(), point.Y.ToString()]) { Tag = point };
        _points.Items.Add(item);
        RenumberPoints();
    }

    private void MovePoint(int direction)
    {
        if (_points.SelectedIndices.Count == 0) return;
        var old = _points.SelectedIndices[0];
        var next = old + direction;
        if (next < 0 || next >= _points.Items.Count) return;
        var item = _points.Items[old];
        _points.Items.RemoveAt(old);
        _points.Items.Insert(next, item);
        item.Selected = true;
        item.EnsureVisible();
        RenumberPoints();
    }

    private void RenumberPoints()
    {
        for (var i = 0; i < _points.Items.Count; i++) _points.Items[i].Text = (i + 1).ToString();
        _pointHint.Text = $"共 {_points.Items.Count} 个位置  ·  顺序从上到下";
    }

    private void UpdateMode()
    {
        var mode = (TargetMode)_mode.SelectedIndex;
        _points.Enabled = _add.Enabled = _remove.Enabled = _up.Enabled = _down.Enabled = mode == TargetMode.Sequence;
        _x.Enabled = _y.Enabled = mode != TargetMode.Cursor;
        _modeHint.Text = mode switch
        {
            TargetMode.Cursor => "点击光标所在位置，运行时可随时移动鼠标。",
            TargetMode.Fixed => "每次点击下方 X / Y 指定的位置，使用屏幕点选可快速设置。",
            _ => "依次点击列表中的所有位置，走完后从第一个位置继续。"
        };
        RenumberPoints();
    }

    // 2026/11/15 新增：关闭先取消并等待输入结束，再保存设置及释放热键。
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_cancellation is not null)
        {
            e.Cancel = true;
            _closeAfterStop = true;
            StopRun();
            return;
        }
        _picker?.Close();
        try
        {
            var settings = ReadSettings();
            // 2026/11/15 新增：空序列只是未配置的草稿，关闭时仍保留其他设置。
            if (settings.Mode == TargetMode.Sequence && settings.Points.Count == 0) settings = settings with { Mode = TargetMode.Cursor };
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, "设置未能保存：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
