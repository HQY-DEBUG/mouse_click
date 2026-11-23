// 文件：MainForm.cs
// 描述：中文连点器主窗口、坐标列表及运行生命周期。
// 版本：v1.12
// 日期：2026/11/23
// 历史：
// v1.12 2026/11/23 重排点击参数、位置编辑和运行区，接入蓝紫界面及屏幕位置预览。
// v1.11 2026/11/23 移动选区时保留原生列表的 Shift 连选起点。
// v1.10 2026/11/23 支持批量移动和移除坐标，删除后自动选中相邻行。
using MouseClick.App.Interop;
using MouseClick.Core;

namespace MouseClick.App;

public sealed class MainForm : Form
{
    // 2026/11/23 新增：窗口标题与托盘提示使用相同的产品名和运行状态。
    private const string ProductTitle = "钰宇鼠标连点器";
    private readonly Icon _idleIcon;
    private readonly Icon _runningIcon;
    private bool _restoreAfterRun;
    private readonly ComboBox _button = Ui.Choice("鼠标左键", "鼠标右键", "鼠标中键");
    private readonly ComboBox _kind = Ui.Choice("单击", "双击");
    private readonly ComboBox _mode = Ui.Choice("跟随鼠标当前位置", "固定一个位置", "多个位置顺序循环");
    // 2026/11/23 修改：直接从可保存的虚拟键生成选项和显示名称。
    private readonly ComboBox _shortcut = Ui.Choice(ClickSettings.ToggleVirtualKeys.ToArray()
        .Select(key => ((Keys)key).ToString()).ToArray());
    // 2026/11/15 修改：界面以秒输入，范围和默认实际时长与既有毫秒配置一致。
    private readonly NumericUpDown _interval = Ui.Seconds(0.001m, 86400m, 0.1m, "IntervalInput");
    private readonly NumericUpDown _delay = Ui.Seconds(0m, 3600m, 1m, "DelayInput");
    private readonly NumericUpDown _repeat = Ui.Number(0, 1000000, 0, "RepeatInput");
    private readonly NumericUpDown _x = Ui.Number(int.MinValue, int.MaxValue, 0, "XInput");
    private readonly NumericUpDown _y = Ui.Number(int.MinValue, int.MaxValue, 0, "YInput");
    // 2026/11/23 修改：循环列表与屏幕预览在同一位置切换。
    private readonly PointListView _points = new();
    private readonly PositionPreview _preview = new();
    private readonly Label _headerState = Ui.Text("已停止", 9);
    private readonly Label _status = Ui.Text("准备就绪。");
    private readonly Label _counts = Ui.Text("0 次动作  /  0 轮", 15, true);
    private readonly Label _pointHint = Ui.Text("");
    private readonly Label _modeHint = Ui.Text("");
    // 2026/11/23 新增：坐标标签和多点说明随所选位置模式一起显隐。
    private readonly Label _xLabel = Ui.Text("X");
    private readonly Label _yLabel = Ui.Text("Y");
    // 2026/11/23 修改：实心主按钮用于启动和停止切换，位置编辑按钮沿用文字及快捷键。
    private readonly ActionButton _start = Ui.Button("开始连点 · F6", true, UiIcon.Start);
    private readonly Button _stop = Ui.Button("停止 · Ctrl+F8", icon: UiIcon.Stop);
    private readonly Button _capture = Ui.Button("屏幕点选 · F7", icon: UiIcon.Pick);
    private readonly Button _add = Ui.Button("添加坐标", icon: UiIcon.Add);
    private readonly Button _remove = Ui.Button("移除", icon: UiIcon.Remove);
    private readonly Button _up = Ui.Button("上移", icon: UiIcon.Up);
    private readonly Button _down = Ui.Button("下移", icon: UiIcon.Down);
    private readonly Panel _settingsPanel = Ui.Card();
    private readonly Panel _positionsPanel = Ui.Card();
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
    private readonly Button _preferences = Ui.Button("设置", icon: UiIcon.Settings);
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
        // 2026/11/23 修改：每个窗体只加载一次两种图标，在托盘图标和原生窗口都释放后处置副本。
        _idleIcon = LoadIcon("MouseClick.AppIcon.ico");
        _runningIcon = LoadIcon("MouseClick.RunningIcon.ico");
        Disposed += (_, _) => { _idleIcon.Dispose(); _runningIcon.Dispose(); };
        Name = "MouseClickMainForm";
        StartPosition = FormStartPosition.CenterScreen;
        // 2026/11/23 修改：减少窗口默认高度，紧凑尺寸仍保留完整设置和坐标操作行。
        ClientSize = new Size(976, 664);
        MinimumSize = new Size(870, 672);
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

