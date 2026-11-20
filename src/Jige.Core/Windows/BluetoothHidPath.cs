using System.Globalization;

namespace Jige.Core.Windows;

internal static class BluetoothHidPath
{
    // Classic HID uses the 32-bit PnP VID source field. HID over GATT uses an 8-bit source field.
    public static ushort? UsbVendorId(string path) =>
        ReadId(path, "VID&0002") ?? ReadId(path, "_DEV_VID&02");

    public static ushort? ProductId(string path) => ReadId(path, "_PID&");

    public static bool IsBluetooth(string text) =>
        UsbVendorId(text).HasValue ||
        text.Contains("{00001124-0000-1000-8000-00805F9B34FB}", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("{00001812-0000-1000-8000-00805F9B34FB}", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith(@"BTHLEDEVICE\", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith(@"BTH\", StringComparison.OrdinalIgnoreCase);

    private static ushort? ReadId(string path, string tag)
    {
        int index = path.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;
        index += tag.Length;
        if (path.Length < index + 4 || path.Length > index + 4 && path[index + 4] is not ('_' or '&' or '#' or '\\'))
            return null;
        return ushort.TryParse(path.AsSpan(index, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
            out ushort value) ? value : null;
    }
}
