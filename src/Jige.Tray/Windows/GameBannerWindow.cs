using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Jige.Tray.Windows;

/// <summary>
/// 进入游戏时的低电量小横幅：屏幕右下角显示几秒后自动消失。
/// 窗口不抢焦点、鼠标点击穿透（与悬停卡片同一套窗口样式），不会把玩家切出游戏。
/// 独占全屏游戏由调用方跳过，不在其上叠窗口。
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class GameBannerWindow(HWND hwnd, TrayModel model) : IDisposable
{
    public const int Width = 320, Height = 68, Margin = 24;
    public const uint VisibleMs = 4_500;

    private Composition? composition;
    private Composition.Layer? surface;

    public HWND Handle { get; private set; } = hwnd;

    public void Show(DeviceStatus device, RuntimeEstimate runtime, int others, HWND foreground)
    {
        var screen = PInvoke.MonitorFromWindow(foreground, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (!Desktop.TryGetMonitor(screen, out var info))
            throw new InvalidOperationException("找不到横幅所在显示器");
        var area = info.rcWork.right > info.rcWork.left && info.rcWork.bottom > info.rcWork.top
            ? info.rcWork : info.rcMonitor;
        float dpiScale = Desktop.Dpi(screen) / 96f;
        int areaW = area.right - area.left;
        int areaH = area.bottom - area.top;
        float scale = Math.Min(dpiScale, Math.Min((areaW - 16f) / Width, (areaH - 16f) / Height));
        if (scale <= 0)
            throw new InvalidOperationException("找不到横幅所在显示器");

        bool dark = Presentation.ResolveDark(model.Settings.Theme, Desktop.DarkApps);
        bool acrylic = Desktop.Backdrop(Handle, dark);
        var colors = Desktop.PanelPalette(dark);
        surface?.Dispose();
        surface = null;
        composition?.Dispose();
        composition = null;
        composition = new(Handle, scale);
        surface = new(composition, composition.Root, 0, 0, Width, Height);

        var tone = Presentation.Tone(device, model.ThresholdFor(device.Device.Id));
        var (suffix, detail) = Presentation.GameEntryBannerParts(device, runtime, others);
        surface.Draw(canvas =>
        {
            canvas.Fill(0, 0, Width, Height, 8, acrylic ? colors.Flyout : colors.Solid);
            canvas.Stroke(.5f, .5f, Width - 1, Height - 1, 7.5f, colors.Border);
            var ink = colors.Status(tone == BatteryTone.Normal ? BatteryTone.Warning : tone);
            canvas.BrandTile(DeviceBrandResolver.Resolve(device.Device), 14, 16, 36, ink.WithAlpha(36), ink);
            // 名称过长时只省略名称，「只剩 xx%」始终完整可见。
            canvas.NameWithSuffix(model.Name(device.Device), suffix, 60, 12, Width - 74, 22, 14, colors.Ink, 600);
            canvas.Text(detail, 60, 35, Width - 74, 20, 12, colors.Secondary, ellipsis: true);
        });
        composition.Root.Clip(Width, Height);
        composition.Root.SetOpacity(1f);
        composition.Commit();

        int width = Pixels(Width, scale), height = Pixels(Height, scale), margin = Pixels(Margin, scale);
        int left = Math.Clamp(area.right - width - margin, area.left, Math.Max(area.left, area.right - width));
        int top = Math.Clamp(area.bottom - height - margin, area.top, Math.Max(area.top, area.bottom - height));
        // 窗口创建时已带 WS_EX_TOPMOST，这里只定位，不改层级、不激活。
        if (!PInvoke.SetWindowPos(Handle, default, left, top, width, height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER))
            throw new InvalidOperationException("无法显示进游戏提醒横幅");
        PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    }

    private static int Pixels(float dip, float scale) => (int)MathF.Round(dip * scale, MidpointRounding.AwayFromZero);

    public void Dispose()
    {
        if (Handle.IsNull)
            return;
        var window = Handle;
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_HIDE);
        surface?.Dispose();
        surface = null;
        composition?.Dispose();
        composition = null;
        Handle = default;
        PInvoke.DestroyWindow(window);
    }
}
