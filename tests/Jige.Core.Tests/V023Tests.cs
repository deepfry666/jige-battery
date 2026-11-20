using System.Text.RegularExpressions;
using Jige.Core.Adapters;
using Jige.Core.Diagnostics;
using static Jige.Core.Tests.LogitechBatteryTests;

namespace Jige.Core.Tests;

public sealed class V023Tests
{
    private const string CompxBatteryReq = "0804000000000000000000000000000049";
    private const string CompxOnlineReq = "080300000000000000000000000000004A";

    private static string CompxFrame(byte cmd, params byte[] data)
    {
        var frame = new byte[17];
        frame[0] = 0x08;
        frame[1] = cmd;
        frame[5] = (byte)data.Length;
        data.CopyTo(frame, 6);
        frame[16] = MouseBatteryProtocol.CompxChecksum(frame.AsSpan(0, 16));
        return Convert.ToHexString(frame);
    }

    [Fact]
    public async Task R1_CompxTimeoutReturnsAsleepWhileExplicitDisconnectAndUnplugReturnOffline()
    {
        var compx = new HidInterface("rx-atk", "p-atk", 0x373B, 0x11D9, null, 0xFF02, 2, 17, 17, 0, 1);
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([compx], [], [])));

        // 1) 鼠标静止几秒后 0x03 和 0x04 均无回复超时 → 休眠（Asleep），绝不能判定为离线（Offline）。
        var timeoutTransport = new FakeReceiverTransport((compx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout),
            new(DiagnosticOperation.HidOutput, CompxOnlineReq),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout),
            new(DiagnosticOperation.HidOutput, CompxBatteryReq)
        ]));
        var asleep = await registry.CheckPresenceAsync(device, timeoutTransport, CancellationToken.None);
        Assert.NotNull(asleep);
        Assert.Equal(LinkState.Asleep, asleep!.Link);
        Assert.Equal(ReadIssue.Timeout, asleep.Issue);
        timeoutTransport.AssertComplete();

        // 完整电量查询超时同样归为休眠。
        var readTimeoutTransport = new FakeReceiverTransport((compx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout),
            new(DiagnosticOperation.HidOutput, CompxBatteryReq)
        ]));
        var readAsleep = await registry.ReadAsync(device, readTimeoutTransport, null, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, readAsleep.Link);
        Assert.Equal(ReadIssue.Timeout, readAsleep.Issue);
        readTimeoutTransport.AssertComplete();

        // 2) 接收器明确回复 0x03 在线标志为 0 → 立即离线。
        var explicitOfflineTransport = new FakeReceiverTransport((compx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, CompxFrame(0x03, 0, 0x11, 0x22, 0x33)),
            new(DiagnosticOperation.HidOutput, CompxOnlineReq)
        ]));
        var offline = await registry.CheckPresenceAsync(device, explicitOfflineTransport, CancellationToken.None);
        Assert.NotNull(offline);
        Assert.Equal(LinkState.Offline, offline!.Link);
        Assert.Equal(ReadIssue.None, offline.Issue);
        explicitOfflineTransport.AssertComplete();

        // 3) 接收器拔出（Disconnected）→ 立即离线。
        var unpluggedTransport = new FakeReceiverTransport((compx.Path,
        [
            new(DiagnosticOperation.OpenHid, Issue: ReadIssue.Disconnected)
        ]));
        var unplugged = await registry.CheckPresenceAsync(device, unpluggedTransport, CancellationToken.None);
        Assert.NotNull(unplugged);
        Assert.Equal(LinkState.Offline, unplugged!.Link);
        Assert.Equal(ReadIssue.Disconnected, unplugged.Issue);
        unpluggedTransport.AssertComplete();
    }

    [Fact]
    public async Task R1AndD4_GWolvesUlxAndBitMouseMapTimeoutToAsleepAndUnplugToOffline()
    {
        var registry = AdapterRegistry.CreateDefault();

        // 1) 游狼 XVI-NEW（5617）超时 → 休眠（D4 并入 R1）。
        var xviNew = new HidInterface("gw-new", "p-xvi-new", 0x33E4, 0x5617, "G-Wolves", 0xFFFF, 0, 0, 0, 65, 2);
        var xviNewDevice = Assert.Single(registry.Detect(new([xviNew], [], [])));
        var xviNewTimeout = new FakeReceiverTransport((xviNew.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, MouseAdapterTests.Pad("00000002020083", 65)),
            new(DiagnosticOperation.GetFeature, Issue: ReadIssue.Timeout, Argument: 0)
        ]));
        var xviPresence = await registry.CheckPresenceAsync(xviNewDevice, xviNewTimeout, CancellationToken.None);
        Assert.NotNull(xviPresence);
        Assert.Equal(LinkState.Asleep, xviPresence!.Link);
        Assert.Equal(ReadIssue.Timeout, xviPresence.Issue);
        xviNewTimeout.AssertComplete();

        // 2) 游狼 XVI-OLD（2717）超时 → 休眠。
        var xviOld = xviNew with { ReceiverId = "gw-old", Path = "p-xvi-old", ProductId = 0x2717 };
        var xviOldDevice = Assert.Single(registry.Detect(new([xviOld], [], [])));
        var xviOldTimeout = new FakeReceiverTransport((xviOld.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, MouseAdapterTests.Pad("0000028F01", 65)),
            new(DiagnosticOperation.GetFeature, Issue: ReadIssue.Timeout, Argument: 0)
        ]));
        var xviOldPresence = await registry.CheckPresenceAsync(xviOldDevice, xviOldTimeout, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, xviOldPresence!.Link);
        xviOldTimeout.AssertComplete();

        // 3) Finalmouse ULX（361D:0100）心跳与完整读取超时 → 休眠。
        var ulx = new HidInterface("ulx-rx", "p-ulx", 0x361D, 0x0100, "UltralightX", 0xFF00, 1, 64, 64, 0, 2);
        var ulxDevice = Assert.Single(registry.Detect(new([ulx], [], [])));
        string ulxLinkReq = Convert.ToHexString(FinalmouseProtocol.Encode(UlxQuery.Link));
        var ulxTimeout = new FakeReceiverTransport((ulx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, ulxLinkReq),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
        ]));
        var ulxPresence = await registry.CheckPresenceAsync(ulxDevice, ulxTimeout, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, ulxPresence!.Link);
        Assert.Equal(ReadIssue.Timeout, ulxPresence.Issue);
        ulxTimeout.AssertComplete();

        var ulxReadTimeout = new FakeReceiverTransport((ulx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, ulxLinkReq),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
        ]));
        var ulxReading = await registry.ReadAsync(ulxDevice, ulxReadTimeout, null, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, ulxReading.Link);
        Assert.Equal(ReadIssue.Timeout, ulxReading.Issue);
        ulxReadTimeout.AssertComplete();

        // 4) ATK BitMouse（373B:1155）心跳超时 → 休眠；拔出 → 立即离线。
        var bit = new HidInterface("bit-rx", "p-bit", 0x373B, 0x1155, "ATK ZERO", 0xFF05, 1, 64, 64, 0, 2);
        var bitDevice = Assert.Single(registry.Detect(new([bit], [], [])));
        string bitReq = MouseAdapterTests.Pad("087D720200010701", 64);
        var bitTimeout = new FakeReceiverTransport((bit.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, bitReq),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
        ]));
        var bitPresence = await registry.CheckPresenceAsync(bitDevice, bitTimeout, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, bitPresence!.Link);
        bitTimeout.AssertComplete();

        var bitUnplug = new FakeReceiverTransport((bit.Path,
        [
            new(DiagnosticOperation.OpenHid, Issue: ReadIssue.Disconnected)
        ]));
        var bitOffline = await registry.CheckPresenceAsync(bitDevice, bitUnplug, CancellationToken.None);
        Assert.Equal(LinkState.Offline, bitOffline!.Link);
        bitUnplug.AssertComplete();
    }

    [Fact]
    public async Task A5_DiagnosticReportBeginsWithSanitizationNoticeAndStaysReplayable()
    {
        var htx = new HidInterface("gw", "p1", 0x33E4, 0x5617, "G-Wolves", 0xFFFF, 0, 0, 0, 65, 2);
        var transport = new FakeReceiverTransport((htx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, MouseAdapterTests.Pad("00000002020083", 65)),
            new(DiagnosticOperation.GetFeature, MouseAdapterTests.Pad("00A10002020083003C", 65), Argument: 0)
        ]));
        string report = await DiagnosticReport.GenerateAsync(new([htx], [], []), transport, CancellationToken.None);
        transport.AssertComplete();

        var lines = report.Split(["\r\n", "\n"], StringSplitOptions.None);
        Assert.Equal(DiagnosticReport.Header, lines[0]);
        Assert.Equal(DiagnosticReport.PrivacyNotice, lines[1]);
        Assert.Contains("GitHub issue", lines[1]);
        Assert.Contains("序列号", lines[1]);
        Assert.Contains("实例 ID", lines[1]);
        Assert.Contains("用户名", lines[1]);
        Assert.Contains("路径", lines[1]);
        Assert.Contains("脱敏", lines[1]);
        Assert.Contains("验证=已实机验证", report);

        // 回放解析器仍能完整重放带开头提示与验证分级的新报告。
        var (inventory, replay, baud) = FakeReceiverTransport.FromReport(report);
        var registry = AdapterRegistry.CreateDefault(baud);
        var detected = Assert.Single(registry.Detect(inventory));
        var reading = await registry.ReadAsync(detected, replay, null, CancellationToken.None);
        Assert.Equal(60, reading.Percentage);
        replay.AssertComplete();
    }

    [Fact]
    public void B1_DeviceCatalogClassifiesHardwareVerifiedFamilyInferredAndDocsOnly()
    {
        var verified = DeviceCatalog.HardwareVerified.Select(entry => entry.VidPid).Order().ToArray();
        Assert.Equal(["054C:0E53", "0B05:1934", "1532:0577", "33E4:5617"], verified);

        var inferred = DeviceCatalog.Entries
            .Where(entry => entry.Verification == VerificationTier.FamilyInferred)
            .Select(entry => entry.VidPid).Order().ToArray();
        Assert.Equal(["33E4:3617", "33E4:3817", "33E4:6817"], inferred);

        Assert.Equal(VerificationTier.DocsOnly, DeviceCatalog.VerificationFor(0x373B, 0x11D9));
        Assert.Equal(VerificationTier.DocsOnly, DeviceCatalog.VerificationFor(0x33E4, 0x2717));
        Assert.Equal(VerificationTier.DocsOnly, DeviceCatalog.VerificationFor(0x361D, 0x0100));
        Assert.Equal(VerificationTier.DocsOnly, DeviceCatalog.VerificationFor(0x1532, 0x00BF));
        Assert.Equal(VerificationTier.DocsOnly, DeviceCatalog.GetVerification(
            new("mouse:rx", "G-Wolves HTX Ultra 8K", DeviceKind.Mouse, AdaptationSource.Generic)));
    }

    [Fact]
    public void B2_DocsDevicesAndReadmeVerifiedListsMatchCoreCatalogExactly()
    {
        string root = FindRepoRoot();
        var catalogVerified = DeviceCatalog.HardwareVerified
            .Select(entry => entry.VidPid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string devicesMd = File.ReadAllText(Path.Combine(root, "docs", "devices.md"));
        var devicesVerified = ExtractVerifiedVidPidsFromMarkdownTable(devicesMd);
        Assert.Equal(catalogVerified, devicesVerified);

        string readmeMd = File.ReadAllText(Path.Combine(root, "README.md"));
        var readmeVerified = ExtractVerifiedVidPidsFromMarkdownTable(readmeMd);
        Assert.Equal(catalogVerified, readmeVerified);

        // 同家族推定型号也应在 docs/devices.md 中标为「同家族推定」。
        var devicesInferred = ExtractTierVidPidsFromMarkdownTable(devicesMd, "同家族推定");
        var catalogInferred = DeviceCatalog.Entries
            .Where(entry => entry.Verification == VerificationTier.FamilyInferred)
            .Select(entry => entry.VidPid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(catalogInferred, devicesInferred);
    }

    [Theory]
    [InlineData(0x1532, 0x0555, 0xFF14, 1, 64, 64, 0, 3)] // 雷蛇 PA：BlackShark V2 Pro 2023
    [InlineData(0x1532, 0x0556, 0xFF14, 1, 64, 64, 0, 3)] // 雷蛇 PA：SKU 候选
    [InlineData(0x1532, 0x0557, 0xFF14, 1, 64, 64, 0, 3)] // 雷蛇 PA：SKU 候选
    [InlineData(0x1532, 0x0583, 0xFF14, 1, 64, 64, 0, 3)] // 雷蛇 PA：SKU 候选
    [InlineData(0x1532, 0x053A, 0xFF14, 1, 64, 64, 0, 3)] // 雷蛇 PA：Barracuda Pro
    [InlineData(0x1532, 0x0536, 0xFF14, 1, 64, 64, 0, 3)] // 雷蛇 PA：Barracuda X 2022
    [InlineData(0x1532, 0x0552, 0xFF00, 1, 64, 64, 0, 3)] // 雷蛇 Airoha RACE：Barracuda X 2022 Macronix
    [InlineData(0x1532, 0x052E, 0xFF00, 1, 0, 0, 64, 3)]  // 雷蛇 BlackShark V2 Pro 2020 充电线
    [InlineData(0x373B, 0x111A, 0xFF04, 1, 17, 0, 17, 1)] // ATK A9 Ultra
    [InlineData(0x33E4, 0x3854, 0xFF02, 2, 17, 17, 0, 1)] // 游狼 Receiver RS
    [InlineData(0x33E4, 0x3517, 0xFFFF, 0, 0, 0, 65, 2)]  // 游狼 Fenrir Asym
    public async Task B3_DisabledPathSkipsBeforeOpeningHandleAndExplainsReasonInDiagnostics(
        int vid, int pid, int usagePage, int usage, int inLen, int outLen, int featLen, int iface)
    {
        var hid = new HidInterface("rx-disabled", "p-disabled", (ushort)vid, (ushort)pid, null,
            (ushort)usagePage, (ushort)usage, (ushort)inLen, (ushort)outLen, (ushort)featLen, (byte)iface);

        Assert.True(DeviceCatalog.IsDisabledPath(hid, out var entry));
        Assert.NotNull(entry);
        Assert.False(string.IsNullOrWhiteSpace(entry!.Reason));
        Assert.False(string.IsNullOrWhiteSpace(entry.Source));

        var registry = AdapterRegistry.CreateDefault();
        Assert.Empty(registry.Detect(new([hid], [], [])));

        // 即便绕过 Detect 直接构造目标调用 ReadAsync / ReadDevicesAsync / CheckPresenceAsync，也不能打开任何句柄。
        var zeroTransport = new FakeReceiverTransport();
        var directTarget = new DeviceTarget(new("disabled:1", entry.Model, DeviceKind.Mouse), hid, "0");
        var directDevice = new DetectedDevice(new MouseReceiverAdapter(), directTarget);

        var read = await registry.ReadAsync(directDevice, zeroTransport, null, CancellationToken.None);
        Assert.Equal(ReadIssue.IoError, read.Issue);
        Assert.Contains("已识别，暂未启用", read.Detail);
        Assert.Null(await registry.CheckPresenceAsync(directDevice, zeroTransport, CancellationToken.None));

        // 诊断报告必须列出「已识别，暂未启用：原因」，且全程不打开该设备句柄。
        string report = await DiagnosticReport.GenerateAsync(new([hid], [], []), zeroTransport, CancellationToken.None);
        Assert.Contains($"已识别，暂未启用：{entry.Reason}", report);
        Assert.Equal(0, zeroTransport.Calls);
    }

    [Fact]
    public async Task D4_RazerBusyRetriesUpToThreeTimesAndRecoversWhenDeviceReady()
    {
        var mouse = new HidInterface("razer-1", "razer-feature", 0x1532, 0x00BF,
            "DeathAdder V4 Pro", 0xFF00, 1, 0, 0, 91, 0);
        int batteryAttempts = 0;
        int chargingAttempts = 0;
        var session = new BatteryHidStub(request =>
        {
            if (request[8] == 0x80)
            {
                batteryAttempts++;
                // 前 3 次返回协议忙（01），第 4 次（第 3 次重试）返回 67%（0xAB）。
                var reply = batteryAttempts <= 3
                    ? Frame("00011F0000000207800000", 91)
                    : Frame("00021F00000002078000AB", 91);
                reply[89] = RazerFeatureProtocol.Checksum(reply);
                return [reply];
            }
            chargingAttempts++;
            // 充电查询前 2 次返回忙，第 3 次返回充电中（01）。
            var charge = chargingAttempts <= 2
                ? Frame("00011F0000000207840000", 91)
                : Frame("00021F0000000207840001", 91);
            charge[89] = RazerFeatureProtocol.Checksum(charge);
            return [charge];
        }, feature: true);

        var adapter = new RazerFeatureAdapter(TimeSpan.Zero, TimeSpan.Zero);
        var registry = new AdapterRegistry(adapter);
        var target = Assert.Single(registry.Detect(new([mouse], [], [])));
        var reading = await registry.ReadAsync(target, new BatteryTransportStub(_ => session), null, CancellationToken.None);

        Assert.Equal(4, batteryAttempts);
        Assert.Equal(3, chargingAttempts);
        Assert.Equal(67, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Equal(ReadIssue.None, reading.Issue);
    }

    [Fact]
    public async Task D4_RazerPersistentBusyStopsAfterThreeRetriesAndNeverReportsOccupied()
    {
        var mouse = new HidInterface("razer-2", "razer-feature", 0x1532, 0x00BF,
            "DeathAdder V4 Pro", 0xFF00, 1, 0, 0, 91, 0);
        int readAttempts = 0;
        var readSession = new BatteryHidStub(_ =>
        {
            readAttempts++;
            var busy = Frame("00011F0000000207800000", 91);
            busy[89] = RazerFeatureProtocol.Checksum(busy);
            return [busy];
        }, feature: true);

        var adapter = new RazerFeatureAdapter(TimeSpan.Zero, TimeSpan.Zero);
        var registry = new AdapterRegistry(adapter);
        var target = Assert.Single(registry.Detect(new([mouse], [], [])));

        var reading = await registry.ReadAsync(target, new BatteryTransportStub(_ => readSession), null, CancellationToken.None);
        // 初始 1 次 + 最多重试 3 次 = 4 次。
        Assert.Equal(1 + RazerFeatureAdapter.MaxBusyRetries, readAttempts);
        Assert.Equal(ReadIssue.Timeout, reading.Issue);
        Assert.NotEqual(ReadIssue.Busy, reading.Issue);
        Assert.NotEqual(LinkState.Busy, reading.Link);

        int hbAttempts = 0;
        var hbSession = new BatteryHidStub(_ =>
        {
            hbAttempts++;
            var busy = Frame("00011F0000000207800000", 91);
            busy[89] = RazerFeatureProtocol.Checksum(busy);
            return [busy];
        }, feature: true);
        var presence = await registry.CheckPresenceAsync(target, new BatteryTransportStub(_ => hbSession), CancellationToken.None);
        Assert.NotNull(presence);
        Assert.Equal(1 + RazerFeatureAdapter.MaxBusyRetries, hbAttempts);
        Assert.NotEqual(LinkState.Busy, presence!.Link);
        Assert.NotEqual(ReadIssue.Busy, presence.Issue);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Jige.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到仓库根目录（Jige.slnx）。");
    }

    private static HashSet<string> ExtractVerifiedVidPidsFromMarkdownTable(string markdown) =>
        ExtractTierVidPidsFromMarkdownTable(markdown, "已实机验证");

    private static HashSet<string> ExtractTierVidPidsFromMarkdownTable(string markdown, string tierLabel)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vidPidRegex = new Regex(@"\b[0-9A-F]{4}:[0-9A-F]{4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (string rawLine in markdown.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith('|'))
                continue;
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            // Markdown 表格行格式：| col1 | 接收器列(cells[2]) | col3 | 验证状态列(cells[4]) | ...
            if (cells.Length < 5)
                continue;
            string statusCell = cells[4];
            if (!statusCell.Contains(tierLabel, StringComparison.Ordinal))
                continue;
            // 从接收器列提取 USB VID:PID，排除厂商 HID UsagePage:Usage（FF00-FFFF）。
            foreach (Match match in vidPidRegex.Matches(cells[2]))
            {
                string pair = match.Value.ToUpperInvariant();
                if (!pair.StartsWith("FF", StringComparison.Ordinal))
                    result.Add(pair);
            }
        }
        return result;
    }
}