    // 2026/11/23 新增：从程序集读取图标并复制，使其使用期限独立于资源流。
    private static Icon LoadIcon(string name)
    {
        using Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("程序缺少图标资源：" + name);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    // 2026/11/23 修改：顶部显示应用与运行状态，中部两栏编辑，底部保留统计、消息和启停入口。
    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(18) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 91));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        Controls.Add(root);
        var header = new WorkspaceHeader(_idleIcon) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent,
            Padding = new Padding(80, 9, 17, 9), Margin = Padding.Empty
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        var title = Ui.Text(ProductTitle, 19, true);
        title.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.White;
        title.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        title.Margin = Padding.Empty;
        title.BackColor = _headerState.BackColor = Color.Transparent;
        _headerState.Name = "HeaderRunState";
        _headerState.Anchor = AnchorStyles.Left;
        _headerState.Margin = Padding.Empty;
        brand.Controls.Add(title, 0, 0);
        brand.Controls.Add(_headerState, 0, 1);
        _preferences.Name = "PreferencesButton";
        _preferences.Anchor = AnchorStyles.Right;
        _preferences.Margin = Padding.Empty;
        brand.Controls.Add(_preferences, 1, 0);
        brand.SetRowSpan(_preferences, 2);
        header.Controls.Add(brand);
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);
        _settingsPanel.Dock = _positionsPanel.Dock = DockStyle.Fill;
        _settingsPanel.Padding = _positionsPanel.Padding = new Padding(17, 14, 17, 13);
        _settingsPanel.Margin = new Padding(0, 0, 12, 0);
        _settingsPanel.AutoScroll = true;
        _positionsPanel.Margin = Padding.Empty;
        body.Controls.Add(_settingsPanel, 0, 0);
        body.Controls.Add(_positionsPanel, 1, 0);
        BuildSettings();
        BuildPositions();

        var console = Ui.Card();
        console.Dock = DockStyle.Fill;
        console.Margin = new Padding(0, 12, 0, 0);
        console.Padding = new Padding(17, 9, 17, 8);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _counts.Name = "RunCounts";
        _counts.Anchor = AnchorStyles.Left;
        _counts.Margin = Padding.Empty;
        footer.Controls.Add(_counts, 0, 0);
        _start.Name = "StartButton";
        _stop.Name = "StopButton";
        _stop.ForeColor = Ui.Danger;
        _stop.Enabled = false;
        var commands = Ui.Row(_stop, _start);
        commands.Dock = DockStyle.None;
        commands.Anchor = AnchorStyles.Right;
        commands.WrapContents = false;
        _start.Margin = Padding.Empty;
        footer.Controls.Add(commands, 1, 0);
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.Margin = new Padding(0, 5, 0, 0);
        _status.Name = "RunStatus";
        footer.Controls.Add(_status, 0, 1);
        footer.SetColumnSpan(_status, 2);
        console.Controls.Add(footer);
        root.Controls.Add(console, 0, 2);
        // 2026/11/23 新增：热键冲突可能有多行说明，按当前宽度给运行区留足阅读空间。
        void FitStatus(object? sender, EventArgs args)
        {
            if (footer.ClientSize.Width <= 0) return;
            int messageHeight = TextRenderer.MeasureText(_status.Text, _status.Font,
                new Size(footer.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak).Height;
            int required = console.Margin.Vertical + console.Padding.Vertical + commands.PreferredSize.Height
                + _status.Margin.Vertical + messageHeight + LogicalToDeviceUnits(3);
            int height = Math.Max(LogicalToDeviceUnits(98), required);
            if ((int)root.RowStyles[2].Height != height) root.RowStyles[2].Height = height;
        }
        _status.TextChanged += FitStatus;
        footer.SizeChanged += FitStatus;
    }

    // 2026/11/23 修改：将按键与方式、间隔与延时成对排列，轮数与快捷键保留独立的整行输入。
    private void BuildSettings()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 10, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int row = 0; row < table.RowCount; row++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = Ui.Text("点击参数", 14, true);
        heading.Margin = new Padding(0, 0, 0, 15);
        table.Controls.Add(heading, 0, 0);
        table.SetColumnSpan(heading, 2);
        void Field(string caption, Control control, string name, int column, int row, bool wide = false)
        {
            var label = Ui.Text(caption, 9);
            label.ForeColor = Ui.Muted;
            label.Margin = new Padding(0, 0, 0, 5);
            table.Controls.Add(label, column, row);
            control.Name = name;
            control.AccessibleName = caption;
            control.Margin = new Padding(0, 0, !wide && column == 0 ? 10 : 0, 13);
            table.Controls.Add(control, column, row + 1);
            if (wide) { table.SetColumnSpan(label, 2); table.SetColumnSpan(control, 2); }
        }
        Field("鼠标按键", _button, "ButtonChoice", 0, 1);
        Field("点击方式", _kind, "KindChoice", 1, 1);
        Field("点击间隔（s）", _interval, "IntervalInput", 0, 3);
        Field("启动延时（s）", _delay, "DelayInput", 1, 3);
        Field("重复轮数（0 = 持续）", _repeat, "RepeatInput", 0, 5, true);
        Field("全局开始 / 停止快捷键", _shortcut, "ShortcutChoice", 0, 7, true);
        var emergency = Ui.Text("Ctrl+F8  紧急停止", 9, true);
        emergency.ForeColor = Ui.Accent;
        emergency.BackColor = Ui.AccentSoft;
        emergency.Margin = new Padding(0, 2, 0, 0);
        emergency.Padding = new Padding(9, 5, 9, 5);
        table.Controls.Add(emergency, 0, 9);
        table.SetColumnSpan(emergency, 2);
        _settingsPanel.Controls.Add(table);
    }

    // 2026/11/23 修改：屏幕预览与坐标列表共享主显示区，模式选择和对应的编辑工具保留在上方。
    private void BuildPositions()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < table.RowCount; i++) table.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        var heading = Ui.Text("点击位置", 14, true);
        heading.Margin = new Padding(0, 0, 0, 9);
        table.Controls.Add(heading, 0, 0);
        _mode.Name = "ModeChoice";
        _mode.AccessibleName = "点击位置模式";
        table.Controls.Add(_mode, 0, 1);
        _modeHint.Font = new Font(Font.FontFamily, 9);
        _modeHint.ForeColor = Ui.Muted;
        _modeHint.AutoSize = false;
        _modeHint.Height = 24;
        _modeHint.Margin = new Padding(0, 4, 0, 5);
        _modeHint.Dock = DockStyle.Top;
        table.Controls.Add(_modeHint, 0, 2);
        _x.Dock = _y.Dock = DockStyle.None;
        _x.Width = _y.Width = 96;
        _x.AccessibleName = "X 坐标";
        _y.AccessibleName = "Y 坐标";
        _xLabel.Anchor = _yLabel.Anchor = _x.Anchor = _y.Anchor = AnchorStyles.Left;
        _xLabel.Margin = _yLabel.Margin = Padding.Empty;
        _x.Margin = _y.Margin = new Padding(4, 0, 8, 0);
        _add.Name = "AddPointButton";
        _add.Margin = new Padding(0, 0, 8, 6);
        _capture.ForeColor = Ui.Accent;
        _capture.Name = "CaptureButton";
        // 2026/11/23 新增：为列表操作提供稳定的控件名称，供界面验收定位按钮。
        _up.Name = "MovePointUpButton";
        _down.Name = "MovePointDownButton";
        _remove.Name = "RemovePointButton";
        var actions = Ui.Row(_xLabel, _x, _yLabel, _y, _add, _capture, _up, _down, _remove);
        actions.SetFlowBreak(_add, true);
        table.Controls.Add(actions, 0, 3);
        var display = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        table.Controls.Add(display, 0, 4);
        _points.Name = "PointList";
        _points.AccessibleName = "循环坐标列表";
        _points.Dock = DockStyle.Fill;
        _preview.Name = "ScreenPositionPreview";
        _preview.Dock = DockStyle.Fill;
        display.Controls.Add(_preview);
        display.Controls.Add(_points);
        _pointHint.ForeColor = Ui.Muted;
        _pointHint.Font = new Font(Font.FontFamily, 9);
        _pointHint.Margin = new Padding(0, 7, 0, 0);
        table.Controls.Add(_pointHint, 0, 5);
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
        // 2026/11/23 修改：三个操作按钮统一作用于当前选中的所有坐标。
        _remove.Click += (_, _) => RemoveSelectedPoints();
        _up.Click += (_, _) => MoveSelectedPoints(-1);
        _down.Click += (_, _) => MoveSelectedPoints(1);
        _mode.SelectedIndexChanged += (_, _) => UpdateMode();
        // 2026/11/23 新增：只在固定坐标或模式变更时刷新屏幕示意图。
        _x.ValueChanged += (_, _) => UpdatePositionPreview();
        _y.ValueChanged += (_, _) => UpdatePositionPreview();
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
        // 2026/11/23 修改：用保存的虚拟键定位选项，既有 F9/F10 不受新增 F8 的位置影响。
        _shortcut.SelectedIndex = ClickSettings.ToggleVirtualKeys.IndexOf(settings.ToggleHotkey);
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
        // 2026/11/23 修改：界面与注册、保存均使用相同的虚拟键值。
        ToggleHotkey = ClickSettings.ToggleVirtualKeys[_shortcut.SelectedIndex],
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
        // 2026/11/23 新增：正常结束或用户停止时恢复窗口；系统关机可单独禁止本轮恢复。
        _restoreAfterRun = true;
        SetRunning(true);
        // 2026/11/15 修改：状态提示使用相同秒单位，避免输入与执行说明混用单位。
        ShowStatus($"运行中 · 启动延时 {settings.StartDelayMilliseconds / 1000m:0.###} s，按 {_hotkeys.StopHotkeyText} 随时停止。");
        try
        {
            // 2026/11/16 新增：按钮、托盘菜单和全局快捷键共用此入口，首个鼠标动作前移开主窗口。
            MinimizeToTray();
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
                // 2026/11/23 新增：仅在引擎结束后显示结果；正在显示的模态窗口继续由其所有者管理。
                else if (_restoreAfterRun && !_closePromptOpen && !_preferencesOpen) RestoreFromTray();
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
        // 2026/11/23 新增：按钮图形与当前开始或停止动作同步。
        _start.Icon = running ? UiIcon.Stop : UiIcon.Start;
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
        // 2026/11/23 修改：就绪时保持消息简短，注册冲突仍完整显示。
        ShowStatus(error ?? "准备就绪。", error is not null);
        // 2026/11/16 新增：紧急停止注册失败时托盘入口也禁止启动。
        UpdateTray();
    }

    private void ShowStatus(string text, bool error = false)
    {
        // 2026/11/16 新增：运行取消的异步回调可能晚于系统关闭，不再更新已释放的控件。
        if (IsDisposed) return;
        _status.Text = text;
        _status.ForeColor = error ? Ui.Danger : Ui.Muted;
        _status.AccessibleDescription = text;
        // 2026/11/16 新增：托盘中启动或运行失败时显示原因，避免快捷键已响应却看起来毫无反应。
        // 2026/11/23 修改：启动前的错误立即显示，运行期间的错误在清理结束、计数刷新后显示。
        if (error && IsInTray && _cancellation is null) RestoreFromTray();
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

    // 2026/11/23 新增：从后向前移除所选行，第一处空位的后继行接收选择，删到末尾时选前一行。
    private void RemoveSelectedPoints()
    {
        int[] indices = _points.SelectedIndices.Cast<int>().Order().ToArray();
        if (indices.Length == 0) return;

        _points.BeginUpdate();
        try
        {
            for (int i = indices.Length - 1; i >= 0; i--) _points.Items.RemoveAt(indices[i]);
            RenumberPoints();
        }
        finally { _points.EndUpdate(); }

        _points.Focus();
        if (_points.Items.Count == 0) return;
        var next = _points.Items[Math.Min(indices[0], _points.Items.Count - 1)];
        next.Selected = next.Focused = true;
        next.EnsureVisible();
    }

    // 2026/11/23 修改：把相邻未选行挪到选区另一端，让原生控件保留选中行、焦点与 Shift 连选起点。
    private void MoveSelectedPoints(int direction)
    {
        if (_points.SelectedItems.Count == 0) return;
        var focus = _points.FocusedItem is { Selected: true } current ? current : _points.SelectedItems[0];
        int count = _points.Items.Count;

        _points.BeginUpdate();
        try
        {
            // 上移从第二行向下扫描，下移从倒数第二行向上扫描。
            int row = direction < 0 ? 1 : count - 2;
            while (row >= 0 && row < count)
            {
                var neighbor = _points.Items[row + direction];
                if (_points.Items[row].Selected && !neighbor.Selected)
                {
                    int end = row - direction;
                    while (end >= 0 && end < count && _points.Items[end].Selected) end -= direction;
                    row = end + direction;
                    _points.Items.Remove(neighbor);
                    _points.Items.Insert(row, neighbor);
                }
                row -= direction;
            }
            RenumberPoints();
        }
        finally { _points.EndUpdate(); }

        _points.Focus();
        focus.Focused = true;
        focus.EnsureVisible();
    }

    private void RenumberPoints()
    {
        for (var i = 0; i < _points.Items.Count; i++) _points.Items[i].Text = (i + 1).ToString();
        // 2026/11/23 修改：在列表下方提示连续和不连续选区的操作方式。
        _pointHint.Text = $"共 {_points.Items.Count} 个位置  ·  Ctrl 逐个多选，Shift 连续多选";
    }

    // 2026/11/23 修改：固定坐标旁直接显示选点按钮，只有循环模式展示添加、排序、列表与数量。
    private void UpdateMode()
    {
        var mode = (TargetMode)_mode.SelectedIndex;
        bool sequence = mode == TargetMode.Sequence;
        _xLabel.Visible = _x.Visible = _yLabel.Visible = _y.Visible = mode != TargetMode.Cursor;
        _add.Visible = _up.Visible = _down.Visible = _remove.Visible = sequence;
        _points.Visible = _pointHint.Visible = sequence;
        _preview.Visible = !sequence;
        UpdatePositionPreview();
        _capture.Text = mode == TargetMode.Fixed ? "选点 · F7" : "屏幕点选 · F7";
        _modeHint.Text = mode switch
        {
            TargetMode.Cursor => "每次点击鼠标当前所在的位置。",
            TargetMode.Fixed => "输入屏幕坐标，或按 F7 选取固定位置。",
            _ => "从上到下点击为一轮。屏幕点选时，右键或 Esc 结束。"
        };
        RenumberPoints();
    }

    // 2026/11/23 新增：坐标来自原有输入框，预览仅呈现当前设置。
    private void UpdatePositionPreview() =>
        _preview.SetTarget((TargetMode)_mode.SelectedIndex, new ClickPoint((int)_x.Value, (int)_y.Value));

    // 2026/11/16 新增：窗口和托盘使用同一套启停入口，托盘显式退出不受记忆偏好影响。
    private void BuildTray()
    {
        // 2026/11/23 修改：在首次显示托盘图标或窗口前初始化已停止状态。
        UpdateTray();
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
        // 2026/11/23 修改：通知区及任务栏都区分绿底播放图标和蓝紫指针，标题与悬停文字同步。
        Icon stateIcon = running ? _runningIcon : _idleIcon;
        if (Icon != stateIcon) Icon = stateIcon;
        if (_tray.Icon != stateIcon) _tray.Icon = stateIcon;
        Text = _tray.Text = ProductTitle + (running ? " · 连点中" : " · 已停止");
        // 2026/11/23 新增：标题栏中的文字状态与托盘状态同时更新。
        _headerState.Text = running ? "●   正在连点" : "●   已停止";
        _headerState.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText :
            running ? Color.FromArgb(144, 235, 199) : Color.FromArgb(204, 207, 238);
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
            // 2026/11/23 新增：关机查询返回后才收到取消完成通知，也应保持当前窗口状态。
            _restoreAfterRun = false;
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
