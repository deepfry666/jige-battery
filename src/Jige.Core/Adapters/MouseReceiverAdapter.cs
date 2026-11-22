namespace Jige.Core.Adapters;

internal sealed class MouseReceiverAdapter(SafetyGateStore? gates = null) : IDeviceAdapter, IPresenceAdapter
{
    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid
            .Where(hid => MouseReceiverCatalog.IsVendor(hid.VendorId, hid.ProductId))
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var groupHids = group.ToArray();
            var catalogMatches = groupHids.SelectMany(hid => MouseReceiverCatalog.Candidates(hid.VendorId, hid.ProductId)
                .Where(row => row.Matches(hid)).Select(row => (Hid: hid, Row: row))).ToArray();
            var activeCatalog = catalogMatches.Where(match => match.Row.Enabled && match.Row.Shape?.Usage is not null).ToArray();

            HidInterface? selectedEndpoint = null;
            MouseReceiver? selectedRow = null;

            if (activeCatalog.Length > 0)
            {
                // 目录已启用的精确路由，仍需通过唯一集合和唯一家族校验（安全门 ①）。
                if (activeCatalog.Length != 1 || catalogMatches.Select(match => (match.Hid, match.Row.Family)).Distinct().Count() != 1)
                {
                    gates?.RecordRejection(activeCatalog[0].Hid, 1, "同一接收器命中多个候选集合或协议家族");
                    continue;
                }
                // 同时检查同一接收器的其他集合是否还暴露了不同协议家族特征（例如同时具备 Compx 和 BitMouse 集合）。
                var otherGenericFamilies = groupHids.Where(hid => hid != activeCatalog[0].Hid)
                    .SelectMany(MouseReceiverCatalog.MatchingGenericFamilies)
                    .Where(family => family != activeCatalog[0].Row.Family).Distinct().ToArray();
                if (otherGenericFamilies.Length > 0)
                {
                    gates?.RecordRejection(activeCatalog[0].Hid, 1, "接收器接口特征同时匹配多个协议家族");
                    continue;
                }
                (selectedEndpoint, selectedRow) = activeCatalog[0];
            }
            else if (catalogMatches.Length > 0)
            {
                // 目录里存在明确禁用或冲突条目（如 3517、3854、111A、1216），记录安全门 ① 拦截原因。
                int distinctFamilies = catalogMatches.Select(match => match.Row.Family).Distinct().Count();
                if (distinctFamilies > 1)
                    gates?.RecordRejection(catalogMatches[0].Hid, 1, "该接收器存在多个候选协议家族冲突");
                continue;
            }
            else
            {
                // 通用协议家族识别（A1）：检查该接收器所有厂商集合匹配到的家族总数。
                var genericHits = groupHids.SelectMany(hid => MouseReceiverCatalog.MatchingGenericFamilies(hid)
                    .Select(family => (Hid: hid, Family: family))).ToArray();
                var distinctGeneric = genericHits.Distinct().ToArray();
                if (distinctGeneric.Length == 0)
                    continue;
                if (distinctGeneric.Length > 1 || distinctGeneric.Select(item => item.Family).Distinct().Count() != 1)
                {
                    gates?.RecordRejection(distinctGeneric[0].Hid, 1, "接口特征对应到多个协议家族或集合，禁止发送命令");
                    continue;
                }

                var candidateHid = distinctGeneric[0].Hid;
                var genericRoute = MouseReceiverCatalog.GenericRoute(candidateHid);
                if (genericRoute is null)
                {
                    gates?.RecordRejection(candidateHid, 1, "接口特征无法唯一对应可安全查询的协议家族");
                    continue;
                }
                selectedEndpoint = candidateHid;
                selectedRow = genericRoute;
            }

            // B3：路径开关登记在禁用表中的设备在打开句柄之前即跳过。
            if (DeviceCatalog.IsDisabledPath(selectedEndpoint, out _))
                continue;

            // 安全门 ③：此前回复格式不符合协议家族的接收器不再发送任何命令。
            if (gates?.IsBlocked(selectedEndpoint, out var blockedReason) == true)
            {
                gates.RecordRejection(selectedEndpoint, 3, $"已拦截（{blockedReason}）");
                continue;
            }

            // Compx-OUT 允许被动只读监听同接收器的其他厂商输入集合（如 FF05、FF06），以排查和接收跨集合回复。
            HidInterface[] siblings = selectedRow.Family == MouseFamily.CompxOut && !selectedRow.ExactCollectionOnly
                ? groupHids.Where(hid => hid != selectedEndpoint &&
                    hid.MetadataIssue == ReadIssue.None && hid.UsagePage is >= 0xFF00 && hid.InputLength is > 0).ToArray()
                : [];

