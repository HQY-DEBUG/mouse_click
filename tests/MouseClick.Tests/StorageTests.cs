// 文件：StorageTests.cs
// 说明：用独立临时文件验证配置加载、保存、发布模式隔离与损坏数据保留。
// 版本：v1.1
// 日期：2026/11/15
// 历史：
// v1.1 2026/11/15 验证安装版与便携版配置隔离和显式路径优先。
// v1.0 2026/11/15 新增设置持久化行为测试。

using System.Text;
using MouseClick.Core;

namespace MouseClick.Tests;

// 2026/11/15 新增：使用真实文件验证持久化边界，临时目录保留为失败排查证据。
internal static class StorageTests
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        // 2026/11/15 新增：通过独立目录验证发布模式选择，不读写真实用户配置。
        ("无便携标记沿用安装版配置路径", InstalledModeKeepsExistingPath),
        ("便携模式独立保存且不读取或覆盖安装版配置", PortableModeIsolatesSettings),
        ("显式配置路径不受便携标记影响", ExplicitPathOverridesPortableMode),
        ("便携目录不可写时报告错误且不回退安装版", PortableWriteFailureDoesNotFallback),
        ("缺失配置使用可执行默认设置且不创建文件", MissingFileLoadsDefaults),
        ("所有设置和多坐标原样保存回读", SaveAndLoadAllSettings),
        ("非法保存不覆盖已有配置", InvalidSavePreservesExistingFile),
        ("损坏 JSON 读取报错并保留原始文件", BadJsonLoadPreservesFile),
        ("JSON 无效设置读取报错并保留原始文件", InvalidSettingsLoadPreservesFile),
        ("保存前为损坏原文件创建完整备份", SaveBacksUpDamagedFile),
        ("配置路径为空时明确拒绝", RejectEmptyPath)
    ];

    // 2026/11/15 新增：无标记时必须继续读取原目录，避免升级后丢失既有设置。
    private static Task InstalledModeKeepsExistingPath()
    {
        string root = Path.GetDirectoryName(NewPath())!;
        string applicationDirectory = Path.Combine(root, "app");
        string localApplicationData = Path.Combine(root, "local-app-data");
        string installedPath = Path.Combine(localApplicationData, "HQY.MouseClick", "settings.json");
        new SettingsStore(installedPath).Save(new ClickSettings { RepeatCount = 21 });

        string selectedPath = SettingsStore.GetDefaultPath(applicationDirectory, localApplicationData);
        Check(selectedPath == installedPath, "无标记应沿用 LocalApplicationData 下的 HQY.MouseClick 目录");
        Check(new SettingsStore(selectedPath).Load().RepeatCount == 21, "安装模式应读取已有配置");
        Check(!Directory.Exists(applicationDirectory), "选择配置路径不应在程序目录创建文件");
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：便携首次运行必须使用默认值，保存仅落入程序旁 data 目录。
    private static Task PortableModeIsolatesSettings()
    {
        string root = Path.GetDirectoryName(NewPath())!;
        string applicationDirectory = Path.Combine(root, "app");
        string localApplicationData = Path.Combine(root, "local-app-data");
        string installedPath = Path.Combine(localApplicationData, "HQY.MouseClick", "settings.json");
        new SettingsStore(installedPath).Save(new ClickSettings { RepeatCount = 37 });
        byte[] installedBytes = File.ReadAllBytes(installedPath);
        Directory.CreateDirectory(applicationDirectory);
        File.WriteAllText(Path.Combine(applicationDirectory, "portable.flag"), "");

        string selectedPath = SettingsStore.GetDefaultPath(applicationDirectory, localApplicationData);
        Check(selectedPath == Path.Combine(applicationDirectory, "data", "settings.json"), "便携配置应位于程序旁 data 目录");
        var store = new SettingsStore(selectedPath);
        Check(store.Load().RepeatCount == 0, "便携首次运行不得读取或迁移安装版配置");
        Check(!File.Exists(selectedPath), "便携读取默认值不得提前创建配置");
        store.Save(new ClickSettings { RepeatCount = 9 });
        Check(store.Load().RepeatCount == 9, "便携配置应能独立保存和重新加载");
        Check(installedBytes.SequenceEqual(File.ReadAllBytes(installedPath)), "便携保存不得改写安装版配置");
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：明确指定的隔离路径仍由调用方控制，不受发布标记重定向。
    private static Task ExplicitPathOverridesPortableMode()
    {
        string root = Path.GetDirectoryName(NewPath())!;
        string applicationDirectory = Path.Combine(root, "app");
        string explicitPath = Path.Combine(root, "custom", "settings.json");
        Directory.CreateDirectory(applicationDirectory);
        File.WriteAllText(Path.Combine(applicationDirectory, "portable.flag"), "");
        string portablePath = SettingsStore.GetDefaultPath(applicationDirectory, Path.Combine(root, "local-app-data"));
        new SettingsStore(portablePath).Save(new ClickSettings { RepeatCount = 11 });
        byte[] portableBytes = File.ReadAllBytes(portablePath);

        var store = new SettingsStore(explicitPath);
        Check(store.Load().RepeatCount == 0, "显式路径不得读取便携默认配置");
        store.Save(new ClickSettings { RepeatCount = 15 });
        Check(new SettingsStore(explicitPath).Load().RepeatCount == 15, "显式路径应保存到指定文件");
        Check(portableBytes.SequenceEqual(File.ReadAllBytes(portablePath)), "显式路径保存不得改写便携默认配置");
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：用同名普通文件阻止创建 data 目录，验证错误传播及无隐式回退。
    private static Task PortableWriteFailureDoesNotFallback()
    {
        string root = Path.GetDirectoryName(NewPath())!;
        string applicationDirectory = Path.Combine(root, "app");
        string localApplicationData = Path.Combine(root, "local-app-data");
        string installedPath = Path.Combine(localApplicationData, "HQY.MouseClick", "settings.json");
        new SettingsStore(installedPath).Save(new ClickSettings { RepeatCount = 23 });
        byte[] installedBytes = File.ReadAllBytes(installedPath);
        Directory.CreateDirectory(applicationDirectory);
        File.WriteAllText(Path.Combine(applicationDirectory, "portable.flag"), "");
        File.WriteAllText(Path.Combine(applicationDirectory, "data"), "阻止创建同名目录");

        var store = new SettingsStore(SettingsStore.GetDefaultPath(applicationDirectory, localApplicationData));
        Throws<IOException>(() => store.Save(new ClickSettings { RepeatCount = 31 }));
        Check(installedBytes.SequenceEqual(File.ReadAllBytes(installedPath)), "便携保存失败不得回退并覆盖安装版配置");
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：默认配置来源于缺文件分支，不能掩盖现有文件读取错误。
    private static Task MissingFileLoadsDefaults()
    {
        string path = NewPath();
        ClickSettings settings = new SettingsStore(path).Load();
        settings.Validate();
        Check(settings.Mode == TargetMode.Cursor && settings.RepeatCount == 0 && settings.IntervalMilliseconds == 100,
            "缺文件应给出持续跟随鼠标的默认设置");
        Check(!File.Exists(path), "读取默认设置不应生成配置文件");
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：捕获字段丢失、坐标顺序改变或正常保存遗留半写入临时文件的问题。
    private static Task SaveAndLoadAllSettings()
    {
        string path = NewPath();
        var store = new SettingsStore(path);
        var expected = new ClickSettings
        {
            Button = MouseButton.Right,
            Kind = ClickKind.Double,
            IntervalMilliseconds = 123,
            StartDelayMilliseconds = 456,
            RepeatCount = 7,
            Mode = TargetMode.Sequence,
            FixedPoint = new(-9, 8),
            Points = [new(-100, 200), new(300, -400)],
            ToggleHotkey = 0x79
        };
        store.Save(expected);
        ClickSettings actual = store.Load();
        Check(actual.Button == MouseButton.Right && actual.Kind == ClickKind.Double, "按键与双击应保留");
        Check(actual.IntervalMilliseconds == 123 && actual.StartDelayMilliseconds == 456 && actual.RepeatCount == 7, "运行时序应保留");
        Check(actual.Mode == TargetMode.Sequence && actual.FixedPoint == new ClickPoint(-9, 8) && actual.ToggleHotkey == 0x79, "模式、固定坐标和快捷键应保留");
        Check(actual.Points.SequenceEqual(new[] { new ClickPoint(-100, 200), new ClickPoint(300, -400) }), "坐标及顺序应保留");
        store.Save(expected with { RepeatCount = 8 });
        Check(store.Load().RepeatCount == 8, "第二次保存应替换已有配置");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "正常原子保存不应遗留临时文件或损坏备份");
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：非法输入必须先被拒绝，不能在失败前截断有效配置。
    private static Task InvalidSavePreservesExistingFile()
    {
        string path = NewPath();
        var store = new SettingsStore(path);
        store.Save(new ClickSettings());
        byte[] before = File.ReadAllBytes(path);
        Throws<ArgumentException>(() => store.Save(new ClickSettings { IntervalMilliseconds = 0 }));
        Throws<ArgumentNullException>(() => store.Save(null!));
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "非法保存应保留原始字节");
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：损坏 JSON 与 JSON null 必须报错，不能悄悄转换成默认配置。
    private static Task BadJsonLoadPreservesFile()
    {
        foreach (string content in new[] { "{not-json", "null", "{\"IntervalMilliseconds\":\"fast\"}" })
        {
            string path = WriteFixture(content);
            Throws<InvalidDataException>(() => new SettingsStore(path).Load());
            Check(File.ReadAllText(path) == content, "读取损坏配置应保留内容");
        }
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：语法有效但超范围、非法枚举、空坐标或空序列的配置同样必须报错。
    private static Task InvalidSettingsLoadPreservesFile()
    {
        string[] fixtures =
        [
            "{\"IntervalMilliseconds\":0}", "{\"Button\":99}",
            "{\"FixedPoint\":null}", "{\"Points\":null}",
            "{\"Points\":[null]}", "{\"Mode\":2,\"Points\":[]}"
        ];
        foreach (string content in fixtures)
        {
            string path = WriteFixture(content);
            Throws<InvalidDataException>(() => new SettingsStore(path).Load());
            Check(File.ReadAllText(path) == content, "无效配置内容应保留");
        }
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：用户重新保存时先保全损坏原文，防止读取报错后静默覆盖损坏证据。
    private static Task SaveBacksUpDamagedFile()
    {
        foreach (string content in new[] { "{bad-json", "{\"IntervalMilliseconds\":0}" })
        {
            string path = WriteFixture(content);
            byte[] original = File.ReadAllBytes(path);
            var store = new SettingsStore(path);
            store.Save(new ClickSettings { RepeatCount = 12 });
            Check(store.Load().RepeatCount == 12, "损坏配置备份后应成功保存新设置");
            string[] backups = Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.invalid-*.bak");
            Check(backups.Length == 1, "应保留一份唯一损坏备份");
            Check(original.SequenceEqual(File.ReadAllBytes(backups[0])), "备份必须保留原文件完整字节");
        }
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：空路径应在构造边界拒绝，避免落入工作目录或掩盖配置错误。
    private static Task RejectEmptyPath()
    {
        Throws<ArgumentException>(() => new SettingsStore(""));
        Throws<ArgumentException>(() => new SettingsStore(" "));
        Throws<ArgumentNullException>(() => new SettingsStore(null!));
        return Task.CompletedTask;
    }

    // 2026/11/15 新增：每个场景使用唯一临时目录，与真实用户设置完全隔离。
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "MouseClick.Tests", Guid.NewGuid().ToString("N"), "settings.json");

    private static string WriteFixture(string content)
    {
        string path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"应抛出 {typeof(TException).Name}");
    }
}
