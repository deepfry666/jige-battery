namespace Jige.Core.Adapters;

internal sealed record SplitCompxModel(
    ushort VendorId,
    ushort ProductId,
    string Name,
    bool QueryLink,
    bool IsEstimated,
    string Source);

/// <summary>
/// 分集合 Compx 17 字节协议（Feature 08 发于 FF02:0002，Input 09 收于 FF01:0000）。
/// 覆盖 Endgame Gear XM2we（okms/xm2we-linux / HaloBattery MIT）与 DAREU 达尔优 A950（MousePower MIT / HaloBattery MIT）。
/// </summary>
internal static class SplitCompxProtocol
{
    public const int FrameLength = 17;
    public const byte FeatureReportId = 0x08;
    public const byte InputReportId = 0x09;
    public const byte CmdLink = 0x03;
    public const byte CmdBattery = 0x04;

    private static readonly SplitCompxModel[] Models =
    [
        new(0x3367, 0x1961, "Endgame Gear XM2we", QueryLink: true, IsEstimated: true,
            "okms/xm2we-linux / HaloBattery（MIT）"),
        new(0x260D, 0x1074, "DAREU A950", QueryLink: false, IsEstimated: false,
            "MousePower（MIT）/ HaloBattery（MIT）"),
        new(0x260D, 0x1084, "DAREU 鼠标", QueryLink: false, IsEstimated: false,
            "MousePower（MIT）/ HaloBattery（MIT）")
    ];

    public static IReadOnlyList<SplitCompxModel> AllModels => Models;

    public static SplitCompxModel? Find(ushort? vid, ushort? pid) =>
        vid is ushort v && pid is ushort p ? Array.Find(Models, m => m.VendorId == v && m.ProductId == p) : null;

    public static byte[] Encode(byte cmd) => cmd switch
    {
        CmdBattery => MouseBatteryProtocol.CompxRequest(CompxQuery.Battery),
        CmdLink => MouseBatteryProtocol.CompxRequest(CompxQuery.Online),
        _ => throw new ArgumentOutOfRangeException(nameof(cmd))
    };

    public static bool AllowsRequest(ReadOnlySpan<byte> frame) =>
        MouseBatteryProtocol.Allows(MouseFamily.CompxFeature, frame) && frame[1] is CmdBattery or CmdLink;

    public static bool TryDecodeBattery(ReadOnlySpan<byte> frame, out int level, out ChargingState charging)
    {
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length != FrameLength || frame[0] != InputReportId || frame[1] != CmdBattery ||
            frame[2] != 0 || frame[3] != 0 || frame[4] != 0 || frame[5] is < 1 or > 10)
            return false;
        if (MouseBatteryProtocol.CompxChecksum(frame[..16]) != frame[16] || frame[6] > 100)
            return false;
        level = frame[6];
        charging = frame[5] >= 2 ? MouseBatteryProtocol.Charge(frame[7]) : ChargingState.Unknown;
        return true;
    }

    public static bool TryDecodeLink(ReadOnlySpan<byte> frame, out bool online)
    {
        online = false;
        if (frame.Length != FrameLength || frame[0] != InputReportId || frame[1] != CmdLink ||
            frame[2] != 0 || frame[3] != 0 || frame[4] != 0 || frame[5] is < 1 or > 10)
            return false;
        if (MouseBatteryProtocol.CompxChecksum(frame[..16]) != frame[16])
            return false;
        online = frame[6] == 1;
        return true;
    }
}

internal sealed class SplitCompxAdapter(SafetyGateStore? gates = null) : IDeviceAdapter, IPresenceAdapter
{
    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    private static bool IsCommandCollection(HidInterface hid) =>
        !DeviceCatalog.IsDisabledPath(hid, out _) && hid is
        {
            UsagePage: 0xFF02, Usage: 0x0002, FeatureLength: SplitCompxProtocol.FrameLength, MetadataIssue: ReadIssue.None
        } && SplitCompxProtocol.Find(hid.VendorId, hid.ProductId) is not null;

