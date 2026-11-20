using System.Diagnostics;

namespace Jige.Core.Adapters;

internal sealed class SonyInzoneAdapter : IDeviceAdapter, IPresenceAdapter
{
    private const string HardwareIdPrefix = @"USB\VID_054C&PID_0E53&";
    private static readonly string[] InzoneHubProcessNames = ["INZONEHub", "INZONE Hub", "INZONEHubService"];
    private readonly int baudRate;
    private readonly Func<bool> isInzoneHubRunning;

    public PresenceProbeKind PresenceKind => PresenceProbeKind.FullPollOnly;

    public SonyInzoneAdapter(int baudRate, Func<bool>? isInzoneHubRunning = null)
    {
        if (baudRate is not (115200 or 460800))
            throw new ArgumentOutOfRangeException(nameof(baudRate));
        this.baudRate = baudRate;
        this.isInzoneHubRunning = isInzoneHubRunning ?? DefaultInzoneHubCheck;
    }

    private static bool DefaultInzoneHubCheck()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        foreach (string name in InzoneHubProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                bool running = processes.Length > 0;
                foreach (var process in processes)
                    process.Dispose();
                if (running)
                    return true;
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
        return false;
    }

    private static bool IsControlInterface(string id) =>
        id.StartsWith(HardwareIdPrefix, StringComparison.OrdinalIgnoreCase) &&
        id.Split('&').Contains("MI_06", StringComparer.OrdinalIgnoreCase);

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Serial
            .Where(port => port.HardwareIds.Any(IsControlInterface))
            .GroupBy(port => port.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var endpoint = group.First();
            var identity = new DeviceIdentity($"inzone-h9:{group.Key}", "Sony INZONE H9（一代接收器）",
                DeviceKind.Headset, AdaptationSource.Catalog, DeviceCatalog.VerificationFor(0x054C, 0x0E53));
            yield return new(identity, endpoint, "0");
        }
    }

    // H9 串口为独占访问，不做高频在线心跳，避免频繁打开端口与 INZONE Hub 争抢。
    public ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken cancellationToken) =>
        ValueTask.FromResult<PresenceUpdate?>(null);

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken)
    {
        var endpoint = (UsbSerialPort)target.Endpoint;
        if (!endpoint.HardwareIds.Any(IsControlInterface))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "未找到 INZONE H9 的 MI_06 串口");

        if (endpoint.Path.Length == 0)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, endpoint.MetadataError ?? "串口设备路径不可用");

        // INZONE Hub 正在运行时主动退让，不打开独占串口，避免与官方驱动冲突。
        if (isInzoneHubRunning())
            return AdapterRegistry.Failure(target, ReadIssue.Busy, "INZONE Hub 正在运行，已退让避免占用串口");

        using var session = transport.OpenSerial(endpoint, baudRate, cancellationToken);
        var stream = new SonyEventStream(trace);
        var buffer = new byte[256];
        bool malformedBattery = false;
        try
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            await QueryAsync(SonyQuery.Link).ConfigureAwait(false);
            await QueryAsync(SonyQuery.Model).ConfigureAwait(false);
            var battery = await QueryAsync(SonyQuery.Battery).ConfigureAwait(false);
            if (battery is not null && SonyProtocol.TryBattery(battery, out var level, out var charging))
                return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);

            if (malformedBattery || stream.SawInvalidData || stream.HasPendingBytes)
                return AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "串口回复不完整或电量字段不符合协议");

            return AdapterRegistry.Failure(target, ReadIssue.Timeout,
                $"串口电量查询超时（{baudRate} 波特），请确认耳机已开机且接收器处于 PC 档");
        }
        finally
        {
            stream.FlushTrace();
        }

        async ValueTask<byte[]?> QueryAsync(SonyQuery query)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(query == SonyQuery.Battery ? 1500 : 700);
            var token = deadline.Token;
            var request = SonyProtocol.Encode(query);
            try
            {
                await session.WriteAsync(request, token).ConfigureAwait(false);
                trace?.Invoke(FrameDirection.Sent, request);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    while (stream.TryRead(out var packet))
                    {
                        if (!SonyProtocol.IsReply(packet, query))
                            continue;
                        if (query != SonyQuery.Battery || SonyProtocol.TryBattery(packet, out _, out _))
                            return packet;
                        malformedBattery = true;
                    }

                    int count = await session.ReadAsync(buffer, token).ConfigureAwait(false);
                    stream.Append(buffer.AsSpan(0, count));
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
    }
}
