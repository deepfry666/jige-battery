using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jige.Core;

namespace Jige.Tray;

[JsonConverter(typeof(JsonStringEnumConverter<TrayDisplayMode>))]
internal enum TrayDisplayMode
{
    Custom,
    LowestBattery,
    [JsonStringEnumMemberName("PerDevice")]
    LegacyPerDevice
}

[JsonConverter(typeof(JsonStringEnumConverter<TrayDisplayLocation>))]
internal enum TrayDisplayLocation { Taskbar, TrayIcons }

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
internal enum ThemeMode { Auto, Light, Dark }

[JsonConverter(typeof(JsonStringEnumConverter<TaskbarStripStyle>))]
internal enum TaskbarStripStyle { Auto, Frameless, Capsule, Badges, Meter }

[JsonConverter(typeof(JsonStringEnumConverter<TaskbarStripColorScheme>))]
internal enum TaskbarStripColorScheme { Auto, Light, Dark, HighContrast, Monochrome, IceBlue }

internal sealed record TraySettings
{
    public static IReadOnlyList<int> PollIntervals { get; } = Array.AsReadOnly<int>([30, 60, 120]);
    /// <summary>单台设备可选的提醒阈值；比全局多一个 5%，适合掉电很慢的键盘。</summary>
    public static IReadOnlyList<int> DeviceThresholdOptions { get; } = Array.AsReadOnly<int>([5, 10, 15, 20, 30]);

    public TrayDisplayLocation DisplayLocation { get; init; } = TrayDisplayLocation.Taskbar;
    public TaskbarStripStyle TaskbarStyle { get; init; } = TaskbarStripStyle.Auto;
    public TaskbarStripColorScheme TaskbarColor { get; init; } = TaskbarStripColorScheme.Auto;
    public ThemeMode Theme { get; init; } = ThemeMode.Auto;
    public TrayDisplayMode TrayMode { get; init; } = TrayDisplayMode.Custom;
    public int LowBatteryThreshold { get; init; } = 15;
    public int PollSeconds { get; init; } = 60;
    public int GamePollSeconds { get; init; } = 300;
    public bool GameSilent { get; init; } = true;
    public bool ShowBluetoothDevices { get; init; } = true;
    public bool StartWithWindows { get; init; } = true;
    /// <summary>
    /// 后台每天检查一次正式版并预先下载、校验；安装仍需用户点击。
    /// 用可空值保存：源生成反序列化不会保留 init 属性的初始值，旧配置缺少此项时为 null，按开启处理。
    /// </summary>
    public bool? AutoCheckUpdates { get; init; }

    [JsonIgnore]
    public bool AutoUpdateEnabled => AutoCheckUpdates != false;
    public bool? WelcomeShown { get; init; }
    /// <summary>显示预计可用时间（估算）。默认关闭；关闭时后台仍继续学习，打开后立即有数。</summary>
    public bool ShowRuntimeEstimate { get; init; }
    /// <summary>充满时提醒拔线，默认关闭。</summary>
    public bool FullChargeReminder { get; init; }
    /// <summary>进入游戏时若有设备电量偏低，弹一条不抢焦点的小横幅。默认开启（可空值兼容旧配置）。</summary>
    public bool? GameEntryReminder { get; init; }

    [JsonIgnore]
    public bool GameEntryReminderEnabled => GameEntryReminder != false;
    /// <summary>单台设备的低电量提醒阈值；未设置的设备跟随 <see cref="LowBatteryThreshold"/>。</summary>
    public Dictionary<string, int> DeviceThresholds { get; init; } = [];
    public string[] GameProcessNames { get; init; } = [];
    public Dictionary<string, string> DeviceAliases { get; init; } = [];
    public string[] HiddenDeviceIds { get; init; } = [];
    public string[] TrayExcludedDeviceIds { get; init; } = [];

    public int ThresholdFor(string id) =>
        DeviceThresholds.TryGetValue(id, out int value) ? value : LowBatteryThreshold;

