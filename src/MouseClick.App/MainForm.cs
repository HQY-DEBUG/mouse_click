// 文件：MainForm.cs
// 描述：中文连点器主窗口、坐标列表及运行生命周期。
// 版本：v1.6
// 日期：2026/11/16
// 历史：
// v1.6 2026/11/16 新增托盘后台运行、关闭询问和可修改的记忆偏好。
// v1.5 2026/11/15 时间输入及提示改为秒，保留毫秒配置兼容性。
// v1.4 2026/11/15 正常启动按便携标记选择独立设置目录。
using MouseClick.App.Interop;
using MouseClick.Core;

namespace MouseClick.App;

public sealed class MainForm : Form
{
    private readonly ComboBox _button = Ui.Choice("鼠标左键", "鼠标右键", "鼠标中键");
    private readonly ComboBox _kind = Ui.Choice("单击", "双击");
    private readonly ComboBox _mode = Ui.Choice("跟随鼠标当前位置", "固定一个位置", "多个位置顺序循环");
    private readonly ComboBox _shortcut = Ui.Choice("F6", "F9", "F10");
    // 2026/11/15 修改：界面以秒输入，范围和默认实际时长与既有毫秒配置一致。
    private readonly NumericUpDown _interval = Ui.Seconds(0.001m, 86400m, 0.1m, "IntervalInput");
    private readonly NumericUpDown _delay = Ui.Seconds(0m, 3600m, 1m, "DelayInput");
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

    // 2026/11/16 新增：托盘生命周期独立于窗口可见性，测试可关闭真实图标并注入选择。
    private readonly Button _preferences = Ui.Button("设置");
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _trayToggle = new("开始连点");
    private readonly ToolStripMenuItem _trayStop = new("停止 · Ctrl+F8");
    private readonly Func<CloseChoice?>? _promptForClose;
    private readonly bool _enableTrayIcon;
    private WindowCloseBehavior _closeBehavior;
    private bool _exitRequested;
    private bool _closePromptOpen;
    private bool _preferencesOpen;
    internal bool IsInTray { get; private set; }

