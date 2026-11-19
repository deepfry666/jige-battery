using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal static class NativeCheck
{
    public static int Run()
    {
        if (PInvoke.CoInitializeEx(COINIT.COINIT_APARTMENTTHREADED).Failed)
            return 1;
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
                        return 1;
                }
            }
            return 0;
        }
        catch (Exception error)
        {
            Desktop.Log(error.Message);
            return 1;
        }
        finally
        {
            PInvoke.CoUninitialize();
        }
    }
}
