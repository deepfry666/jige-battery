using System.Diagnostics;
using Jige.Core.Windows;

namespace Jige.Core.Adapters;

internal sealed class SonyInzoneAdapter : IDeviceAdapter, IPresenceAdapter
{
    private const string HardwareIdPrefix = @"USB\VID_054C&PID_0E53&";
    private const uint SnapProcessFlag = 0x00000002;
    internal const int BackoffThreshold = 2;
    internal static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    private static readonly string[] InzoneHubProcessNames = ["INZONEHub", "INZONE Hub", "INZONEHubService"];
    private readonly int baudRate;
    private readonly Func<bool> isInzoneHubRunning;
    private readonly TimeProvider clock;
    private readonly TimeSpan settleDelay;
    private readonly TimeSpan? queryTimeoutOverride;
    private readonly Lock backoffLock = new();
    private readonly Dictionary<string, (int Timeouts, long LastTimeoutStamp)> backoff = new(StringComparer.OrdinalIgnoreCase);

    public PresenceProbeKind PresenceKind => PresenceProbeKind.FullPollOnly;

    public SonyInzoneAdapter(int baudRate, Func<bool>? isInzoneHubRunning = null, TimeProvider? clock = null,
        TimeSpan? settleDelay = null, TimeSpan? queryTimeout = null)
    {
        if (baudRate is not (115200 or 460800))
            throw new ArgumentOutOfRangeException(nameof(baudRate));
        this.baudRate = baudRate;
        this.isInzoneHubRunning = isInzoneHubRunning ?? DefaultInzoneHubCheck;
        this.clock = clock ?? TimeProvider.System;
        this.settleDelay = settleDelay ?? TimeSpan.FromMilliseconds(500);
        queryTimeoutOverride = queryTimeout;
    }

    public void ResetBackoff()
    {
        lock (backoffLock)
            backoff.Clear();
    }

