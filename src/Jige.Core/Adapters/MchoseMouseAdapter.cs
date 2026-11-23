using System.Buffers.Binary;

namespace Jige.Core.Adapters;

internal static class MchoseMouseProtocol
{
    internal const ushort FirstVendor = 0x5253;
    internal const ushort SecondVendor = 0x3837;

    // mchose-linux 和 HaloBattery（MIT）记录的只读状态命令 06。
    private static readonly byte[] ShortRequest = EncodeStatus(0x11, 21);
    private static readonly byte[] LongRequest = EncodeStatus(0x12, 65);

    public static byte ReportId(ushort? length) => length switch { 21 => 0x11, 65 => 0x12, _ => 0 };

    private static byte[] EncodeStatus(byte reportId, int length)
    {
        var frame = new byte[length];
        Array.Fill(frame, (byte)0xFF);
        frame[0] = reportId;
        frame[1] = 0xF9;
        return frame;
    }

    public static byte[] Request(ushort length) => length switch
    {
        21 => [.. ShortRequest],
        65 => [.. LongRequest],
        _ => throw new ArgumentOutOfRangeException(nameof(length))
    };

    public static bool Allows(ReadOnlySpan<byte> request) =>
        request.SequenceEqual(ShortRequest) || request.SequenceEqual(LongRequest);

    public static bool TryDecode(ReadOnlySpan<byte> frame, byte expectedId, out int level, out ChargingState charging)
    {
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length is not (21 or 65) || ReportId((ushort)frame.Length) != expectedId ||
            frame[0] != expectedId || frame[1] != 0xF9)
            return false;

        // 全零占位和请求回显没有有效 VID。这里读取的是鼠标机身的 VID，PID 可以与接收器不同。
        ushort vendor = (ushort)~BinaryPrimitives.ReadUInt16LittleEndian(frame[2..4]);
        int percentage = frame[11] ^ 0xFF;
        if (vendor is not (FirstVendor or SecondVendor) || percentage > 100)
            return false;

        level = percentage;
        charging = MouseBatteryProtocol.Charge((byte)(frame[12] ^ 0xFF));
        return true;
    }

    public static bool IsIdleReply(ReadOnlySpan<byte> frame) =>
        Allows(frame) || !frame.ContainsAnyExcept((byte)0) ||
        (frame.Length >= 2 && frame[1] is 0 or 0x06 or 0xF9 && !frame[2..].ContainsAnyExcept((byte)0)) ||
        (frame.Length >= 2 && frame[1] == 0xF9 && !frame[2..].ContainsAnyExcept((byte)0xFF));
}

internal sealed class MchoseMouseAdapter(SafetyGateStore? gates = null, TimeSpan? replyDelay = null) : IDeviceAdapter
{
    // 每轮 SET 之后等接收器准备回包；测试传 0，避免依赖 CI 机器的计时。
    private readonly TimeSpan replyWait = replyDelay ?? TimeSpan.FromMilliseconds(120);

    internal const string Prefix = "mchose-mouse:";
    internal const string Family = "MCHOSE 反码 Feature";

    internal static IReadOnlyList<DeviceCatalogEntry> Models { get; } =
    [
        new(MchoseMouseProtocol.FirstVendor, 0x1020, "MCHOSE 鼠标", Family, DeviceKind.Mouse,
            VerificationTier.DocsOnly, Prefix, "mchose-linux / HaloBattery（MIT）"),
        new(MchoseMouseProtocol.SecondVendor, 0x100B, "MCHOSE A7 V2 Ultra", Family, DeviceKind.Mouse,
            VerificationTier.DocsOnly, Prefix, "HaloBattery #4 描述符 / mchose-linux（MIT）")
    ];

    internal static DeviceCatalogEntry? Find(ushort? vid, ushort? pid) =>
        Models.FirstOrDefault(model => model.VendorId == vid && model.ProductId == pid);

    private static DeviceCatalogEntry? Match(HidInterface hid)
    {
        if (hid is not { UsagePage: 0xFF01, Usage: 1, FeatureLength: 21 or 65, MetadataIssue: ReadIssue.None } ||
            hid.VendorId == MchoseMouseProtocol.SecondVendor && hid.InterfaceNumber != 2 ||
            DeviceCatalog.IsDisabledPath(hid, out _))
            return null;
        return Find(hid.VendorId, hid.ProductId);
    }

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        var candidates = inventory.Hid
            .Select(hid => (Hid: hid, Model: Match(hid)))
            .Where(candidate => candidate.Model is not null);
        foreach (var group in candidates.GroupBy(candidate => candidate.Hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var matches = group.Take(2).ToArray();
            var (endpoint, model) = matches[0];
            if (matches.Length != 1)
            {
                gates?.RecordRejection(endpoint, 1, "迈从鼠标命中多个候选 Feature 集合");
                continue;
            }
            if (gates?.IsBlocked(endpoint, out _) == true)
                continue;

            yield return new(new(Prefix + endpoint.ReceiverId, model!.Model, DeviceKind.Mouse,
                AdaptationSource.Catalog, model.Verification)
            {
                SourceLabel = "迈从无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || Match(endpoint) is null)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "迈从鼠标控制集合不匹配");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        ushort length = endpoint.FeatureLength!.Value;
        byte reportId = MchoseMouseProtocol.ReportId(length);
        var request = MchoseMouseProtocol.Request(length);
        if (!MchoseMouseProtocol.Allows(request))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");

        using var session = transport.OpenHid(endpoint with { FeatureOnly = true, RequiredFeatureReportId = reportId }, token);
        var reply = new byte[length];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(1200); // 最多 4 轮，每轮先等 120 ms；给慢速接收器和 CI 留余量。
        bool malformedReply = false;
        TransportException? lastReadError = null;
        try
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await session.WriteFeatureAsync(request, deadline.Token).ConfigureAwait(false);
                trace?.Invoke(FrameDirection.Sent, request);
                if (replyWait > TimeSpan.Zero)
                    await Task.Delay(replyWait, deadline.Token).ConfigureAwait(false);
                Array.Clear(reply);
                reply[0] = reportId;
                try
                {
                    await session.ReadFeatureAsync(reply, deadline.Token).ConfigureAwait(false);
                    lastReadError = null;
                }
                catch (TransportException error) when (error.Issue == ReadIssue.IoError)
                {
                    // HaloBattery 的实机记录中，接收器有读错误时仍可在下一轮返回新状态。
                    lastReadError = error;
                    continue;
                }
                trace?.Invoke(FrameDirection.Received, reply);
                if (MchoseMouseProtocol.TryDecode(reply, reportId, out int level, out var charging))
                    return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);

                malformedReply |= !MchoseMouseProtocol.IsIdleReply(reply);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // 接收器静默时保留上次读数，并按休眠处理。
        }
        catch (TransportException error) when (error.Issue == ReadIssue.Timeout)
        {
        }

        if (malformedReply)
            return AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "迈从鼠标未返回有效电量");
        if (lastReadError is not null)
            return AdapterRegistry.Failure(target, lastReadError.Issue, lastReadError.Message);
        return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
            DateTimeOffset.UtcNow, ReadIssue.Timeout, "迈从接收器未返回新读数，鼠标可能正在休眠");
    }
}
