using Jige.Tray.Windows;

namespace Jige.Tray;

internal static class Program
{
    // 必须是 STA：Direct2D/DirectComposition/托盘消息循环都在主线程，
    // 顶层语句默认让运行时把主线程初始化为 MTA，之后再 CoInitializeEx(STA) 会返回 0x80010106 导致启动即退出。
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Console.Error.WriteLine("几格托盘程序需要 Windows 10 1809 或更高版本。");
            return 2;
        }

        if (args is ["--check"])
            return NativeCheck.Run();

        return TrayApp.Run();
    }
}
