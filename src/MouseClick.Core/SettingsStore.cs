// 文件：SettingsStore.cs
// 说明：选择发布模式配置路径，读取、校验并原子保存点击设置，保留损坏配置备份。
// 版本：v1.1
// 日期：2026/11/15
// 历史：
// v1.1 2026/11/15 按便携标记隔离配置目录并保留显式路径行为。
// v1.0 2026/11/15 新增 JSON 设置持久化。

using System.Text.Json;

namespace MouseClick.Core;

// 2026/11/15 新增：将配置文件错误保留为可向界面明确报告的异常。
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;

    // 2026/11/15 新增：固定配置绝对路径，避免运行期间工作目录变化导致读写错位。
    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    // 2026/11/15 新增：为 MainForm 正常启动选择默认路径；显式路径构造调用方不受影响。
    public static string GetDefaultPath(string applicationDirectory, string localApplicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
        return File.Exists(Path.Combine(applicationDirectory, "portable.flag"))
            ? Path.Combine(applicationDirectory, "data", "settings.json")
            : Path.Combine(localApplicationDataDirectory, "HQY.MouseClick", "settings.json");
    }

    // 2026/11/15 新增：只有文件或目录不存在时使用默认配置，其余读取与校验错误向上报告。
    public ClickSettings Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return new ClickSettings();
        }
        catch (DirectoryNotFoundException)
        {
            return new ClickSettings();
        }

        try
        {
            ClickSettings settings = JsonSerializer.Deserialize<ClickSettings>(json, JsonOptions)
                ?? throw new InvalidDataException($"配置文件为空对象，原文件已保留：{path}");
            settings.Validate();
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"配置文件 JSON 损坏，原文件已保留：{path}", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"配置文件包含无效设置，原文件已保留：{path}。{exception.Message}", exception);
        }
    }

    // 2026/11/15 新增：保存前校验快照；损坏旧文件先备份，完整写入同目录临时文件后原子替换。
    public void Save(ClickSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var snapshot = settings with { Points = settings.Points?.ToArray()! };
        snapshot.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            _ = Load();
        }
        catch (InvalidDataException)
        {
            string backupPath = $"{path}.invalid-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.bak";
            File.Copy(path, backupPath, overwrite: false);
        }

        // 2026/11/15 新增：先刷盘再替换；保存失败保留旧文件及临时文件，避免丢失诊断信息。
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                   bufferSize: 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, snapshot, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }
}
