using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal static class NativeCheck
{
    /// <summary>CI 自检。WinExe 没有控制台，失败原因写入 JIGE_CHECK_LOG 指定的文件。</summary>
    public static int Run()
    {
        var init = PInvoke.CoInitializeEx(COINIT.COINIT_APARTMENTTHREADED);
        if (init.Failed)
            return Fail($"CoInitializeEx 失败 0x{init.Value:X8}");
        try
        {
            foreach (int size in new[] { 16, 24, 32 })
            {
                foreach (var sample in new IconAppearance[]
                {
                    new(72, BatteryTone.Normal, new(0x1b1b1b)),
                    new(48, BatteryTone.Charging, new(0x0f7b0f)),
                    new(100, BatteryTone.Normal, new(0xf3f3f3)),
                    new(null, BatteryTone.Idle, new(0xf3f3f3, 140))
                })
                {
                    var icon = TrayIcon.Draw(size, sample);
                    if (!PInvoke.DestroyIcon(icon))
                        return Fail($"DestroyIcon 失败（{size}px）");
                }
            }
            return 0;
        }
        catch (Exception error)
        {
            Desktop.Log(error.Message);
            return Fail(error.ToString());
        }
        finally
        {
            PInvoke.CoUninitialize();
        }
    }

    private static int Fail(string message)
    {
        if (Environment.GetEnvironmentVariable("JIGE_CHECK_LOG") is { Length: > 0 } path)
            File.WriteAllText(path, message);
        return 1;
    }
}
