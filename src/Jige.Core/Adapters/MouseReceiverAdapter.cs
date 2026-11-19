namespace Jige.Core.Adapters;

internal sealed class MouseReceiverAdapter : IDeviceAdapter
{
    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid
            .Where(hid => MouseReceiverCatalog.IsListed(hid.VendorId, hid.ProductId))
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var matches = group.SelectMany(hid => MouseReceiverCatalog.Candidates(hid.VendorId, hid.ProductId)
                .Where(row => row.Matches(hid)).Select(row => (Hid: hid, Row: row))).ToArray();
            var active = matches.Where(match => match.Row.Enabled && match.Row.Shape?.Usage is not null).ToArray();
            if (active.Length != 1 || matches.Select(match => (match.Hid, match.Row.Family)).Distinct().Count() != 1)
                continue;
            var (endpoint, row) = active[0];
            yield return new(new($"mouse:{group.Key}", row.Model, DeviceKind.Mouse), endpoint, "0");
        }
    }

    internal static MouseReceiver? AllowedRoute(HidInterface endpoint)
    {
        var rows = MouseReceiverCatalog.Candidates(endpoint.VendorId, endpoint.ProductId)
            .Where(row => row.Matches(endpoint)).ToArray();
        if (rows.Select(row => row.Family).Distinct().Count() != 1)
            return null;
        var active = rows.Where(row => row.Enabled && row.Shape?.Usage is not null).Take(2).ToArray();
        return active.Length == 1 ? active[0] : null;
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken cancellationToken)
    {
        if (target.Endpoint is not HidInterface endpoint || AllowedRoute(endpoint) is not { } route)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "接收器未通过只读白名单");

        using var session = transport.OpenHid(endpoint, cancellationToken);
        var result = await QueryAsync(session, endpoint, route, false, trace, cancellationToken).ConfigureAwait(false);
        if (result.Asleep)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow);
        if (result.Level is null)
            return AdapterRegistry.Failure(target, result.Issue, "接收器未返回有效电量");

        var charging = result.Charging;
        if (route.Family == MouseFamily.BitMouse)
        {
            var power = await QueryAsync(session, endpoint, route, true, trace, cancellationToken).ConfigureAwait(false);
            charging = power.Charging;
        }
        return new(target.Device, result.Level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private readonly record struct Reply(
        int? Level, ChargingState Charging, ReadIssue Issue = ReadIssue.None, bool Asleep = false);

    private static async ValueTask<Reply> QueryAsync(IHidSession session, HidInterface endpoint, MouseReceiver route,
        bool charging, FrameTrace? trace, CancellationToken cancellationToken)
    {
        var shape = route.Shape!;
        var request = MouseBatteryProtocol.Request(route.Family, charging);
        var report = new byte[shape.Length];
        if (!route.Enabled || shape.Usage is null || !route.Matches(endpoint) ||
            request.Length != shape.Length || request[0] != shape.ReportId || !MouseBatteryProtocol.Allows(route.Family, request))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(500);
        var token = deadline.Token;
        bool sawReply = false;
        Task<int>? pending = null;
        try
        {
            // Compx 的中断回复可能先于写入完成，先挂起一条输入读取。
            if (route.Family is MouseFamily.CompxOut or MouseFamily.CompxFeature)
            {
                pending = session.ReadInputAsync(report, token).AsTask();
                if (pending.IsFaulted || pending.IsCanceled)
                    await pending.ConfigureAwait(false);
            }

            if (shape.Channel == MouseChannel.OutputInput)
                await session.WriteOutputAsync(request, token).ConfigureAwait(false);
            else
                await session.WriteFeatureAsync(request, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);

            if (shape.Channel == MouseChannel.Feature)
                await Task.Delay(100, token).ConfigureAwait(false);

            for (int attempt = 0; attempt < 8; attempt++)
            {
                token.ThrowIfCancellationRequested();
                int count = report.Length;
                if (shape.Channel == MouseChannel.Feature)
                {
                    report[0] = shape.ReportId;
                    await session.ReadFeatureAsync(report, token).ConfigureAwait(false);
                }
                else
                {
                    count = pending is null
                        ? await session.ReadInputAsync(report, token).ConfigureAwait(false)
                        : await pending.ConfigureAwait(false);
                    pending = null;
                }
                sawReply = true;
                trace?.Invoke(FrameDirection.Received, report.AsSpan(0, count));
                var reply = Decode(report.AsSpan(0, count), route, charging);
                if (reply.Level.HasValue || reply.Asleep)
                    return reply;
                if (shape.Channel == MouseChannel.Feature)
                    await Task.Delay(25, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 子查询超时。外层取消继续由注册表处理。
        }
        finally
        {
            if (pending is not null)
            {
                deadline.Cancel();
                await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        return new(null, ChargingState.Unknown, sawReply ? ReadIssue.InvalidResponse : ReadIssue.Timeout);
    }

    private static Reply Decode(ReadOnlySpan<byte> bytes, MouseReceiver route, bool queryCharging)
    {
        switch (route.Family)
        {
            case MouseFamily.CompxOut or MouseFamily.CompxFeature:
                if (MouseBatteryProtocol.TryCompx(bytes, out var compxLevel, out var compxCharging))
                    return new(compxLevel, compxCharging);
                break;
            case MouseFamily.BitMouse:
                if (MouseBatteryProtocol.TryBitMouse(bytes, route.BitReply, queryCharging, out byte value))
                    return new(value, queryCharging ? MouseBatteryProtocol.Charge(value) : ChargingState.Unknown);
                break;
            case MouseFamily.XviOld or MouseFamily.XviNew:
                if (MouseBatteryProtocol.TryXvi(bytes, route.Family, out var xviLevel, out var xviCharging))
                    return new(xviLevel, xviCharging);
                if (MouseBatteryProtocol.IsXviAsleep(bytes, route.Family))
                    return new(null, ChargingState.Unknown, Asleep: true);
                break;
        }
        return default;
    }
}