    /// <summary>设备页「提醒」标签的点击循环：默认 → 5% → 10% → 15% → 20% → 30% → 默认。</summary>
    public TraySettings CycleDeviceThreshold(string id)
    {
        var thresholds = new Dictionary<string, int>(DeviceThresholds, StringComparer.OrdinalIgnoreCase);
        var options = DeviceThresholdOptions;
        if (!thresholds.TryGetValue(id, out int current))
            thresholds[id] = options[0];
        else
        {
            int index = -1;
            for (int i = 0; i < options.Count; i++)
                if (options[i] == current)
                    index = i;
            if (index < 0 || index == options.Count - 1)
                thresholds.Remove(id);
            else
                thresholds[id] = options[index + 1];
        }
        return this with { DeviceThresholds = thresholds };
    }

    public bool IsInTray(string id) =>
        !TrayExcludedDeviceIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    public TraySettings ToggleTrayDevice(string id)
    {
        string[] excluded = IsInTray(id)
            ? [.. TrayExcludedDeviceIds, id]
            : TrayExcludedDeviceIds.Where(item => !StringComparer.OrdinalIgnoreCase.Equals(item, id)).ToArray();
        return this with { TrayExcludedDeviceIds = excluded };
    }

    public TraySettings WithAlias(string id, string name)
    {
        string alias = CleanAlias(name);
        bool hasAlias = DeviceAliases.TryGetValue(id, out var current);
        if (hasAlias ? current == alias : alias.Length == 0)
            return this;
        var aliases = new Dictionary<string, string>(DeviceAliases, StringComparer.OrdinalIgnoreCase);
        if (alias.Length == 0)
            aliases.Remove(id);
        else
            aliases[id] = alias;
        return this with { DeviceAliases = aliases };
    }

    public TraySettings WithoutDevice(string id)
    {
        var settings = WithAlias(id, "");
        return settings with
        {
            DeviceThresholds = DeviceThresholds.Where(pair => !StringComparer.OrdinalIgnoreCase.Equals(pair.Key, id))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            HiddenDeviceIds = HiddenDeviceIds.Where(hidden => !StringComparer.OrdinalIgnoreCase.Equals(hidden, id)).ToArray(),
            TrayExcludedDeviceIds = TrayExcludedDeviceIds.Where(excluded => !StringComparer.OrdinalIgnoreCase.Equals(excluded, id)).ToArray()
        };
    }

    private static string CleanAlias(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public TraySettings Normalize()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in DeviceAliases ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                aliases[id] = CleanAlias(name);
        }

