namespace Jige.Core.Adapters;

internal sealed class PassiveMouseAdapter(TimeSpan? readWindow = null, TimeProvider? clock = null)
    : IDeviceAdapter, IPushPresenceAdapter
{
    private const int FrameLimit = 12;
    private readonly TimeSpan readWindow = readWindow ?? TimeSpan.FromMilliseconds(350);
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Lock historyLock = new();
    private readonly Dictionary<string, Queue<PassiveMouseFrame>> history = new(StringComparer.OrdinalIgnoreCase);

    public PresenceProbeKind PresenceKind => PresenceProbeKind.PushListener;

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        var targets = DomesticMouseStatus.Detect(inventory, feature: false);
        var paths = targets.Select(target => target.Endpoint.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (historyLock)
            foreach (string path in history.Keys.Where(path => !paths.Contains(path)).ToArray())
                history.Remove(path);
        return targets;
    }

    public ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token) => ValueTask.FromResult<PresenceUpdate?>(null);

    public async Task ListenPushAsync(
        DeviceTarget target, IReceiverTransport transport, Action<PresenceUpdate> onUpdate, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface hid || DomesticMouseStatus.Match(hid) is not { Feature: false } route)
            return;
        using var session = OpenInput(transport, hid, route, token);
        var buffer = new byte[hid.InputLength!.Value];
        while (!token.IsCancellationRequested)
        {
            int count = await session.ReadInputAsync(buffer, token).ConfigureAwait(false);
            if (count == 0)
                return;
            token.ThrowIfCancellationRequested();
            var frame = buffer.AsSpan(0, count);
            var status = route.Decode(frame);
            Remember(hid.Path, route, frame, status is not null);
            if (status is { } value)
            {
                var reading = Reading(target, value);
                onUpdate(new(hid.ReceiverId, null, reading.Device.Id, LinkState.Online) { Battery = reading });
            }
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface hid || DomesticMouseStatus.Match(hid) is not { Feature: false } route)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "未匹配鼠标电量输入集合");

        token.ThrowIfCancellationRequested();
        if (readWindow != TimeSpan.Zero)
        {
            using var deadline = new CancellationTokenSource(readWindow, clock);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            try
            {
                using var session = OpenInput(transport, hid, route, window.Token);
                var buffer = new byte[hid.InputLength!.Value];
                while (!window.IsCancellationRequested)
                {
                    int count = await session.ReadInputAsync(buffer, window.Token).ConfigureAwait(false);
                    if (count == 0)
                        break;
                    window.Token.ThrowIfCancellationRequested();
                    var frame = buffer.AsSpan(0, count);
                    trace?.Invoke(FrameDirection.Received, frame);
                    var status = route.Decode(frame);
                    Remember(hid.Path, route, frame, status is not null);
                    if (status is { } value)
                        return Reading(target, value);
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
            {
            }
            catch (TransportException error) when (error.Issue == ReadIssue.Timeout)
            {
            }
        }

        token.ThrowIfCancellationRequested();
        return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
            clock.GetUtcNow(), Detail: "等待鼠标上报电量");
    }

    private static IHidSession OpenInput(IReceiverTransport transport, HidInterface hid, DomesticMouseStatus route,
        CancellationToken token) =>
        transport.OpenHid(hid with { InputOnly = true, RequiredInputReportId = route.ReportId }, token);

    private BatteryReading Reading(DeviceTarget target, MouseBatteryStatus status) =>
        new(status.Name is { } name ? target.Device with { Name = name } : target.Device,
            status.Level, false, status.Charging, LinkState.Online, clock.GetUtcNow());

    private void Remember(string path, DomesticMouseStatus route, ReadOnlySpan<byte> raw, bool valid)
    {
        var frame = new PassiveMouseFrame(DiagnosticFrame(route, raw), valid);
        lock (historyLock)
        {
            if (!history.TryGetValue(path, out var frames))
                history.Add(path, frames = new());
            if (frames.Count == FrameLimit)
                frames.Dequeue();
            frames.Enqueue(frame);
        }
    }

    internal PassiveMouseFrame[] RecentFrames(string path)
    {
        lock (historyLock)
            return history.TryGetValue(path, out var frames) ? frames.ToArray() : [];
    }

    internal static string DiagnosticFrame(DomesticMouseStatus route, ReadOnlySpan<byte> raw)
    {
        byte[] safe = new byte[raw.Length];
        route.CopyDiagnosticFields(raw, safe);
        return Convert.ToHexString(safe);
    }
}

internal sealed record PassiveMouseFrame(string Hex, bool Valid);

internal sealed class RapooVt9Adapter : IDeviceAdapter
{
    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory) => DomesticMouseStatus.Detect(inventory, feature: true);

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface hid || DomesticMouseStatus.Match(hid) is not { Feature: true } route)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "未匹配雷柏 VT9 Pro 只读特征集合");

        using var session = transport.OpenHid(hid with { FeatureOnly = true, RequiredFeatureReportId = route.ReportId }, token);
        var buffer = new byte[hid.FeatureLength!.Value];
        buffer[0] = route.ReportId;
        await session.ReadFeatureAsync(buffer, token).ConfigureAwait(false);
        trace?.Invoke(FrameDirection.Received, buffer);
        return route.Decode(buffer) is { } status
            ? new(target.Device, status.Level, false, status.Charging, LinkState.Online, DateTimeOffset.UtcNow)
            : AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "雷柏 VT9 Pro 返回无效电量报告");
    }
}