    internal static bool MatchesProcessName(ReadOnlySpan<char> exeFile, ReadOnlySpan<string> targetNames)
    {
        if (exeFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exeFile = exeFile[..^4];
        foreach (string name in targetNames)
        {
            if (exeFile.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static unsafe bool DefaultInzoneHubCheck()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        // 单次 Toolhelp32 进程快照（不抓取全系统线程表、不创建托管 Process 对象），替代三次 Process.GetProcessesByName。
        nint snapshot = Native.CreateToolhelp32Snapshot(SnapProcessFlag, 0);
        if (snapshot is 0 or -1)
            return FallbackProcessCheck();
        try
        {
            var entry = new Native.ProcessEntry32 { Size = (uint)sizeof(Native.ProcessEntry32) };
            if (Native.Process32First(snapshot, ref entry) == 0)
                return false;
            do
            {
                var nameSpan = new ReadOnlySpan<char>(entry.ExeFile, 260);
                int length = nameSpan.IndexOf('\0');
                if (length >= 0)
                    nameSpan = nameSpan[..length];
                if (MatchesProcessName(nameSpan, InzoneHubProcessNames))
                    return true;
            }
            while (Native.Process32Next(snapshot, ref entry) != 0);
            return false;
        }
        finally
        {
            Native.CloseHandle(snapshot);
        }
    }

    private static bool FallbackProcessCheck()
    {
        try
        {
            var processes = Process.GetProcesses();
            bool running = false;
            foreach (var process in processes)
            {
                try
                {
                    if (!running && MatchesProcessName(process.ProcessName, InzoneHubProcessNames))
                        running = true;
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return running;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsControlInterface(string id) =>
        id.StartsWith(HardwareIdPrefix, StringComparison.OrdinalIgnoreCase) &&
        id.Split('&').Contains("MI_06", StringComparer.OrdinalIgnoreCase);

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        var targets = new List<DeviceTarget>();
        foreach (var group in inventory.Serial
            .Where(port => port.HardwareIds.Any(IsControlInterface))
            .GroupBy(port => port.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var endpoint = group.First();
            var identity = new DeviceIdentity($"inzone-h9:{group.Key}", "Sony INZONE H9（一代接收器）",
                DeviceKind.Headset, AdaptationSource.Catalog, DeviceCatalog.VerificationFor(0x054C, 0x0E53))
            {
                SourceLabel = "Sony INZONE 接收器"
            };
            targets.Add(new(identity, endpoint, "0"));
        }

        if (inventory.Warnings.Count == 0)
        {
            var live = targets.Select(target => target.Endpoint.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            lock (backoffLock)
            {
                foreach (string path in backoff.Keys.Where(path => !live.Contains(path)).ToArray())
                    backoff.Remove(path);
            }
        }
        return targets;
    }

    // H9 串口为独占访问，不做高频在线心跳，避免频繁打开端口与 INZONE Hub 争抢。
    public ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken cancellationToken) =>
        ValueTask.FromResult<PresenceUpdate?>(null);

    private bool IsBackedOff(string path)
    {
        lock (backoffLock)
        {
            if (!backoff.TryGetValue(path, out var entry) || entry.Timeouts < BackoffThreshold)
                return false;
            var delay = entry.Timeouts == BackoffThreshold ? FirstBackoff : MaxBackoff;
            return clock.GetElapsedTime(entry.LastTimeoutStamp) < delay;
        }
    }

    private void RecordTimeout(string path)
    {
        long now = clock.GetTimestamp();
        lock (backoffLock)
        {
            int next = backoff.TryGetValue(path, out var entry) ? Math.Min(entry.Timeouts + 1, BackoffThreshold + 1) : 1;
            backoff[path] = (next, now);
        }
    }

    private void ClearBackoff(string path)
    {
        lock (backoffLock)
            backoff.Remove(path);
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken)
    {
        if (target.Endpoint is not UsbSerialPort endpoint)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "INZONE H9 接口类型不匹配");
        if (!endpoint.HardwareIds.Any(IsControlInterface))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "未找到 INZONE H9 的 MI_06 串口");

        if (endpoint.Path.Length == 0)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, endpoint.MetadataError ?? "串口设备路径不可用");
        if (DeviceCatalog.IsDisabledPath(endpoint, out var disabled))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"已识别，暂未启用：{disabled!.Reason}");

        // 耳机长期关机时，连续超时后进入退避期，避免每 30 秒做进程快照并阻塞串口 2.75 秒；诊断模式不受退避限制。
        if (trace is null && IsBackedOff(endpoint.Path))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Unknown, clock.GetUtcNow(),
                ReadIssue.None, "耳机持续未响应，退避等待下次查询");

        // INZONE Hub 正在运行时主动退让，不打开独占串口，避免与官方驱动冲突。
        if (isInzoneHubRunning())
        {
            ClearBackoff(endpoint.Path);
            return AdapterRegistry.Failure(target, ReadIssue.Busy, "INZONE Hub 正在运行，已退让避免占用串口");
        }

        using var session = transport.OpenSerial(endpoint, baudRate, cancellationToken);
        var stream = new SonyEventStream(trace);
        var buffer = new byte[256];
        bool malformedBattery = false;
        try
        {
            if (settleDelay > TimeSpan.Zero)
                await Task.Delay(settleDelay, cancellationToken).ConfigureAwait(false);
            await QueryAsync(SonyQuery.Link).ConfigureAwait(false);
            await QueryAsync(SonyQuery.Model).ConfigureAwait(false);
            var battery = await QueryAsync(SonyQuery.Battery).ConfigureAwait(false);
            if (battery is not null && SonyProtocol.TryBattery(battery, out var level, out var charging))
            {
                ClearBackoff(endpoint.Path);
                return new(target.Device, level, false, charging, LinkState.Online, clock.GetUtcNow());
            }

            if (malformedBattery || stream.SawInvalidData || stream.HasPendingBytes)
            {
                ClearBackoff(endpoint.Path);
                return AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "串口回复不完整或电量字段不符合协议");
            }

            if (trace is null)
                RecordTimeout(endpoint.Path);
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
            var queryTimeout = queryTimeoutOverride ?? TimeSpan.FromMilliseconds(query == SonyQuery.Battery ? 1500 : 700);
            deadline.CancelAfter(queryTimeout);
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
