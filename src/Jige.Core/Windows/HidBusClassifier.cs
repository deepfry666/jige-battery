using System.Globalization;

namespace Jige.Core.Windows;

internal static class HidBusClassifier
{
    public static bool IsUsb(string instance) =>
        instance.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase);

    public static HidBus Classify(string path, string nodeId, IReadOnlyList<string> ancestors)
    {
        if (BluetoothHidPath.IsBluetooth(path) || BluetoothHidPath.IsBluetooth(nodeId))
            return HidBus.Bluetooth;
        if (IsUsb(nodeId))
            return HidBus.Usb;
        foreach (string ancestor in ancestors)
        {
            if (BluetoothHidPath.IsBluetooth(ancestor))
                return HidBus.Bluetooth;
            if (IsUsb(ancestor))
                return HidBus.Usb;
        }
        if (ancestors.Count == 0 && HasUsbVidPid(path, nodeId))
            return HidBus.Usb;
        return HidBus.NonUsb;
    }

    public static string? FindUsbAncestor(IReadOnlyList<string> ancestors)
    {
        for (int i = 0; i < ancestors.Count; i++)
        {
            string id = ancestors[i];
            if (BluetoothHidPath.IsBluetooth(id))
                return null;
            if (!IsUsb(id))
                continue;
            if (ReadHexTag(id, "MI_", 2) is null)
                return id;
            if (i + 1 < ancestors.Count)
            {
                string compositeId = ancestors[i + 1];
                if (IsUsb(compositeId) && ReadHexTag(compositeId, "VID_", 4) == ReadHexTag(id, "VID_", 4) &&
                    ReadHexTag(compositeId, "PID_", 4) == ReadHexTag(id, "PID_", 4))
                    return compositeId;
            }
            return id;
        }
        return null;
    }

    public static string ResolveFallbackReceiverId(string nodeId, string fallback, IReadOnlyList<string> ancestors)
    {
        if (IsUsb(nodeId) && ReadHexTag(nodeId, "MI_", 2) is null)
            return nodeId;
        if (FindUsbAncestor(ancestors) is { } usb)
            return usb;
        if (nodeId.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) &&
            ancestors is [var parent, ..] &&
            !parent.Equals(@"HTREE\ROOT\0", StringComparison.OrdinalIgnoreCase) &&
            !BluetoothHidPath.IsBluetooth(parent))
            return parent;
        return nodeId.Length == 0 ? fallback : nodeId;
    }

    internal static ushort? ReadHexTag(string text, string tag, int digits)
    {
        int offset = text.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
        if (offset < 0 || text.Length < offset + tag.Length + digits)
            return null;
        return ushort.TryParse(text.AsSpan(offset + tag.Length, digits),
            NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort value) ? value : null;
    }

    private static bool HasUsbVidPid(string path, string nodeId) =>
        (ReadHexTag(path, "VID_", 4) ?? ReadHexTag(nodeId, "VID_", 4)).HasValue &&
        (ReadHexTag(path, "PID_", 4) ?? ReadHexTag(nodeId, "PID_", 4)).HasValue;
}