            var source = selectedRow.IsGeneric ? AdaptationSource.Generic : AdaptationSource.Catalog;
            var tier = source == AdaptationSource.Generic
                ? VerificationTier.DocsOnly
                : DeviceCatalog.VerificationFor(selectedEndpoint.VendorId, selectedEndpoint.ProductId);
            string sourceLabel = selectedEndpoint.VendorId switch
            {
                0x33E4 => "游狼无线接收器",
                0x373B when !selectedRow.ExactCollectionOnly => "ATK 无线接收器",
                _ => "无线接收器"
            };
            yield return new(new($"mouse:{group.Key}", selectedRow.Model, DeviceKind.Mouse, source, tier)
            {
                SourceLabel = sourceLabel
            },
                selectedEndpoint, "0")
            {
                SiblingInputHids = siblings
            };
        }
    }

    internal static MouseReceiver? AllowedRoute(HidInterface endpoint)
    {
        if (DeviceCatalog.IsDisabledPath(endpoint, out _))
            return null;
        var rows = MouseReceiverCatalog.Candidates(endpoint.VendorId, endpoint.ProductId)
            .Where(row => row.Matches(endpoint)).ToArray();
        if (rows.Length > 0)
        {
            if (rows.Select(row => row.Family).Distinct().Count() != 1)
                return null;
            var active = rows.Where(row => row.Enabled && row.Shape?.Usage is not null).Take(2).ToArray();
            return active.Length == 1 ? active[0] : null;
        }
        return MouseReceiverCatalog.GenericRoute(endpoint);
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken cancellationToken)
    {
        if (target.Endpoint is not HidInterface endpoint)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "接收器未通过只读白名单");
        if (DeviceCatalog.IsDisabledPath(endpoint, out var disabled))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"已识别，暂未启用：{disabled!.Reason}");
        if (AllowedRoute(endpoint) is not { } route)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "接收器未通过只读白名单");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        using var session = transport.OpenHid(QueryEndpoint(endpoint, route), cancellationToken);
        var result = await QueryAsync(session, endpoint, route, false, target.SiblingInputHids, transport, trace, cancellationToken)
            .ConfigureAwait(false);
        if (result.Asleep)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow);
        if (result.Level is null)
        {
            // R1 / D4：Compx、BitMouse、游狼（含 XVI-NEW）查询超时一律按休眠处理，仅协议明确断开或拔出算离线。
            if (result.Issue == ReadIssue.Timeout)
                return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                    DateTimeOffset.UtcNow, ReadIssue.Timeout, "接收器查询超时，设备可能正在休眠");
            // 安全门 ② / ③：首条无害状态查询收到格式不符的回复，立即停止并在通用模式下持久化拦截。
            if (result.Issue == ReadIssue.InvalidResponse && (route.IsGeneric || target.Device.Source == AdaptationSource.Generic))
                gates?.Block(endpoint, "首条状态查询回复格式不符合协议家族");
            return AdapterRegistry.Failure(target, result.Issue, "接收器未返回有效电量");
        }

        var device = target.Device;
        var charging = result.Charging;
        if (route.Family == MouseFamily.BitMouse)
        {
            var power = await QueryAsync(session, endpoint, route, true, [], transport, trace, cancellationToken)
                .ConfigureAwait(false);
            charging = power.Charging;
        }
        else if (route.Family == MouseFamily.CompxOut && route.ResolveModelByCidMid)
        {
            // 仅在首条电量查询已证实协议匹配后，按白名单 0x10 读取 CID/MID 以确定具体鼠标型号。
            var resolved = await QueryCompxModelAsync(session, endpoint, trace, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
                device = device with { Name = resolved };
        }
        return new(device, result.Level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken cancellationToken)
    {
        if (target.Endpoint is not HidInterface endpoint || DeviceCatalog.IsDisabledPath(endpoint, out _) ||
            AllowedRoute(endpoint) is not { } route || gates?.IsBlocked(endpoint, out _) == true)
            return null;

        using var session = transport.OpenHid(QueryEndpoint(endpoint, route), cancellationToken);
        if (route.Family == MouseFamily.CompxOut)
        {
            // R1：优先发轻量的 Compx 0x03 在线查询：
            // 1) 在线标志为 1 → 在线；为 0 → 休眠。实机日志显示鼠标静止十几秒就会回 0，
            //    无法区分省电休眠和关机，所以交给「连续休眠 10 分钟转离线」处理；
            // 2) 无回复超时（如 Compx 静止几秒后不再应答，或固件不支持 0x03）→ 回退 0x04；若仍超时一律算休眠而非离线。
            var online = await QueryCompxOnlineAsync(session, endpoint, cancellationToken).ConfigureAwait(false);
            if (online is bool state)
                return new(endpoint.ReceiverId, null, target.Device.Id, state ? LinkState.Online : LinkState.Asleep);
            var battery = await QueryAsync(session, endpoint, route, false, target.SiblingInputHids, transport, null, cancellationToken)
                .ConfigureAwait(false);
            if (battery.Issue == ReadIssue.InvalidResponse && route.IsGeneric)
                gates?.Block(endpoint, "心跳查询回复格式不符合协议家族");
            var compxLink = battery.Level.HasValue ? LinkState.Online
                : battery.Asleep || battery.Issue == ReadIssue.Timeout ? LinkState.Asleep
                : LinkState.Unknown;
            return new(endpoint.ReceiverId, null, target.Device.Id, compxLink,
                battery.Level.HasValue ? ReadIssue.None : battery.Issue);
        }

        // R1 / D4：BitMouse 与游狼（XVI-OLD / XVI-NEW）查询超时一律算休眠，不误判离线。
        var result = await QueryAsync(session, endpoint, route, false, [], transport, null, cancellationToken)
            .ConfigureAwait(false);
        if (result.Issue == ReadIssue.InvalidResponse && route.IsGeneric)
            gates?.Block(endpoint, "心跳查询回复格式不符合协议家族");
        var link = result.Level.HasValue ? LinkState.Online
            : result.Asleep || result.Issue == ReadIssue.Timeout ? LinkState.Asleep
            : LinkState.Unknown;
        return new(endpoint.ReceiverId, null, target.Device.Id, link, result.Level.HasValue ? ReadIssue.None : result.Issue);
    }

    private static HidInterface QueryEndpoint(HidInterface endpoint, MouseReceiver route)
    {
        if (!route.ExactCollectionOnly)
            return endpoint;
        return route.Shape!.Channel == MouseChannel.Feature
            ? endpoint with { FeatureOnly = true, RequiredFeatureReportId = route.Shape.ReportId }
            : endpoint with { RequiredInputReportId = route.Shape.ReportId, RequiredOutputReportId = route.Shape.ReportId };
    }

    private readonly record struct Reply(
        int? Level, ChargingState Charging, ReadIssue Issue = ReadIssue.None, bool Asleep = false);

    private static async ValueTask<bool?> QueryCompxOnlineAsync(
        IHidSession session, HidInterface endpoint, CancellationToken cancellationToken)
    {
        var request = MouseBatteryProtocol.CompxRequest(CompxQuery.Online);
        if (!MouseBatteryProtocol.Allows(MouseFamily.CompxOut, request) || endpoint.InputLength != 17 || endpoint.OutputLength != 17)
            return null;

        var report = new byte[17];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(300);
        var token = deadline.Token;
        Task<int>? pending = null;
        try
        {
            pending = session.ReadInputAsync(report, token).AsTask();
            if (pending.IsFaulted || pending.IsCanceled)
                await pending.ConfigureAwait(false);
            await session.WriteOutputAsync(request, token).ConfigureAwait(false);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                token.ThrowIfCancellationRequested();
                int count = pending is null
                    ? await session.ReadInputAsync(report, token).ConfigureAwait(false)
                    : await pending.ConfigureAwait(false);
                pending = null;
                if (MouseBatteryProtocol.TryCompxOnline(report.AsSpan(0, count), out bool online))
                    return online;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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

    private static async ValueTask<string?> QueryCompxModelAsync(
        IHidSession session, HidInterface endpoint, FrameTrace? trace, CancellationToken cancellationToken)
    {
        var request = MouseBatteryProtocol.CompxRequest(CompxQuery.CidMid);
        if (!MouseBatteryProtocol.Allows(MouseFamily.CompxOut, request) || endpoint.InputLength != 17 || endpoint.OutputLength != 17)
            return null;

        var report = new byte[17];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(250);
        var token = deadline.Token;
        Task<int>? pending = null;
        try
        {
            pending = session.ReadInputAsync(report, token).AsTask();
            if (pending.IsFaulted || pending.IsCanceled)
                await pending.ConfigureAwait(false);
            await session.WriteOutputAsync(request, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                token.ThrowIfCancellationRequested();
                int count = pending is null
                    ? await session.ReadInputAsync(report, token).ConfigureAwait(false)
                    : await pending.ConfigureAwait(false);
                pending = null;
                trace?.Invoke(FrameDirection.Received, report.AsSpan(0, count));
                if (MouseBatteryProtocol.TryCompxCidMid(report.AsSpan(0, count), out byte cid, out byte mid))
                    return MouseReceiverCatalog.LookupAtkModel(cid, mid);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (TransportException)
        {
            // 型号识别失败不影响已经拿到的电量读数。
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

    private static async ValueTask<Reply> QueryAsync(IHidSession session, HidInterface endpoint, MouseReceiver route,
        bool charging, IReadOnlyList<HidInterface> siblingInputs, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken cancellationToken)
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
        List<(IHidSession Session, byte[] Buffer, Task<int> Read)> siblingListeners = [];
        try
        {
            // Compx 的中断回复可能先于写入完成，先挂起主集合和同接收器其他厂商集合的只读输入监听。
            if (route.Family is MouseFamily.CompxOut or MouseFamily.CompxFeature)
            {
                pending = session.ReadInputAsync(report, token).AsTask();
                if (pending.IsFaulted || pending.IsCanceled)
                    await pending.ConfigureAwait(false);
                if (route.Family == MouseFamily.CompxOut && !route.ExactCollectionOnly && siblingInputs.Count > 0)
                {
                    foreach (var sibling in siblingInputs)
                    {
                        if (sibling.InputLength is not > 0 || sibling.FeatureOnly)
                            continue;
                        try
                        {
                            var siblingSession = transport.OpenHid(sibling with { InputOnly = true }, token);
                            var siblingBuffer = new byte[sibling.InputLength.Value];
                            var read = siblingSession.ReadInputAsync(siblingBuffer, token).AsTask();
                            siblingListeners.Add((siblingSession, siblingBuffer, read));
                        }
                        catch (TransportException)
                        {
                            // 附属集合无法打开或被占用时忽略，不干扰主集合查询。
                        }
                    }
                }
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
                if (shape.Channel == MouseChannel.Feature)
                {
                    report[0] = shape.ReportId;
                    await session.ReadFeatureAsync(report, token).ConfigureAwait(false);
                    sawReply = true;
                    trace?.Invoke(FrameDirection.Received, report);
                    var reply = Decode(report, route, charging);
                    if (reply.Level.HasValue || reply.Asleep)
                        return reply;
                    await Task.Delay(25, token).ConfigureAwait(false);
                    continue;
                }

                if (siblingListeners.Count == 0)
                {
                    int count = pending is null
                        ? await session.ReadInputAsync(report, token).ConfigureAwait(false)
                        : await pending.ConfigureAwait(false);
                    pending = null;
                    var span = report.AsSpan(0, count);
                    trace?.Invoke(FrameDirection.Received, span);
                    if (route.Family == MouseFamily.BitMouse && MouseBatteryProtocol.IsBitMousePush(span))
                        continue;
                    sawReply = true;
                    var reply = Decode(span, route, charging);
                    if (reply.Level.HasValue || reply.Asleep)
                        return reply;
                    continue;
                }

                // 同时等待主集合与附属集合回复，哪一个先到且通过协议校验即采用。
                pending ??= session.ReadInputAsync(report, token).AsTask();
                List<Task<int>> activeReads = [pending];
                foreach (var listener in siblingListeners)
                    activeReads.Add(listener.Read);
                var finished = await Task.WhenAny(activeReads).ConfigureAwait(false);
                if (finished == pending)
                {
                    int count = await pending.ConfigureAwait(false);
                    pending = null;
                    sawReply = true;
                    trace?.Invoke(FrameDirection.Received, report.AsSpan(0, count));
                    var reply = Decode(report.AsSpan(0, count), route, charging);
                    if (reply.Level.HasValue || reply.Asleep)
                        return reply;
                }
                else
                {
                    int idx = siblingListeners.FindIndex(item => item.Read == finished);
                    if (idx < 0)
                        continue;
                    var listener = siblingListeners[idx];
                    try
                    {
                        int count = await listener.Read.ConfigureAwait(false);
                        sawReply = true;
                        trace?.Invoke(FrameDirection.Received, listener.Buffer.AsSpan(0, count));
                        if (MouseBatteryProtocol.TryCompxFromCollection(listener.Buffer.AsSpan(0, count),
                            out int level, out var compxCharging))
                            return new(level, compxCharging);
                        siblingListeners[idx] = (listener.Session, listener.Buffer,
                            listener.Session.ReadInputAsync(listener.Buffer, token).AsTask());
                    }
                    catch (TransportException)
                    {
                        listener.Session.Dispose();
                        siblingListeners.RemoveAt(idx);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 子查询超时。外层取消继续由注册表处理。
        }
        finally
        {
            if (pending is not null || siblingListeners.Count > 0)
            {
                deadline.Cancel();
                if (pending is not null)
                    await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                foreach (var listener in siblingListeners)
                {
                    await ((Task)listener.Read).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    listener.Session.Dispose();
                }
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
