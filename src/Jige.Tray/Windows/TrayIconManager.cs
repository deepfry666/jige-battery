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

    // Windows 11 按"程序路径 + 图标 uID"记住用户把哪个图标拖到了时钟旁。
    // uID 必须由设备 ID 稳定推导，不能按启动顺序递增，否则重启后被提升的图标会换成别的设备。
    public static uint StableId(string key)
    {
        if (string.Equals(key, SingleKey, StringComparison.OrdinalIgnoreCase))
            return 1;
        uint hash = 2166136261;
        foreach (char c in key.ToUpperInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }
        return 2 + hash % 65000;
    }

    public static TrayIconSlot[] Build(TrayModel model, bool suppressTooltip, bool showIcons = true)
    {
        if (!showIcons)
            return [];
        var devices = model.TrayDevices();
        if (model.Settings.TrayMode == TrayDisplayMode.LowestBattery || devices.Length == 0)
        {
            var target = devices.Length > 0 ? devices[0] : null;
            string tip = suppressTooltip ? "" : target is null ? "几格 · 点击打开面板" : Presentation.Tooltip(model);
            return [new(SingleKey, target, tip)];
        }

        return devices.Select(device =>
            new TrayIconSlot(device.Device.Id, device, suppressTooltip ? "" : Presentation.DeviceTooltip(model, device)))
            .ToArray();
    }

    public static uint ReserveId(string key, Dictionary<string, uint> reservations)
    {
        if (reservations.TryGetValue(key, out uint id))
            return id;
        id = StableId(key);
        // 离线后继续保留 uID。即使两个设备的散列碰撞，重连时仍能使用原来的 uID。
        while (reservations.ContainsValue(id))
            id = id >= ushort.MaxValue ? 2u : id + 1;
        reservations.Add(key, id);
        return id;
    }
}

/// <summary>管理一个或多个独立托盘图标：设备增删或切换模式时立即清理旧图标，不留残余。</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class TrayIconManager(HWND window, uint callback) : IDisposable
{
    private readonly Dictionary<string, TrayIcon> icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> reservedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, TrayIcon> byId = [];
    private readonly Dictionary<uint, string> keyById = [];
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

    public bool TryGetSlotKey(uint id, out string? key) => keyById.TryGetValue(id, out key);

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

    public void Clear()
    {
        foreach (var icon in icons.Values)
            icon.Dispose();
        icons.Clear();
        byId.Clear();
        keyById.Clear();
        lastActiveId = null;
    }

    public void Sync(TrayModel model, bool darkTaskbar, bool suppressTooltip, bool showIcons = true)
    {
        var slots = TrayIconPlan.Build(model, suppressTooltip, showIcons);
        var desiredKeys = slots.Select(slot => slot.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int threshold = model.Settings.LowBatteryThreshold;
        foreach (var slot in slots)
        {
            if (!icons.TryGetValue(slot.Key, out var icon))
            {
                uint id = TrayIconPlan.ReserveId(slot.Key, reservedIds);
                icon = new TrayIcon(window, callback, id);
                icons[slot.Key] = icon;
                byId[id] = icon;
                keyById[id] = slot.Key;
            }

            bool hasLocation = icon.TryGetRect(out var bounds);
            if (!hasLocation)
                bounds = Desktop.TaskbarAnchor();
            var appearance = Presentation.Icon(slot.Device, darkTaskbar, threshold);
            try
            {
                icon.Update(appearance, IconSize(bounds), slot.Tooltip);
                if (!hasLocation && icon.TryGetRect(out var registeredBounds))
                    icon.Update(appearance, IconSize(registeredBounds), slot.Tooltip);
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.ExternalException or InvalidOperationException or UnauthorizedAccessException)
            {
                Desktop.Log("更新单个托盘图标失败", error);
            }
        }

        // 先注册新图标，再移除旧图标，最后一台设备断开时面板入口仍然可用。
        foreach (string obsolete in icons.Keys.Where(key => !desiredKeys.Contains(key)).ToArray())
        {
            var removed = icons[obsolete];
            icons.Remove(obsolete);
            byId.Remove(removed.Id);
            keyById.Remove(removed.Id);
            if (lastActiveId == removed.Id)
                lastActiveId = null;
            removed.Dispose();
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
        keyById.Clear();
        TrayIcon.DisposeSharedRenderer();
    }
}
