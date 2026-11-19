namespace Jige.Core.Adapters;

public sealed record DetectedDevice(IDeviceAdapter Adapter, DeviceTarget Target);

public sealed class AdapterRegistry
{
    private sealed class ReceiverLock
    {
        public readonly SemaphoreSlim Gate = new(1);
        public int References;
    }

    private static readonly Dictionary<string, ReceiverLock> Receivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDeviceAdapter[] adapters;

    public AdapterRegistry(params IDeviceAdapter[] adapters) => this.adapters = [.. adapters];

    public static AdapterRegistry CreateDefault(int sonyBaudRate = 115200) =>
        new(new FinalmouseAdapter(), new SonyInzoneAdapter(sonyBaudRate));

    public IEnumerable<DetectedDevice> Detect(DeviceInventory inventory) =>
        adapters.SelectMany(adapter => adapter.Detect(inventory).Select(target => new DetectedDevice(adapter, target)));

    public async Task<BatteryReading> ReadAsync(
        DetectedDevice device, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken)
    {
        try
        {
            return await OperationDeadline.RunAsync(token => ReadLockedAsync(device, transport, trace, token),
                TimeSpan.FromSeconds(7), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Failure(device.Target, ReadIssue.Timeout, "接收器查询超时");
        }
        catch (TransportException error)
        {
            return Failure(device.Target, error.Issue, error.Message);
        }
    }

    internal static BatteryReading Failure(DeviceTarget target, ReadIssue issue, string detail)
    {
        var link = issue switch
        {
            ReadIssue.Busy => LinkState.Busy,
            ReadIssue.Disconnected => LinkState.Offline,
            _ => LinkState.Unknown
        };
        return new(target.Device, null, false, ChargingState.Unknown, link, DateTimeOffset.UtcNow, issue, detail);
    }

    private static async Task<BatteryReading> ReadLockedAsync(
        DetectedDevice device, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var id = device.Target.Endpoint.ReceiverId;
        ReceiverLock receiver;
        lock (Receivers)
        {
            if (!Receivers.TryGetValue(id, out receiver!))
                Receivers.Add(id, receiver = new());
            receiver.References++;
        }

        try
        {
            await receiver.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                return await device.Adapter.ReadAsync(device.Target, transport, trace, token).ConfigureAwait(false);
            }
            finally
            {
                receiver.Gate.Release();
            }
        }
        finally
        {
            lock (Receivers)
            {
                if (--receiver.References == 0)
                {
                    Receivers.Remove(id);
                    receiver.Gate.Dispose();
                }
            }
        }
    }
}
