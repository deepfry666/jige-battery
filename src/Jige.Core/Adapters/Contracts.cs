namespace Jige.Core.Adapters;

public sealed record DeviceTarget(DeviceIdentity Device, ReceiverEndpoint Endpoint, string Slot)
{
    internal HidInterface? ShortHid { get; init; }
    // 同一接收器上的其他厂商输入集合，仅用于只读被动监听回复（不向其发送任何命令）。
    internal IReadOnlyList<HidInterface> SiblingInputHids { get; init; } = [];
    // 同一物理接收器上的关联厂商集合（用于跨集合只读协议：如 XM2we/达尔优分集合、ROG OMNI 多设备、HyperX Cloud III S）。
    internal IReadOnlyList<HidInterface> RelatedHids { get; init; } = [];
}

public interface IDeviceAdapter
{
    IEnumerable<DeviceTarget> Detect(DeviceInventory inventory);

    ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken);
}

internal interface IMultiDeviceAdapter : IDeviceAdapter
{
    ValueTask<IReadOnlyList<BatteryReading>> ReadDevicesAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken);
}

internal interface IPresenceAdapter
{
    PresenceProbeKind PresenceKind { get; }

    ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken cancellationToken);
}

internal interface IPushPresenceAdapter : IPresenceAdapter
{
    Task ListenPushAsync(
        DeviceTarget target, IReceiverTransport transport, Action<PresenceUpdate> onUpdate, CancellationToken cancellationToken);
}

// 由注册表在带期限的工作线程中调用，同一个接收器的会话不会交叠。
public interface IReceiverTransport
{
    IHidSession OpenHid(HidInterface endpoint, CancellationToken cancellationToken);
    ISerialSession OpenSerial(UsbSerialPort endpoint, int baudRate, CancellationToken cancellationToken);
}

public interface IHidSession : IDisposable
{
    ValueTask WriteOutputAsync(byte[] report, CancellationToken cancellationToken);
    ValueTask<int> ReadInputAsync(byte[] report, CancellationToken cancellationToken);
    ValueTask WriteFeatureAsync(byte[] report, CancellationToken cancellationToken);
    ValueTask ReadFeatureAsync(byte[] report, CancellationToken cancellationToken);
}

public interface ISerialSession : IDisposable
{
    ValueTask WriteAsync(byte[] bytes, CancellationToken cancellationToken);
    ValueTask<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken);
}

public sealed class TransportException(ReadIssue issue, string message, Exception? innerException = null, int? nativeCode = null)
    : IOException(message, innerException)
{
    public ReadIssue Issue { get; } = issue;
    public int? NativeCode { get; } = nativeCode;
}