        // Equidistant values choose the shorter interval. Use long for older out-of-range values.
        int normal = PollIntervals.MinBy(seconds => Math.Abs((long)seconds - PollSeconds));
        var mode = TrayMode == TrayDisplayMode.LowestBattery ? TrayDisplayMode.LowestBattery : TrayDisplayMode.Custom;
        return this with
        {
            TrayMode = mode,
            DisplayLocation = DisplayLocation == TrayDisplayLocation.TrayIcons
                ? TrayDisplayLocation.TrayIcons : TrayDisplayLocation.Taskbar,
            TaskbarStyle = TaskbarStyle is TaskbarStripStyle.Frameless or TaskbarStripStyle.Capsule
                or TaskbarStripStyle.Badges or TaskbarStripStyle.Meter
                ? TaskbarStyle : TaskbarStripStyle.Auto,
            TaskbarColor = TaskbarColor is TaskbarStripColorScheme.Light or TaskbarStripColorScheme.Dark
                or TaskbarStripColorScheme.HighContrast or TaskbarStripColorScheme.Monochrome
                or TaskbarStripColorScheme.IceBlue
                ? TaskbarColor : TaskbarStripColorScheme.Auto,
            Theme = Theme is ThemeMode.Light or ThemeMode.Dark ? Theme : ThemeMode.Auto,
            LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 0, 100),
            AutoCheckUpdates = AutoUpdateEnabled,
            GameEntryReminder = GameEntryReminderEnabled,
            DeviceThresholds = (DeviceThresholds ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && DeviceThresholdOptions.Contains(pair.Value))
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase),
            PollSeconds = normal,
            GamePollSeconds = Math.Clamp(GamePollSeconds, normal, 86_400),
            DeviceAliases = aliases,
            GameProcessNames = (GameProcessNames ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
                .Where(name => name.Length > 0 && !name.Contains('/') && !name.Contains('\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            HiddenDeviceIds = (HiddenDeviceIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            TrayExcludedDeviceIds = (TrayExcludedDeviceIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}

internal sealed record SavedBattery(DeviceIdentity Device, int Percentage, bool IsEstimated, DateTimeOffset ReadAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WasChargingOrFull { get; init; }
}
internal sealed record SavedReminder(DateTimeOffset? Due);
internal sealed record SavedHistorySample(DateTimeOffset At, int Percentage, bool IsEstimated, ChargingState Charging);
internal sealed record SavedDeviceHistory(
    DateTimeOffset FirstSeen, DateTimeOffset? LastOnline, SavedHistorySample[] RecentReadings);

internal sealed record TrayState
{
    public Dictionary<string, SavedBattery> Readings { get; init; } = [];
    public Dictionary<string, SavedReminder> Reminders { get; init; } = [];
    public string[] RemovedOfflineDevices { get; init; } = [];
    public Dictionary<string, SavedDeviceHistory> History { get; init; } = [];
    public Dictionary<string, string> BlockedReceivers { get; init; } = [];
    public Dictionary<string, SavedRuntimeEstimate> Runtime { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(TraySettings))]
[JsonSerializable(typeof(TrayState))]
internal sealed partial class TrayJsonContext : JsonSerializerContext;

internal sealed class TrayFiles(string folder)
{
    private bool preserveConfig;

    public string ConfigPath => Path.Combine(folder, "config.json");
    public string DiagnosticsPath => Path.Combine(folder, "diagnostics.txt");
    public string StatePath => Path.Combine(folder, "state.json");
    public UpdateFiles Updates { get; } = new(Path.Combine(folder, "updates"));

    public bool TryLoadSettings(out TraySettings settings, Action<string, Exception>? log = null)
    {
        settings = new TraySettings().Normalize();
        preserveConfig = false;
        try
        {
            TraySettings loaded;
            using (var stream = File.OpenRead(ConfigPath))
                loaded = JsonSerializer.Deserialize(stream, TrayJsonContext.Default.TraySettings)
                    ?? throw new JsonException("配置缺少根对象");
            settings = loaded.Normalize();
            if (settings.PollSeconds != loaded.PollSeconds || settings.TrayMode != loaded.TrayMode ||
                settings.DisplayLocation != loaded.DisplayLocation || settings.Theme != loaded.Theme ||
                settings.TaskbarStyle != loaded.TaskbarStyle || settings.TaskbarColor != loaded.TaskbarColor)
                SaveMigratedSettings(settings, log);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            settings = settings with { WelcomeShown = false };
        }
        catch (JsonException error)
        {
            log?.Invoke("配置解析失败，使用默认设置", error);
            preserveConfig = true;
            try
            {
                string backup = Path.Combine(folder, $"config.broken-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.json");
                File.Move(ConfigPath, backup);
                preserveConfig = false;
                foreach (string old in Directory.EnumerateFiles(folder, "config.broken-*.json"))
                    if (old != backup)
                        File.Delete(old);
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                log?.Invoke("损坏配置备份或清理失败", backupError);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            preserveConfig = true;
            log?.Invoke("配置读取失败", error);
        }
        return false;
    }

    public TrayState LoadState() => Read(StatePath, TrayJsonContext.Default.TrayState) ?? new();

    public TrayState LoadState(ref TraySettings settings, Action<string, Exception>? log = null)
    {
        var saved = LoadState();
        var emptyIds = (saved.History ?? []).Where(pair => pair.Value is
            { LastOnline: null } entry && !(entry.RecentReadings ?? []).Any(sample => sample is { Percentage: >= 0 and <= 100 }))
            .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        emptyIds.ExceptWith((saved.Readings ?? []).Where(pair => pair.Value is { Percentage: >= 0 and <= 100 })
            .Select(pair => pair.Key));
        if (emptyIds.Count == 0)
            return saved;

        var cleaned = settings;
        foreach (string id in emptyIds)
        {
            if (cleaned.DeviceAliases.ContainsKey(id) || cleaned.HiddenDeviceIds.Contains(id, StringComparer.OrdinalIgnoreCase) ||
                !cleaned.IsInTray(id))
                cleaned = cleaned.WithoutDevice(id);
        }
        if (cleaned != settings)
        {
            settings = cleaned;
            // 保留旧历史到下次启动，以便在配置文件恢复可写后重试。
            if (!SaveMigratedSettings(cleaned, log))
                return saved;
        }

        var migrated = saved with
        {
            Readings = WithoutEmpty(saved.Readings),
            Reminders = WithoutEmpty(saved.Reminders),
            History = WithoutEmpty(saved.History),
            Runtime = WithoutEmpty(saved.Runtime),
            RemovedOfflineDevices = (saved.RemovedOfflineDevices ?? []).Where(id => !emptyIds.Contains(id)).ToArray()
        };
        try { SaveState(migrated); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("迁移电量记录失败", error);
        }
        return migrated;

        Dictionary<string, T> WithoutEmpty<T>(Dictionary<string, T>? entries) =>
            (entries ?? []).Where(pair => !emptyIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private bool SaveMigratedSettings(TraySettings settings, Action<string, Exception>? log)
    {
        try
        {
            SaveSettings(settings);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("迁移设置失败", error);
            return false;
        }
    }

    private bool TryBackupLockedConfig(Action<string, Exception>? log)
    {
        if (!File.Exists(ConfigPath))
            return true;
        try
        {
            string backup = Path.Combine(folder, $"config.locked-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.json");
            File.Copy(ConfigPath, backup, overwrite: true);
            foreach (string old in Directory.EnumerateFiles(folder, "config.locked-*.json")
                         .OrderByDescending(file => file, StringComparer.OrdinalIgnoreCase).Skip(3))
            {
                try { File.Delete(old); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("备份原配置失败，继续保护原文件", error);
            return false;
        }
    }

    public void SaveSettings(TraySettings settings) => SaveSettings(settings, userInitiated: false, log: null);

    public void SaveSettings(TraySettings settings, bool userInitiated, Action<string, Exception>? log = null)
    {
        if (preserveConfig && !(userInitiated && TryBackupLockedConfig(log)))
            throw new IOException("原设置尚未备份");
        Write(ConfigPath, settings, TrayJsonContext.Default.TraySettings);
        preserveConfig = false;
    }

    /// <summary>D1（#21）：写盘前先序列化并与现有 state.json 字节比较，内容完全一致时跳过写盘。</summary>
    public bool SaveState(TrayState state)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state, TrayJsonContext.Default.TrayState);
        if (File.Exists(StatePath))
        {
            try
            {
                if (File.ReadAllBytes(StatePath).AsSpan().SequenceEqual(payload))
                    return false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 现有文件暂时不可读时回退为原子覆写。
            }
        }
        WriteBytes(StatePath, payload);
        return true;
    }

    private static T? Read<T>(string path, JsonTypeInfo<T> type)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, type);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return default;
        }
    }

    private void Write<T>(string path, T value, JsonTypeInfo<T> type) =>
        WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, type));

    private void WriteBytes(string path, byte[] payload)
    {
        Directory.CreateDirectory(folder);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, payload);
            if (File.Exists(path))
            {
                try { File.Replace(temporary, path, null, ignoreMetadataErrors: true); }
                catch (FileNotFoundException) { File.Move(temporary, path); }
            }
            else
                File.Move(temporary, path);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
