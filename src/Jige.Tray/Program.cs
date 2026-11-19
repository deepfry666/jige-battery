using Jige.Tray.Windows;

if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
{
    Console.Error.WriteLine("几格托盘程序需要 Windows 10 1809 或更高版本。");
    return 2;
}

if (args is ["--check"])
    return NativeCheck.Run();

return TrayApp.Run();
