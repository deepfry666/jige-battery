namespace Jige.Core.Adapters;

public sealed record DetectedDevice(IDeviceAdapter Adapter, DeviceTarget Target)
{
    public PresenceProbeKind PresenceKind =>
        Adapter is IPresenceAdapter presence ? presence.PresenceKind : PresenceProbeKind.FullPollOnly;
}

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

    public static AdapterRegistry CreateDefault(
        int sonyBaudRate = 115200,
        SafetyGateStore? gates = null,
        Func<bool>? isInzoneHubRunning = null) =>
        new(new FinalmouseAdapter(gates), new SonyInzoneAdapter(sonyBaudRate, isInzoneHubRunning),
            new MouseReceiverAdapter(gates), new LogitechHidppAdapter(), new LogitechHeadsetAdapter(),
            new RazerFeatureAdapter(), new RazerHeadsetAdapter());

    internal IReadOnlyList<IDeviceAdapter> Adapters => adapters;

    /// <summary>只为支持的接收器家族或已收录接收器读取 HID 描述符；托盘、探针、诊断共用这一判定。</summary>
    public static bool IsKnownHidReceiver(ushort? vid, ushort? pid) =>
        vid == 0x361D || vid == 0x373B || MouseReceiverCatalog.IsListed(vid, pid) || vid == 0x046D ||
        vid == 0x1532 && (RazerFeatureProtocol.Find(pid) is not null || pid == 0x0565);

    public IEnumerable<DetectedDevice> Detect(DeviceInventory inventory) =>
        adapters.SelectMany(adapter => adapter.Detect(inventory)
            .Where(target => !DeviceCatalog.IsDisabledPath(target.Endpoint, out _))
            .Select(target => new DetectedDevice(adapter, target)));

    public Task<BatteryReading> ReadAsync(
        DetectedDevice device, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken)
    {
        // B3：登记在路径开关表中的设备在打开句柄之前即跳过。
        if (DeviceCatalog.IsDisabledPath(device.Target.Endpoint, out var disabled))
            return Task.FromResult(Failure(device.Target, ReadIssue.IoError, $"已识别，暂未启用：{disabled!.Reason}"));
        return ReadWithDeadlineAsync(device, token => device.Adapter.ReadAsync(device.Target, transport, trace, token),
            issue => issue, cancellationToken);
    }

    public Task<IReadOnlyList<BatteryReading>> ReadDevicesAsync(
        DetectedDevice device, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken)
    {
        if (DeviceCatalog.IsDisabledPath(device.Target.Endpoint, out var disabled))
            return Task.FromResult<IReadOnlyList<BatteryReading>>(
                [Failure(device.Target, ReadIssue.IoError, $"已识别，暂未启用：{disabled!.Reason}")]);
        return device.Adapter is IMultiDeviceAdapter multi
            ? ReadWithDeadlineAsync(device, token => multi.ReadDevicesAsync(device.Target, transport, trace, token),
                issue => [issue], cancellationToken)
            : ReadSingleAsync(device, transport, trace, cancellationToken);
    }

    public async Task<PresenceUpdate?> CheckPresenceAsync(
        DetectedDevice device, IReceiverTransport transport, CancellationToken cancellationToken)
    {
        if (DeviceCatalog.IsDisabledPath(device.Target.Endpoint, out _))
            return null;
        if (device.Adapter is not IPresenceAdapter presence || presence.PresenceKind != PresenceProbeKind.Heartbeat)
            return null;
        try
        {
            return await OperationDeadline.RunAsync(
                token => ReadLockedAsync(device.Target.Endpoint.ReceiverId,
                    inner => presence.CheckPresenceAsync(device.Target, transport, inner), token),
                TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // R1：查询超时一律按休眠处理，仅协议明确返回未连接或接收器拔出才算离线。
            return new(device.Target.Endpoint.ReceiverId, null, device.Target.Device.Id, LinkState.Asleep, ReadIssue.Timeout);
        }
        catch (TransportException error)
        {
            var link = error.Issue switch
            {
                ReadIssue.Busy => LinkState.Busy,
                ReadIssue.Disconnected => LinkState.Offline,
                ReadIssue.Timeout => LinkState.Asleep,
                _ => LinkState.Unknown
            };
            return new(device.Target.Endpoint.ReceiverId, null, device.Target.Device.Id, link, error.Issue);
        }
    }

    public Task ListenPushAsync(
        DetectedDevice device, IReceiverTransport transport, Action<PresenceUpdate> onUpdate, CancellationToken cancellationToken) =>
        device.Adapter is IPushPresenceAdapter push
            ? push.ListenPushAsync(device.Target, transport, onUpdate, cancellationToken)
            : Task.CompletedTask;

    private async Task<IReadOnlyList<BatteryReading>> ReadSingleAsync(
        DetectedDevice device, IReceiverTransport transport, FrameTrace? trace, CancellationToken token) =>
        [await ReadAsync(device, transport, trace, token).ConfigureAwait(false)];

    private static async Task<T> ReadWithDeadlineAsync<T>(DetectedDevice device,
        Func<CancellationToken, ValueTask<T>> read, Func<BatteryReading, T> failure, CancellationToken cancellationToken)
    {
        try
        {
            return await OperationDeadline.RunAsync(token => ReadLockedAsync(device.Target.Endpoint.ReceiverId, read, token),
                TimeSpan.FromSeconds(7), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return failure(Failure(device.Target, ReadIssue.Timeout, "接收器查询超时"));
        }
        catch (TransportException error)
        {
            return failure(Failure(device.Target, error.Issue, error.Message));
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

    private static async Task<T> ReadLockedAsync<T>(
        string id, Func<CancellationToken, ValueTask<T>> read, CancellationToken token)
    {
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
                return await read(token).ConfigureAwait(false);
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