    // 2026/11/15 修改：正常启动按程序目录便携标记选择配置位置；调用方 Program 无需变更。
    public MainForm() : this(new SettingsStore(SettingsStore.GetDefaultPath(AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))),
        new WindowsClickOutput())
    {
    }

    // 2026/11/16 修改：WindowsTests 可注入关闭选择并禁用真实托盘；Program 默认保持完整桌面行为。
    internal MainForm(SettingsStore store, IClickOutput output,
        Func<CloseChoice?>? promptForClose = null, bool enableTrayIcon = true)
    {
        _store = store;
        _engine = new ClickEngine(output);
        _promptForClose = promptForClose;
        _enableTrayIcon = enableTrayIcon;
        // 2026/11/15 新增：窗口及任务栏使用嵌入式图标，释放窗体时一并释放图标副本。
        using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("MouseClick.AppIcon.ico")!)
        using (var sourceIcon = new Icon(iconStream))
        {
            var windowIcon = (Icon)sourceIcon.Clone();
            Icon = windowIcon;
            Disposed += (_, _) => windowIcon.Dispose();
        }
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
        BuildTray();
        WireEvents();
        LoadSettings();
        UpdateMode();
        _loading = false;
        Shown += (_, _) =>
        {
            _tray.Visible = _enableTrayIcon;
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
        // 2026/11/16 修改：设置入口置于标题区，运行时仍可调整关闭偏好。
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // 2026/11/15 修改：界面大标题与窗口标题统一使用钰宇鼠标连点器。
        header.Controls.Add(Ui.Text("钰宇鼠标连点器", 25, true), 0, 0);
        _preferences.Name = "PreferencesButton";
        header.Controls.Add(_preferences, 1, 0);
        var subtitle = Ui.Text("设置节奏，点选位置，让重复点击自动完成。", 10);
        subtitle.ForeColor = Ui.Muted;
        header.Controls.Add(subtitle, 0, 1);
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

    // 2026/11/15 修改：时间标签及可访问名称统一标注 s，轮数仍为整数。
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
        Field("点击间隔（s）", _interval, "IntervalInput");
        Field("启动延时（s）", _delay, "DelayInput");
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
        // 2026/11/16 新增：主窗口设置与托盘设置共用即时保存入口。
        _preferences.Click += (_, _) => ShowPreferences();
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
        // 2026/11/16 新增：恢复关闭偏好，缺少字段的旧配置由核心层默认为询问。
        _closeBehavior = settings.CloseBehavior;
        _kind.SelectedIndex = (int)settings.Kind;
        _mode.SelectedIndex = (int)settings.Mode;
        // 2026/11/15 修改：旧配置仍为整数毫秒，除以 decimal 1000 保留原始时长及小数精度。
        _interval.Value = settings.IntervalMilliseconds / 1000m;
        _delay.Value = settings.StartDelayMilliseconds / 1000m;
        _repeat.Value = settings.RepeatCount;
        _x.Value = settings.FixedPoint.X;
        _y.Value = settings.FixedPoint.Y;
        _shortcut.SelectedIndex = settings.ToggleHotkey == 0x78 ? 1 : settings.ToggleHotkey == 0x79 ? 2 : 0;
        foreach (var point in settings.Points) AddPoint(point);
    }

    // 2026/11/15 修改：MainForm 内部调用仍接收毫秒设置，秒输入仅在界面边界转换。
    private ClickSettings ReadSettings() => new()
    {
        Button = (Core.MouseButton)_button.SelectedIndex,
        Kind = (ClickKind)_kind.SelectedIndex,
        Mode = (TargetMode)_mode.SelectedIndex,
        IntervalMilliseconds = decimal.ToInt32(_interval.Value * 1000m),
        StartDelayMilliseconds = decimal.ToInt32(_delay.Value * 1000m),
        RepeatCount = (int)_repeat.Value,
        FixedPoint = new ClickPoint((int)_x.Value, (int)_y.Value),
        Points = _points.Items.Cast<ListViewItem>().Select(item => (ClickPoint)item.Tag!).ToArray(),
        ToggleHotkey = _shortcut.SelectedIndex == 1 ? 0x78 : _shortcut.SelectedIndex == 2 ? 0x79 : 0x75,
        // 2026/11/16 新增：保存点击参数时同时保留关闭偏好。
        CloseBehavior = _closeBehavior
    };

    // 2026/11/15 新增：开始前检查全部目标；生命周期结束前禁止重入和退出。
    private async Task ToggleAsync()
    {
        if (_cancellation is not null) { StopRun(); return; }
        // 2026/11/16 修改：关闭或设置对话框期间禁止新启动，停止请求仍优先处理。
        if (_picker is not null || _exitRequested || _closePromptOpen || _preferencesOpen) return;
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
        // 2026/11/15 修改：状态提示使用相同秒单位，避免输入与执行说明混用单位。
        ShowStatus($"运行中 · 启动延时 {settings.StartDelayMilliseconds / 1000m:0.###} s，按 {_hotkeys.StopHotkeyText} 随时停止。");
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
            // 2026/11/16 修改：系统关闭或直接释放后的异步收尾不能访问已销毁的界面和托盘。
            if (!IsDisposed)
            {
                SetRunning(false);
                RefreshCounts();
                if (_closeAfterStop) Close();
            }
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
        // 2026/11/16 新增：托盘菜单实时反映同一执行任务的状态。
        UpdateTray();
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
        // 2026/11/16 新增：紧急停止注册失败时托盘入口也禁止启动。
        UpdateTray();
    }

    private void ShowStatus(string text, bool error = false)
    {
        // 2026/11/16 新增：运行取消的异步回调可能晚于系统关闭，不再更新已释放的控件。
        if (IsDisposed) return;
        _status.Text = text;
        _status.ForeColor = error ? Ui.Danger : Ui.Accent;
        _status.AccessibleDescription = text;
    }

    // 2026/11/15 新增：点选层隐藏主窗口，结束选择后恢复窗口及列表。
    private void CapturePoints()
    {
        // 2026/11/16 修改：关闭及设置对话框期间不创建取点层。
        if (_cancellation is not null || _picker is not null || _exitRequested || _closePromptOpen || _preferencesOpen) return;
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
            // 2026/11/16 修改：退出时关闭取点层不能重新显示主窗口。
            if (_exitRequested || IsDisposed) return;
            RestoreFromTray();
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

    // 2026/11/16 新增：窗口和托盘使用同一套启停入口，托盘显式退出不受记忆偏好影响。
    private void BuildTray()
    {
        _tray.Icon = Icon;
        _tray.Text = Text;
        _tray.ContextMenuStrip = _trayMenu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        _trayMenu.Items.Add("显示主窗口", null, (_, _) => RestoreFromTray());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayToggle.Click += async (_, _) => await ToggleAsync();
        _trayStop.Click += (_, _) => StopRun();
        _trayMenu.Items.Add(_trayToggle);
        _trayMenu.Items.Add(_trayStop);
        _trayMenu.Items.Add("设置", null, (_, _) => ShowPreferences());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出", null, (_, _) => RequestExit());
        _trayMenu.Opening += (_, _) => UpdateTray();
    }

    // 2026/11/16 新增：隐藏窗口不取消点击、不释放热键，保留可恢复的托盘图标。
    private void MinimizeToTray()
    {
        IsInTray = true;
        _tray.Visible = _enableTrayIcon;
        Hide();
        UpdateTray();
    }

    // 2026/11/16 新增：托盘菜单、双击和取点完成使用统一的主窗口恢复入口。
    internal void RestoreFromTray()
    {
        if (_exitRequested || IsDisposed) return;
        IsInTray = false;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    // 2026/11/16 新增：供托盘菜单及测试清理显式退出，运行中的任务仍须先取消并等待完成。
    internal void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    // 2026/11/16 新增：菜单跟随热键可用性、运行和取点状态，隐藏时提示继续运行。
    private void UpdateTray()
    {
        bool running = _cancellation is not null;
        _trayToggle.Text = running ? "停止连点" : "开始连点 · " + _shortcut.Text;
        _trayToggle.Enabled = running || (_hotkeys.CanStop && _picker is null && !_exitRequested && !_closePromptOpen && !_preferencesOpen);
        _trayStop.Enabled = running;
        _tray.Text = Text + (running ? " · 连点中" : " · 已停止");
    }

    // 2026/11/16 新增：设置可随时改回询问，保存失败回滚内存偏好并明确报告。
    private void ShowPreferences()
    {
        if (_preferencesOpen || _closePromptOpen || _exitRequested || _picker is not null) return;
        RestoreFromTray();
        _preferencesOpen = true;
        try
        {
            using var dialog = new PreferencesForm(_closeBehavior);
            if (dialog.ShowDialog(this) != DialogResult.OK || _exitRequested) return;
            var previous = _closeBehavior;
            _closeBehavior = dialog.SelectedCloseBehavior;
            if (!TrySaveSettings()) _closeBehavior = previous;
            else ShowStatus("关闭方式已保存，可随时在设置中修改。");
        }
        finally { _preferencesOpen = false; }
    }

    // 2026/11/16 新增：默认对话框与隐藏测试注入使用相同的选择契约。
    private CloseChoice? PromptForClose()
    {
        if (_promptForClose is not null) return _promptForClose();
        using var dialog = new ClosePromptForm();
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Decision : null;
    }

    // 2026/11/16 修改：仅用户关闭主窗口时询问，最小化保持运行，明确退出先停止输入。
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            if (_closePromptOpen || _preferencesOpen) { e.Cancel = true; return; }
            WindowCloseBehavior action = _closeBehavior;
            WindowCloseBehavior previous = _closeBehavior;
            if (action == WindowCloseBehavior.Ask)
            {
                CloseChoice? choice;
                _closePromptOpen = true;
                try { choice = PromptForClose(); }
                finally { _closePromptOpen = false; }
                if (choice is null) { e.Cancel = true; return; }
                action = choice.Value.Action;
                if (choice.Value.Remember) _closeBehavior = action;
            }
            if (action == WindowCloseBehavior.MinimizeToTray)
            {
                e.Cancel = true;
                if (!TrySaveSettings())
                {
                    _closeBehavior = previous;
                    return;
                }
                MinimizeToTray();
                return;
            }
        }
        // 2026/11/16 新增：关机询问阶段停止输入但不锁死退出状态，用户取消关机后仍可使用。
        if (e.CloseReason == CloseReason.WindowsShutDown)
        {
            bool previousExit = _exitRequested;
            _exitRequested = true;
            try
            {
                StopRun();
                _picker?.Close();
                TrySaveSettings(showWarning: false);
            }
            finally { _exitRequested = previousExit; }
            return;
        }
        _exitRequested = true;
        _picker?.Close();
        if (_cancellation is not null)
        {
            e.Cancel = true;
            _closeAfterStop = true;
            StopRun();
            return;
        }
        TrySaveSettings();
    }

    // 2026/11/16 修改：偏好、托盘和退出统一保存完整草稿，空坐标列表兼容已有保存行为。
    private bool TrySaveSettings(bool showWarning = true)
    {
        try
        {
            var settings = ReadSettings();
            // 2026/11/15 新增：空序列只是未配置的草稿，关闭时仍保留其他设置。
            if (settings.Mode == TargetMode.Sequence && settings.Points.Count == 0) settings = settings with { Mode = TargetMode.Cursor };
            _store.Save(settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            var message = "设置未能保存：" + ex.Message;
            System.Diagnostics.Trace.TraceError(message);
            ShowStatus(message, true);
            if (showWarning) MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    // 2026/11/16 新增：真正释放窗口才移除托盘和热键，直接 Dispose 的隐藏测试亦不会泄漏。
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 2026/11/16 新增：异常/外部释放也须停止输入并移除无主取点层，随后才注销停止键。
            _exitRequested = true;
            _cancellation?.Cancel();
            _picker?.Dispose();
            _tray.Dispose();
            _trayMenu.Dispose();
            _refresh.Dispose();
            _hotkeys.Dispose();
        }
        base.Dispose(disposing);
    }
}
