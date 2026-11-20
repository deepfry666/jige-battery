namespace Jige.Tray;

internal static class InstallPaths
{
    public const string Executable = "Jige.exe";
    public const string UninstallRegistryKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{DD33BE43-6D23-45E0-936F-9B6B067067E8}_is1";

    public static string InstalledExecutable(string localAppData) =>
        Path.Combine(localAppData, "Programs", "Jige", Executable);

    public static string ResolveInstalledExecutable(string localAppData, string? registeredInstallDirectory)
    {
        if (string.IsNullOrWhiteSpace(registeredInstallDirectory))
            return InstalledExecutable(localAppData);

        string directory = TrimDirectorySeparators(registeredInstallDirectory.Trim());
        return directory.Length == 0
            ? InstalledExecutable(localAppData)
            : Path.Combine(directory, Executable);
    }

    public static bool IsInstalledExecutable(string? process, string localAppData,
        string? registeredInstallDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(process))
            return false;

        string expected = ResolveInstalledExecutable(localAppData, registeredInstallDirectory);
        return string.Equals(NormalizeForComparison(process), NormalizeForComparison(expected),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string? AutostartCommand(bool enabled, string exe) => enabled ? $"\"{exe}\"" : null;

    public static bool ShouldRepairAutostart(object? current, string? desired) => desired is null
        ? current is not null
        : current is not string command || !string.Equals(command, desired, StringComparison.OrdinalIgnoreCase);

    private static string TrimDirectorySeparators(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return trimmed + Path.DirectorySeparatorChar;
        return trimmed;
    }

    private static string NormalizeForComparison(string path)
    {
        string normalized = path.Trim().Replace('/', '\\');
        while (normalized.Length > 3 && normalized.EndsWith('\\'))
            normalized = normalized[..^1];
        return normalized;
    }
}
