using System.Drawing;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Jige.Tray.Windows;

internal readonly record struct TrayIconSlot(string Key, DeviceStatus? Device, string Tooltip);

/// <summary>计算托盘应当显示的图标槽位，供 Windows 多图标管理器与单元测试共用。</summary>
internal static class TrayIconPlan
{
    public const string SingleKey = "__single__";

    public static TrayIconSlot[] Build(TrayModel model, bool suppressTooltip)
    {
        var devices = model.TrayDevices();
        if (model.Settings.TrayMode == TrayDisplayMode.LowestBattery || devices.Length == 0)
        {
            var target = devices.Length > 0 ? devices[0] : null;
            string tip = suppressTooltip ? "" :
                target is null && model.OrderedDevices().Length > 0 ? "几格 · 点击打开面板" :
                Presentation.Tooltip(model);
            return [new(SingleKey, target, tip)];
        }

        return devices.Select(device =>
            new TrayIconSlot(device.Device.Id, device, suppressTooltip ? "" : Presentation.DeviceTooltip(model, device)))
            .ToArray();
    }
}

/// <summary>管理一个或多个独立托盘图标：设备增删或切换模式时立即清理旧图标，不留残余。</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class TrayIconManager(HWND window, uint callback) : IDisposable
{
    private readonly Dictionary<string, TrayIcon> icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, TrayIcon> byId = [];
    private uint nextId = 1;
    private uint? lastActiveId;

    public bool AnyRegistered => icons.Values.Any(item => item.Registered);
    public bool AllRegistered => icons.Count > 0 && icons.Values.All(item => item.Registered);

    public void Recover()
    {
        foreach (var icon in icons.Values)
            icon.Recover();
    }

    public bool TryGetById(uint id, out TrayIcon? icon)
    {
        if (byId.TryGetValue(id, out icon))
        {
            lastActiveId = id;
            return true;
        }
        return false;
    }

    public bool TryGetRect(uint? preferredId, out RECT bounds)
    {
        if (preferredId is uint id && byId.TryGetValue(id, out var specific) && specific.TryGetRect(out bounds))
            return true;
        if (lastActiveId is uint recent && byId.TryGetValue(recent, out var last) && last.TryGetRect(out bounds))
            return true;
        foreach (var icon in icons.Values)
        {
            if (icon.TryGetRect(out bounds))
                return true;
        }
        bounds = default;
        return false;
    }

    public bool ContainsCursor(Point cursor)
    {
        foreach (var icon in icons.Values)
        {
            if (icon.TryGetRect(out var bounds) &&
                cursor.X >= bounds.left && cursor.X < bounds.right &&
                cursor.Y >= bounds.top && cursor.Y < bounds.bottom)
                return true;
        }
        return false;
    }

    public void Sync(TrayModel model, bool darkTaskbar, bool suppressTooltip)
    {
        var slots = TrayIconPlan.Build(model, suppressTooltip);
        var desiredKeys = slots.Select(slot => slot.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 先删除已经不在列表中的图标，避免残留。
        foreach (string obsolete in icons.Keys.Where(key => !desiredKeys.Contains(key)).ToArray())
        {
            var removed = icons[obsolete];
            icons.Remove(obsolete);
            byId.Remove(removed.Id);
            if (lastActiveId == removed.Id)
                lastActiveId = null;
            removed.Dispose();
        }

        int threshold = model.Settings.LowBatteryThreshold;
        foreach (var slot in slots)
        {
            if (!icons.TryGetValue(slot.Key, out var icon))
            {
                uint id = nextId++;
                if (nextId > ushort.MaxValue)
                    nextId = 1;
                while (byId.ContainsKey(id))
                    id = id >= ushort.MaxValue ? 1u : id + 1;
                icon = new TrayIcon(window, callback, id);
                icons[slot.Key] = icon;
                byId[id] = icon;
            }

            bool hasLocation = icon.TryGetRect(out var bounds);
            if (!hasLocation)
                bounds = Desktop.TaskbarAnchor();
            var appearance = Presentation.Icon(slot.Device, darkTaskbar, threshold);
            icon.Update(appearance, IconSize(bounds), slot.Tooltip);
            if (!hasLocation && icon.TryGetRect(out var registeredBounds))
                icon.Update(appearance, IconSize(registeredBounds), slot.Tooltip);
        }
    }

    private static int IconSize(RECT bounds)
    {
        uint dpi = Desktop.Dpi(PInvoke.MonitorFromRect(in bounds, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));
        return Math.Clamp(PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi), 8, 256);
    }

    public void Dispose()
    {
        foreach (var icon in icons.Values)
            icon.Dispose();
        icons.Clear();
        byId.Clear();
        TrayIcon.DisposeSharedRenderer();
    }
}
