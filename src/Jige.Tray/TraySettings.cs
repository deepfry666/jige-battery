using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jige.Core;

namespace Jige.Tray;

internal sealed record TraySettings
{
    public string? PrimaryDeviceId { get; init; }
    public int LowBatteryThreshold { get; init; } = 15;
    public int PollSeconds { get; init; } = 60;
    public int GamePollSeconds { get; init; } = 300;
    public bool GameSilent { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public Dictionary<string, string> DeviceAliases { get; init; } = [];

    public TraySettings Normalize()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in DeviceAliases ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                aliases[id] = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        int normal = Math.Clamp(PollSeconds, 15, 86_400);
        return this with
        {
            LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 0, 100),
            PollSeconds = normal,
            GamePollSeconds = Math.Clamp(GamePollSeconds, normal, 86_400),
            DeviceAliases = aliases
        };
    }
}

internal sealed record SavedBattery(DeviceIdentity Device, int Percentage, bool IsEstimated, DateTimeOffset ReadAt);
internal sealed record SavedReminder(DateTimeOffset? Due);
internal sealed record TrayState
{
    public Dictionary<string, SavedBattery> Readings { get; init; } = [];
    public Dictionary<string, SavedReminder> Reminders { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(TraySettings))]
[JsonSerializable(typeof(TrayState))]
internal sealed partial class TrayJsonContext : JsonSerializerContext;

internal sealed class TrayFiles(string folder)
{
    public string ConfigPath => Path.Combine(folder, "config.json");
    public string DiagnosticsPath => Path.Combine(folder, "diagnostics.txt");
    public string StatePath => Path.Combine(folder, "state.json");

    public bool TryLoadSettings(out TraySettings settings)
    {
        var loaded = Read(ConfigPath, TrayJsonContext.Default.TraySettings);
        settings = (loaded ?? new()).Normalize();
        return loaded is not null;
    }

    public TrayState LoadState() => Read(StatePath, TrayJsonContext.Default.TrayState) ?? new();

    public void SaveSettings(TraySettings settings) => Write(ConfigPath, settings, TrayJsonContext.Default.TraySettings);
    public void SaveState(TrayState state) => Write(StatePath, state, TrayJsonContext.Default.TrayState);

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

    private void Write<T>(string path, T value, JsonTypeInfo<T> type)
    {
        Directory.CreateDirectory(folder);
        string temporary = path + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, value, type);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
