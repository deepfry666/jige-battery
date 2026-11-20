namespace Jige.Core;

public enum DeviceConnection { Receiver, UsbCable, Bluetooth }
public enum DeviceKind { Mouse, Headset, Keyboard, Other }
public enum LinkState { Online, Asleep, Offline, Busy, Unknown }
public enum ChargingState { Unknown, NotCharging, Charging, Full }
public enum ReadIssue { None, Timeout, AccessDenied, Busy, Disconnected, InvalidResponse, IoError }
public enum AdaptationSource { Catalog, Generic }
public enum PresenceProbeKind { Heartbeat, PushListener, FullPollOnly }

/// <summary>设备协议验证状态分级（B1）：严格区分实机已验证、同家族推定与仅按资料。</summary>
public enum VerificationTier
{
    /// <summary>在 Windows 实机上实际看到过读数。</summary>
    HardwareVerified,
    /// <summary>与已实机验证设备同一协议家族，且来自同一份权威型号表。</summary>
    FamilyInferred,
    /// <summary>照公开开源实现或协议文档编写，尚未在实机上验证。</summary>
    DocsOnly
}

public sealed record DeviceIdentity(
    string Id, string Name, DeviceKind Kind,
    AdaptationSource Source = AdaptationSource.Catalog,
    VerificationTier Verification = VerificationTier.DocsOnly,
    DeviceConnection Connection = DeviceConnection.Receiver)
{
    public bool NameFromHidpp { get; init; }
    public string? BluetoothContainerKey { get; init; }
    public string? SourceLabel { get; init; }
}

public sealed record PresenceUpdate(
    string ReceiverId, byte? Slot, string? DeviceId, LinkState Link, ReadIssue Issue = ReadIssue.None)
{
    public BatteryReading? Battery { get; init; }
    public DeviceConnection? Connection { get; init; }
}

public sealed record BatteryReading(
    DeviceIdentity Device, int? Percentage, bool IsEstimated, ChargingState Charging,
    LinkState Link, DateTimeOffset ReadAt, ReadIssue Issue = ReadIssue.None, string? Detail = null)
{
    internal string? BluetoothContainer { get; init; }
    internal ulong? BluetoothAddress { get; init; }
    internal string? ProtocolSummary { get; init; }
    public int? Percentage { get; } = Percentage is < 0 or > 100
        ? throw new ArgumentOutOfRangeException(nameof(Percentage))
        : Percentage;
}

public abstract record ReceiverEndpoint(string ReceiverId, string Path);

public sealed record HidInterface(
    string ReceiverId, string Path, ushort? VendorId, ushort? ProductId, string? Product,
    ushort? UsagePage, ushort? Usage, ushort? InputLength, ushort? OutputLength,
    ushort? FeatureLength, byte? InterfaceNumber, ReadIssue MetadataIssue = ReadIssue.None,
    string? MetadataError = null) : ReceiverEndpoint(ReceiverId, Path)
{
    internal ulong? BluetoothAddress { get; init; }
    internal bool BluetoothHid { get; init; }
    internal bool InputOnly { get; init; }
    internal byte? RequiredInputReportId { get; init; }
    internal byte? RequiredOutputReportId { get; init; }
    internal byte? RequiredFeatureReportId { get; init; }
}

public sealed record UsbSerialPort(
    string ReceiverId, string Path, string? PortName, IReadOnlyList<string> HardwareIds,
    string? MetadataError = null) : ReceiverEndpoint(ReceiverId, Path);

public sealed record DeviceInventory(
    IReadOnlyList<HidInterface> Hid, IReadOnlyList<UsbSerialPort> Serial,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<BluetoothDevice> Bluetooth { get; init; } = [];
}

public enum FrameDirection { Sent, Received, Incomplete }
public delegate void FrameTrace(FrameDirection direction, ReadOnlySpan<byte> bytes);
