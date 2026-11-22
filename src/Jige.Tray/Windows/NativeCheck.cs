using System.Runtime.Versioning;
using System.Text.Json;
using Jige.Core;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class NativeCheck
{
    private static readonly Guid AutomationClass = new("e22ad333-b25f-460c-83d0-0581107395c9"); // CUIAutomation8

    /// <summary>CI 自检。WinExe 没有控制台，失败原因写入 JIGE_CHECK_LOG 指定的文件。</summary>
    public static int Run()
    {
        var init = PInvoke.CoInitializeEx(COINIT.COINIT_APARTMENTTHREADED);
        if (init.Failed)
            return Fail($"CoInitializeEx 失败 0x{init.Value:X8}");
        try
        {
            foreach (int size in new[] { 16, 24, 32, 48 })
            {
                foreach (var sample in new IconAppearance[]
                {
                    new(72, BatteryTone.Normal, new(0x1b1b1b), DeviceKind.Mouse, new(0, 56)),
                    new(80, BatteryTone.Normal, new(0xffffff), DeviceKind.Keyboard, new(0xffffff, 71)),
                    new(48, BatteryTone.Charging, new(0x0f7b0f), DeviceKind.Headset, new(0, 56)),
                    new(100, BatteryTone.Charging, new(0x6ccb5f), DeviceKind.Other, new(0xffffff, 71)),
                    new(null, BatteryTone.Idle, new(0xf3f3f3, 140), DeviceKind.Keyboard, new(0xffffff, 71)),
                    new(null, BatteryTone.Idle, new(0xf3f3f3, 140), null, new(0xffffff, 71))
                })
                {
                    var icon = TrayIcon.Draw(size, sample);
                    if (!PInvoke.DestroyIcon(icon))
                        return Fail($"DestroyIcon 失败（{size}px）");
                }
            }

            // 验证面板内嵌图标资源存在且为完整 64×64 预乘 BGRA（16384 字节）。
            using (var panelIcon = typeof(NativeCheck).Assembly.GetManifestResourceStream("Jige.PanelIcon"))
            {
                if (panelIcon is null || panelIcon.Length != 64 * 64 * 4)
                    return Fail($"面板图标资源 Jige.PanelIcon 缺失或大小异常（{panelIcon?.Length ?? -1} 字节）");
            }

            // 验证任务栏直读条像素光栅化与分层命中 Alpha 填充。
            TaskbarStripDevice[] stripDevices =
            [
                new(DeviceKind.Mouse, 82, BatteryTone.Normal, ShowBolt: false),
                new(DeviceKind.Keyboard, 100, BatteryTone.Charging, ShowBolt: true)
            ];
            var stripLayout = TaskbarStripLayout.Build(stripDevices);
            var stripPalette = TaskbarStripPalette.Create(dark: true);
            uint[] stripPixels = TaskbarStripPixels.Render(stripLayout, stripPalette, scale: 2);
            TaskbarStripPixels.FillLayeredHitAlpha(stripPixels);
            if (stripPixels.Length != stripLayout.Width * 2 * TaskbarStripLayout.Height * 2)
                return Fail("任务栏直读条渲染像素尺寸异常");

            // 验证 NativeAOT 下 JSON 源生成上下文可正常序列化与反序列化。
            string settingsJson = JsonSerializer.Serialize(new TraySettings(), TrayJsonContext.Default.TraySettings);
            if (JsonSerializer.Deserialize(settingsJson, TrayJsonContext.Default.TraySettings) is null)
                return Fail("TraySettings JSON 往返失败");
            string stateJson = JsonSerializer.Serialize(new TrayState(), TrayJsonContext.Default.TrayState);
            if (JsonSerializer.Deserialize(stateJson, TrayJsonContext.Default.TrayState) is null)
                return Fail("TrayState JSON 往返失败");
            string releaseJson = JsonSerializer.Serialize(
                new ReleaseResponse("v0.2.19", "ok", false, false, []),
                ReleaseJsonContext.Default.ReleaseResponse);
            if (JsonSerializer.Deserialize(releaseJson, ReleaseJsonContext.Default.ReleaseResponse)?.Tag != "v0.2.19")
                return Fail("ReleaseResponse JSON 往返失败");

            // 验证 UI Automation COM 对象可实例化（用于任务栏直读条几何发现）。
            var clsid = AutomationClass;
            var iid = IUIAutomation.IID_Guid;
            IUIAutomation* uia = null;
            var hr = PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, (void**)&uia);
            if (hr.Failed || uia == null)
                return Fail($"CoCreateInstance(CUIAutomation8) 失败 0x{hr.Value:X8}");
            uia->Release();

            return 0;
        }
        catch (Exception error)
        {
            Desktop.Log("原生启动自检失败", error);
            return Fail(TrayLog.Describe("原生启动自检失败", error));
        }
        finally
        {
            TrayIcon.DisposeSharedRenderer();
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
