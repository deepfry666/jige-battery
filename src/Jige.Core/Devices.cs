namespace Jige.Core;

public enum DeviceKind { Mouse, Headset, Keyboard, Other }
public enum LinkState { Online, Asleep, Offline, Busy, Unknown }
public enum ChargingState { Unknown, NotCharging, Charging }
public enum ReadIssue { None, Timeout, AccessDenied, Busy, Disconnected, InvalidResponse, IoError }

public sealed record DeviceIdentity(string Id, string Name, DeviceKind Kind);

public sealed record BatteryReading(
    DeviceIdentity Device, int? Percentage, bool IsEstimated, ChargingState Charging,
    LinkState Link, DateTimeOffset ReadAt, ReadIssue Issue = ReadIssue.None, string? Detail = null)
{
    public int? Percentage { get; } = Percentage is < 0 or > 100
        ? throw new ArgumentOutOfRangeException(nameof(Percentage))
        : Percentage;
}

public abstract record ReceiverEndpoint(string ReceiverId, string Path);

public sealed record HidInterface(
    string ReceiverId, string Path, ushort? VendorId, ushort? ProductId, string? Product,
    ushort? UsagePage, ushort? Usage, ushort? InputLength, ushort? OutputLength,
    ushort? FeatureLength, byte? InterfaceNumber, ReadIssue MetadataIssue = ReadIssue.None,
    string? MetadataError = null) : ReceiverEndpoint(ReceiverId, Path);

public sealed record UsbSerialPort(
    string ReceiverId, string Path, string? PortName, IReadOnlyList<string> HardwareIds,
    string? MetadataError = null) : ReceiverEndpoint(ReceiverId, Path);

public sealed record DeviceInventory(
    IReadOnlyList<HidInterface> Hid, IReadOnlyList<UsbSerialPort> Serial,
    IReadOnlyList<string> Warnings);

public enum FrameDirection { Sent, Received, Incomplete }
public delegate void FrameTrace(FrameDirection direction, ReadOnlySpan<byte> bytes);