    private static bool IsReplyCollection(HidInterface hid) =>
        hid is { UsagePage: 0xFF01, Usage: 0x0000, InputLength: SplitCompxProtocol.FrameLength, MetadataIssue: ReadIssue.None };

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid
            .Where(hid => SplitCompxProtocol.Find(hid.VendorId, hid.ProductId) is not null)
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var commands = group.Where(IsCommandCollection).Take(2).ToArray();
            var replies = group.Where(IsReplyCollection).Take(2).ToArray();
            if (commands.Length != 1 || replies.Length != 1)
                continue;
            var cmd = commands[0];
            var reply = replies[0];
            if (gates?.IsBlocked(cmd, out _) == true)
                continue;
            var model = SplitCompxProtocol.Find(cmd.VendorId, cmd.ProductId)!;
            var tier = DeviceCatalog.VerificationFor(cmd.VendorId, cmd.ProductId);
            yield return new(new($"split-compx:{cmd.ReceiverId}", model.Name, DeviceKind.Mouse,
                AdaptationSource.Catalog, tier)
            {
                SourceLabel = "无线接收器"
            }, cmd, "0")
            {
                RelatedHids = [reply]
            };
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface cmd || target.RelatedHids is not [var reply] ||
            !IsCommandCollection(cmd) || !IsReplyCollection(reply))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "分集合 Compx 接口不匹配");
        if (gates?.IsBlocked(cmd, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");
        var model = SplitCompxProtocol.Find(cmd.VendorId, cmd.ProductId)!;

        using var inputSession = transport.OpenHid(reply with { InputOnly = true, RequiredInputReportId = SplitCompxProtocol.InputReportId }, token);
        using var featureSession = transport.OpenHid(cmd with { FeatureOnly = true, RequiredFeatureReportId = SplitCompxProtocol.FeatureReportId }, token);

        var battFrame = await QueryAsync(featureSession, inputSession, SplitCompxProtocol.CmdBattery,
            frame => SplitCompxProtocol.TryDecodeBattery(frame, out _, out _), trace, token).ConfigureAwait(false);
        if (battFrame is null || !SplitCompxProtocol.TryDecodeBattery(battFrame, out int level, out var charging))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, ReadIssue.Timeout, "接收器查询超时，鼠标可能正在休眠");

        if (model.QueryLink && charging != ChargingState.Charging)
        {
            var linkFrame = await QueryAsync(featureSession, inputSession, SplitCompxProtocol.CmdLink,
                frame => SplitCompxProtocol.TryDecodeLink(frame, out _), trace, token).ConfigureAwait(false);
            if (linkFrame is not null && SplitCompxProtocol.TryDecodeLink(linkFrame, out bool online) && !online)
                return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                    DateTimeOffset.UtcNow, Detail: "鼠标当前处于休眠状态");
        }
        return new(target.Device, level, model.IsEstimated, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<byte[]?> QueryAsync(
        IHidSession featureSession, IHidSession inputSession, byte cmd,
        Func<byte[], bool> matches, FrameTrace? trace, CancellationToken token)
    {
        var req = SplitCompxProtocol.Encode(cmd);
        if (!SplitCompxProtocol.AllowsRequest(req))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(500);
        var dt = deadline.Token;
        var buffer = new byte[SplitCompxProtocol.FrameLength];
        Task<int>? pending = null;
        try
        {
            pending = inputSession.ReadInputAsync(buffer, dt).AsTask();
            if (pending.IsFaulted || pending.IsCanceled)
                await pending.ConfigureAwait(false);
            await featureSession.WriteFeatureAsync(req, dt).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, req);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                dt.ThrowIfCancellationRequested();
                int count = pending is null
                    ? await inputSession.ReadInputAsync(buffer, dt).ConfigureAwait(false)
                    : await pending.ConfigureAwait(false);
                pending = null;
                var frame = count == buffer.Length ? buffer : buffer[..count];
                trace?.Invoke(FrameDirection.Received, frame);
                if (matches(frame))
                    return frame.ToArray();
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
        }
        finally
        {
            if (pending is not null)
            {
                deadline.Cancel();
                await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        return null;
    }

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || !IsCommandCollection(endpoint) ||
            gates?.IsBlocked(endpoint, out _) == true)
            return null;
        var reading = await ReadAsync(target, transport, null, token).ConfigureAwait(false);
        return new(endpoint.ReceiverId, null, target.Device.Id, reading.Link, reading.Issue)
        {
            Battery = reading.Link == LinkState.Online ? reading : null
        };
    }
}
