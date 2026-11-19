namespace Jige.Core.Adapters;

public sealed record DeviceTarget(DeviceIdentity Device, ReceiverEndpoint Endpoint, string Slot)
{
    internal HidInterface? ShortHid { get; init; }
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

public sealed class TransportException(ReadIssue issue, string message, Exception? innerException = null)
    : IOException(message, innerException)
{
    public ReadIssue Issue { get; } = issue;
}
