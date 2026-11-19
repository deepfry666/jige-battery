namespace Jige.Tray;

internal static class InstallPaths
{
    public const string Executable = "Jige.exe";

    public static string InstalledExecutable(string localAppData) =>
        Path.Combine(localAppData, "Programs", "Jige", Executable);

    public static bool IsInstalledExecutable(string? process, string localAppData) =>
        string.Equals(process, InstalledExecutable(localAppData), StringComparison.OrdinalIgnoreCase);

    public static string? AutostartCommand(bool enabled, string exe) => enabled ? $"\"{exe}\"" : null;

    public static bool ShouldRepairAutostart(object? current, string? desired) => desired is null
        ? current is not null
        : current is not string command || !string.Equals(command, desired, StringComparison.OrdinalIgnoreCase);
}
