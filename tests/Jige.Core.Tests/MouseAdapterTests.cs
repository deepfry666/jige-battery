using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Core.Tests;

public sealed class MouseAdapterTests
{
    private static readonly HidInterface Atk = new("atk", "atk", 0x373B, 0x1155, "ATK ZERO", 0xFF05, 1, 64, 64, 0, 2);
    private static readonly HidInterface Xvi = new("xvi", "xvi", 0x33E4, 0x3617, "G-Wolves", 0xFFFF, 0, 0, 0, 65, 2);
    internal const string CompxRequest = "0804000000000000000000000000000049";
    internal const string CapturedCompxReply = "08040000000219000E46000000000000DA";

    // Compx 的完整实机帧来自 OpenMouse X1 Pro Max 无线抓包，测试不借此给 X1 Pro Max 猜 PID。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompxFamiliesShareCodecForCapturedBatteryFrame(bool feature)
    {
        Assert.Equal(CompxRequest, Convert.ToHexString(
            MouseBatteryProtocol.Request(feature ? MouseFamily.CompxFeature : MouseFamily.CompxOut)));
        var reply = Convert.FromHexString(CapturedCompxReply);
        Assert.True(MouseBatteryProtocol.TryCompx(reply, out int level, out var charge));
        Assert.Equal(25, level);
        Assert.Equal(ChargingState.NotCharging, charge);
        reply[^1] ^= 1;
        Assert.False(MouseBatteryProtocol.TryCompx(reply, out _, out _));
    }

    [Fact]
    public void CompxFeatureRecognizesRecordedChecksumButRejectsNonBatteryReply()
    {
        // A9 Ultra 接收器的真实 Feature 通道版本回复，来自 atkctl 的实机记录。
        // 电量解码必须拒绝 1D，即使该设备和校验字节均属同一协议。
        byte[] reply = Convert.FromHexString("081D0000000201750000000000000000B8");
        Assert.Equal(0xB8, MouseBatteryProtocol.CompxChecksum(reply.AsSpan(0, 16)));
        Assert.False(MouseBatteryProtocol.TryCompx(reply, out _, out _));
        var shape = Assert.IsType<MouseProtocolShape>(MouseReceiverCatalog.Shape(MouseFamily.CompxFeature, 0x373B));
        Assert.Equal(MouseChannel.FeatureInput, shape.Channel);
        Assert.Equal(17, shape.Length);
        Assert.Null(shape.Usage);
        var query = shape.QueryEndpoint(Atk);
        Assert.Equal((byte)8, query.RequiredFeatureReportId);
        Assert.Equal((byte)8, query.RequiredInputReportId);
        Assert.Null(query.RequiredOutputReportId);
        Assert.False(query.FeatureOnly);
    }

    [Fact]
    public void ChecksumsWrapIndependently()
    {
        Assert.Equal(0x49, MouseBatteryProtocol.CompxChecksum([0x08, 0x04]));
        Assert.Equal(0x57, MouseBatteryProtocol.CompxChecksum([0xFF, 0xFF]));
        Assert.Equal(0x7D, MouseBatteryProtocol.BitMouseChecksum([0x72, 2, 0, 1, 7, 1, 0]));
        Assert.Equal(0x01, MouseBatteryProtocol.BitMouseChecksum([0xFF, 2]));
    }

    [Fact]
    public async Task BitMouseUsesOnlyTwoBatteryFramesAndRejectsAllSingleByteMutations()
    {
        string battery = Pad("087D720200010701", 64);
        string charge = Pad("0885720200010F01", 64);
        foreach (string hex in new[] { battery, charge })
        {
            var request = Convert.FromHexString(hex);
            for (int i = 0; i < request.Length; i++)
            {
                byte original = request[i];
                for (int b = 0; b <= byte.MaxValue; b++)
                {
                    if (b == original)
                        continue;
                    request[i] = (byte)b;
                    Assert.False(MouseBatteryProtocol.Allows(MouseFamily.BitMouse, request));
                }
                request[i] = original;
            }
        }

        Assert.False(MouseBatteryProtocol.Allows(MouseFamily.BitMouse, MouseBatteryProtocol.Request(MouseFamily.XviNew)));
        Assert.Throws<ArgumentOutOfRangeException>(() => MouseBatteryProtocol.Request(MouseFamily.Unknown));
        var transport = new FakeReceiverTransport((Atk.Path,
        [
            new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidOutput, battery),
            new(DiagnosticOperation.HidInput, Pad("087200000107012A", 64)),
            new(DiagnosticOperation.HidOutput, charge), new(DiagnosticOperation.HidInput, Pad("08720000010F0101", 64))
        ]));
        var reading = await Read(Atk, transport);
        Assert.Equal(42, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        transport.AssertComplete();
    }

    [Fact]
    public void BitMouseValidatesChecksumLayoutCommandAndStatus()
    {
        // atkctl 记录的带前置校验布局。电量字节是离线合成值。
        byte[] reply = Convert.FromHexString(Pad("08A57200000107012A", 64));
        Assert.True(MouseBatteryProtocol.TryBitMouse(reply, BitMouseReply.WithChecksum, false, out byte level));
        Assert.Equal(42, level);
        Assert.False(MouseBatteryProtocol.TryBitMouse(reply, BitMouseReply.WithoutChecksum, false, out _));
        for (int index = 1; index <= 6; index++)
        {
            byte[] bad = [.. reply];
            bad[index] = 0xFF;
            Assert.False(MouseBatteryProtocol.TryBitMouse(bad, BitMouseReply.WithChecksum, false, out _));
        }
    }

    [Fact]
    public void BitMouseDecodesRecordedBatteryWithNonEchoedSequenceAndTarget()
    {
        // atkctl 实机样本保留了短记录中的帧尾，Win32 报告的未记录部分补零。
        byte[] frame = Convert.FromHexString(Pad("0872003B000701643011", 64));
        Assert.True(MouseBatteryProtocol.TryBitMouse(frame, BitMouseReply.WithoutChecksum, false, out byte value));
        Assert.Equal(100, value);
        frame[2] = 0x73;
        Assert.False(MouseBatteryProtocol.TryBitMouse(frame, BitMouseReply.WithoutChecksum, false, out _));
        frame[2] = 0xFF;
        Assert.False(MouseBatteryProtocol.TryBitMouse(frame, BitMouseReply.WithoutChecksum, false, out _));
    }

    [Theory]
    [InlineData(0x2717, "0000028F01", "00A1028F01013B", 59, ChargingState.Charging)]
    [InlineData(0x3617, "00000002020083", "00A10002020083003C", 60, ChargingState.NotCharging)]
    public async Task XviFeatureRoutesStaySeparate(int pid, string request, string reply, int expected, ChargingState charging)
    {
        // NEW 的 60% 载荷来自 overlay 实机记录。OLD 的头来自实机验证驱动，59% 为合成值。
        var endpoint = Xvi with { ProductId = (ushort)pid };
        var transport = new FakeReceiverTransport((endpoint.Path,
        [
            new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.SetFeature, Pad(request, 65)),
            new(DiagnosticOperation.GetFeature, Pad(reply, 65), Argument: 0)
        ]));
        var reading = await Read(endpoint, transport);
        Assert.Equal(expected, reading.Percentage);
        Assert.Equal(charging, reading.Charging);
        transport.AssertComplete();
    }

    [Fact]
    public async Task XviOldReportsVerifiedSleepStatusWithoutRetrying()
    {
        var endpoint = Xvi with { ProductId = 0x2717 };
        var transport = new FakeReceiverTransport((endpoint.Path,
        [
            new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.SetFeature, Pad("0000028F01", 65)),
            new(DiagnosticOperation.GetFeature, Pad("0002028F01", 65), Argument: 0)
        ]));
        var reading = await Read(endpoint, transport);
        Assert.Equal(LinkState.Asleep, reading.Link);
        Assert.Null(reading.Percentage);
        transport.AssertComplete();
    }

    [Fact]
    public async Task AmbiguousOrUnknownVendorCannotSendEvenThroughDirectAdapterEntry()
    {
        var transport = new FakeReceiverTransport();
        var registry = AdapterRegistry.CreateDefault();
        var adapter = new MouseReceiverAdapter();
        // 未收录的 33E4 XVI 命中新旧两代家族（安全门 ① 冲突），未知厂商 1234 命中 0 个家族，两者均不得发包。
        foreach (var endpoint in new[] { Xvi with { ProductId = 0x9999 }, Atk with { VendorId = 0x1234, ProductId = 0x9999 } })
        {
            Assert.Empty(registry.Detect(new([endpoint], [], [])));
            var target = new DeviceTarget(new("test", "测试", DeviceKind.Mouse), endpoint, "0");
            var reading = await adapter.ReadAsync(target, transport, null, CancellationToken.None);
            Assert.Equal(ReadIssue.IoError, reading.Issue);
        }
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public void RejectsAmbiguityCandidateAndIncompleteRoute()
    {
        var registry = AdapterRegistry.CreateDefault();
        foreach (var endpoint in new[]
        {
            Atk with { Usage = 2 }, Atk with { VendorId = 0x33E4 }, Atk with { UsagePage = 1 },
            Atk with { OutputLength = 63 }, Xvi with { FeatureLength = 64 }, Xvi with { InterfaceNumber = 3 },
            Xvi with { ProductId = 0x3517 }, Xvi with { ProductId = 0x3854 },
            Atk with { ProductId = 0x111A, UsagePage = 0xFF04, InputLength = 17, FeatureLength = 17 }
        })
            Assert.Empty(registry.Detect(new([endpoint], [], [])));

        Assert.Empty(registry.Detect(new([Atk, Atk with { Path = "duplicate" }], [], [])));
        Assert.Empty(registry.Detect(new([Atk, Xvi with { ReceiverId = Atk.ReceiverId }], [], [])));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Atk1216NkNanoDetectsAlongsideSiblingVendorPagesAndSupportsBothBitMouseLayouts(bool withChecksum)
    {
        // 复现实机诊断报告中的 373B:1216 集合布局（FFB1、FF05、FFB3）。
        var ffb1 = new HidInterface("rx-1216", "p-ffb1", 0x373B, 0x1216, null, 0xFFB1, 1, 0, 13, 0, 2);
        var ff05 = new HidInterface("rx-1216", "p-ff05", 0x373B, 0x1216, null, 0xFF05, 1, 64, 64, 0, 2);
        var ffb3 = new HidInterface("rx-1216", "p-ffb3", 0x373B, 0x1216, null, 0xFFB3, 1, 13, 65, 0, 1);

        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([ffb1, ff05, ffb3], [], [])));
        Assert.Equal("ATK NK NANO 接收器", target.Target.Device.Name);

        string batteryReq = Pad("087D720200010701", 64);
        string chargeReq = Pad("0885720200010F01", 64);
        string batteryReply = withChecksum ? Pad("08A57200000107012A", 64) : Pad("087200000107012A", 64);
        string chargeReply = withChecksum ? Pad("0884720000010F0101", 64) : Pad("08720000010F0101", 64);

        var transport = new FakeReceiverTransport((ff05.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, batteryReq),
            new(DiagnosticOperation.HidInput, batteryReply),
            new(DiagnosticOperation.HidOutput, chargeReq),
            new(DiagnosticOperation.HidInput, chargeReply)
        ]));
        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(42, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Equal(LinkState.Online, reading.Link);
        transport.AssertComplete();
    }

    [Fact]
    public void AtkReadOnlyCatalogPidsAreEnabledForTheirProtocolShapes()
    {
        var registry = AdapterRegistry.CreateDefault();
        var c10c9 = new HidInterface("rx-10c9", "p-10c9", 0x373B, 0x10C9, null, 0xFF02, 2, 17, 17, 0, 1);
        var c11dc = new HidInterface("rx-11dc", "p-11dc", 0x373B, 0x11DC, null, 0xFF05, 1, 64, 64, 0, 2);
        var c11db = new HidInterface("rx-11db", "p-11db", 0x373B, 0x11DB, null, 0xFF05, 1, 64, 64, 0, 2);
        var detected = registry.Detect(new([c10c9, c11dc, c11db], [], [])).ToArray();
        Assert.Equal(3, detected.Length);
    }

    [Fact]
    public async Task CompxPrimaryReplySkipsRedundantSiblingsOnNextPollButKeepsThemForDiagnostics()
    {
        var primary = new HidInterface("rx-atk", "p-ff02", 0x373B, 0x11D9, null, 0xFF02, 2, 17, 17, 0, 1);
        var sibling = new HidInterface("rx-atk", "p-ff05", 0x373B, 0x11D9, null, 0xFF05, 0, 8, 0, 0, 1);
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([primary, sibling], [], [])));
        Assert.Single(device.Target.SiblingInputHids);

        const string cidMidReq = "081000000000000000000000000000003D";
        const string cidMidReply = "0810000000030108000000000000000031";
        ReceiverStep[] primarySteps() =>
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, CapturedCompxReply),
            new(DiagnosticOperation.HidOutput, CompxRequest),
            new(DiagnosticOperation.HidInput, cidMidReply),
            new(DiagnosticOperation.HidOutput, cidMidReq)
        ];
        ReceiverStep[] siblingTimeoutSteps() =>
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
        ];

        var transport1 = new FakeReceiverTransport(
            (primary.Path, primarySteps()),
            (sibling.Path, siblingTimeoutSteps()));
        var first = await registry.ReadAsync(device, transport1, null, CancellationToken.None);
        Assert.Equal(25, first.Percentage);
        transport1.AssertComplete();

        // 主集合已稳定回复：第二次普通轮询不再打开永远超时的旁路集合。
        var transport2 = new FakeReceiverTransport((primary.Path, primarySteps()));
        var second = await registry.ReadAsync(device, transport2, null, CancellationToken.None);
        Assert.Equal(25, second.Percentage);
        transport2.AssertComplete();

        // 诊断导出（trace != null）仍照常监听旁路集合。
        var transport3 = new FakeReceiverTransport(
            (primary.Path, primarySteps()),
            (sibling.Path, siblingTimeoutSteps()));
        var diag = await registry.ReadAsync(device, transport3, (_, _) => { }, CancellationToken.None);
        Assert.Equal(25, diag.Percentage);
        transport3.AssertComplete();

        // 重置缓存后再次按完整旁路探测。
        registry.ResetPollingCaches();
        var transport4 = new FakeReceiverTransport(
            (primary.Path, primarySteps()),
            (sibling.Path, siblingTimeoutSteps()));
        var afterReset = await registry.ReadAsync(device, transport4, null, CancellationToken.None);
        Assert.Equal(25, afterReset.Percentage);
        transport4.AssertComplete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompxResolvesInputAndOutputOnTheirOwnCollections(bool heartbeat)
    {
        var main = new HidInterface("rx", "cmd", 0x373B, 0x11D9, null, 0xFF02, 2, 17, 17, 0, 1);
        var sibling = main with { Path = "reply", UsagePage = 0xFF06, InputLength = 49, OutputLength = 0 };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([main, sibling], [], [])));
        var notifications = Enumerable.Repeat(new ReceiverStep(DiagnosticOperation.HidInput, Pad("091D", 49)), 5);
        var transport = new FakeReceiverTransport(
            ("cmd", heartbeat
                ? [new(DiagnosticOperation.OpenHid, Issue: ReadIssue.IoError, Code: -1072627696),
                   new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidOutput, "080300000000000000000000000000004A")]
                : [new(DiagnosticOperation.OpenHid, Issue: ReadIssue.IoError, Code: -1072627696),
                   new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidOutput, CompxRequest),
                   new(DiagnosticOperation.HidOutput, "081000000000000000000000000000003D")]),
            ("reply", heartbeat
                ? [new(DiagnosticOperation.OpenHid), .. notifications, new(DiagnosticOperation.HidInput, Pad("0803000000010100000000000000000048", 49))]
                : [new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidInput, Pad(CapturedCompxReply, 49)),
                   .. notifications, new(DiagnosticOperation.HidInput, Pad("0810000000030108000000000000000031", 49))]));

        if (heartbeat)
        {
            var presence = await registry.CheckPresenceAsync(device, transport, CancellationToken.None);
            Assert.Equal(LinkState.Online, presence?.Link);
        }
        else
        {
            var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
            Assert.Equal(25, reading.Percentage);
            Assert.Equal("ATK F1 Ultimate 2.0", reading.Device.Name);
        }
        var first = transport.OpenedHids[0];
        Assert.Equal((byte)8, first.RequiredInputReportId);
        Assert.Equal((byte)8, first.RequiredOutputReportId);
        var input = Assert.Single(transport.OpenedHids, hid => hid.Path == "reply");
        Assert.True(input.InputOnly);
        Assert.Equal((byte)8, input.RequiredInputReportId);
        var output = transport.OpenedHids.Last(hid => hid.Path == "cmd");
        Assert.Equal((byte)8, output.RequiredOutputReportId);
        Assert.Null(output.RequiredInputReportId);
        transport.AssertComplete();
    }

    [Fact]
    public async Task CompxMissingInputIdOnBothCollectionsCannotSend()
    {
        var main = new HidInterface("rx", "cmd", 0x373B, 0x11D9, null, 0xFF02, 2, 17, 17, 0, 1);
        var sibling = main with { Path = "reply", UsagePage = 0xFF06, InputLength = 49, OutputLength = 0 };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([main, sibling], [], [])));
        var missingId = new ReceiverStep(DiagnosticOperation.OpenHid, Issue: ReadIssue.IoError, Code: -1072627696);
        var transport = new FakeReceiverTransport(("cmd", [missingId]), ("reply", [missingId]));
        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);

        Assert.Equal(ReadIssue.IoError, reading.Issue);
        Assert.Equal(0, transport.Writes);
        transport.AssertComplete();
    }

    [Theory]
    [InlineData("0904", ReadIssue.Timeout)]
    [InlineData("081D0000000201750000000000000000B8", ReadIssue.Timeout)]
    [InlineData("08040000000219000E46000000000000DB", ReadIssue.InvalidResponse)]
    public async Task GenericCompxSplitInputBlocksOnlyMalformedBatteryReplies(string frame, ReadIssue expected)
    {
        var main = new HidInterface("rx", "cmd", 0x373B, 0x1998, null, 0xFF02, 2, 17, 17, 0, 1)
            { InputReportIds = "09", OutputReportIds = "08" };
        var sibling = main with { Path = "reply", UsagePage = 0xFF06, InputLength = 49, InputReportIds = "08,09", OutputLength = 0 };
        var gates = new SafetyGateStore();
        var registry = AdapterRegistry.CreateDefault(gates: gates);
        var device = Assert.Single(registry.Detect(new([main, sibling], [], [])));
        Assert.Equal(AdaptationSource.Generic, device.Target.Device.Source);
        var transport = new FakeReceiverTransport(
            ("cmd", [new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidOutput, CompxRequest)]),
            ("reply", [new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidInput, Pad(frame, 49)),
                new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)]));

        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);

        Assert.Equal(expected, reading.Issue);
        Assert.Equal(expected == ReadIssue.InvalidResponse, gates.IsBlocked(main, out _));
        transport.AssertComplete();
    }

    [Fact]
    public async Task SplitCompxWaitsForBatteryAmidUnrelatedInputNotifications()
    {
        var main = new HidInterface("rx", "cmd", 0x373B, 0x1031, null, 0xFF02, 2, 17, 17, 0, 1)
            { InputReportIds = "09", OutputReportIds = "08" };
        var sibling = main with { Path = "reply", UsagePage = 0xFF06, InputLength = 49, InputReportIds = "08,09", OutputLength = 0 };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([main, sibling], [], [])));
        var notifications = Enumerable.Repeat(new ReceiverStep(DiagnosticOperation.HidInput, Pad("0904", 49)), 9);
        var transport = new FakeReceiverTransport(
            ("cmd", [new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidOutput, CompxRequest)]),
            ("reply", [new(DiagnosticOperation.OpenHid), .. notifications, new(DiagnosticOperation.HidInput, Pad(CapturedCompxReply, 49))]));

        Assert.Equal(25, (await registry.ReadAsync(device, transport, null, CancellationToken.None)).Percentage);
        transport.AssertComplete();
    }

    [Fact]
    public void SplitCompxNeverPairsDifferentProductsInOneContainer()
    {
        var command = new HidInterface("shared", "cmd", 0x3367, 0x1961, null, 0xFF02, 2, 0, 0, 17, 1);
        var reply = new HidInterface("shared", "reply", 0x260D, 0x1074, null, 0xFF01, 0, 17, 0, 0, 2);
        Assert.Empty(new SplitCompxAdapter().Detect(new([command, reply], [], [])));
    }

    [Fact]
    public async Task CompxRetriesSiblingAfterConfirmedPrimaryStopsReplying()
    {
        var main = new HidInterface("rx", "cmd", 0x373B, 0x1031, null, 0xFF02, 2, 17, 17, 0, 1);
        var sibling = main with { Path = "reply", UsagePage = 0xFF06, InputLength = 49, OutputLength = 0 };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([main, sibling], [], [])));
        ReceiverStep open = new(DiagnosticOperation.OpenHid);
        ReceiverStep silent = new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout);
        ReceiverStep query = new(DiagnosticOperation.HidOutput, CompxRequest);
        var confirmed = new FakeReceiverTransport(("cmd", [open, new(DiagnosticOperation.HidInput, CapturedCompxReply), query]),
            ("reply", [open, silent]));
        Assert.Equal(25, (await registry.ReadAsync(device, confirmed, null, CancellationToken.None)).Percentage);
        confirmed.AssertComplete();

        var quiet = new FakeReceiverTransport(("cmd", [open, silent, query]));
        Assert.Equal(ReadIssue.Timeout, (await registry.ReadAsync(device, quiet, null, CancellationToken.None)).Issue);
        quiet.AssertComplete();

        var moved = new FakeReceiverTransport(("cmd", [open, silent, query]),
            ("reply", [open, new(DiagnosticOperation.HidInput, Pad(CapturedCompxReply, 49))]));
        Assert.Equal(25, (await registry.ReadAsync(device, moved, null, CancellationToken.None)).Percentage);
        moved.AssertComplete();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SplitInputCannotBypassOutputCheckOrAmbiguousReceivers(bool ambiguous)
    {
        var main = new HidInterface("rx", "cmd", 0x373B, 0x1031, null, 0xFF02, 2, 17, 17, 0, 1);
        var reply = main with { Path = "in1", UsagePage = 0xFF06, InputLength = 49, OutputLength = 0 };
        var other = reply with { Path = "in2", UsagePage = 0xFF07 };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new(ambiguous ? [main, reply, other] : [main, reply], [], [])));
        var missingId = new ReceiverStep(DiagnosticOperation.OpenHid, Issue: ReadIssue.IoError, Code: -1072627696);
        var transport = new FakeReceiverTransport(
            ("cmd", ambiguous ? [missingId] : [missingId, missingId]),
            ("in1", [new(DiagnosticOperation.OpenHid)]), ("in2", ambiguous ? [new(DiagnosticOperation.OpenHid)] : []));
        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(ReadIssue.IoError, reading.Issue);
        Assert.Equal(0, transport.Writes);
        transport.AssertComplete();
    }

    internal static string Pad(string prefix, int bytes) => prefix.PadRight(bytes * 2, '0');

    internal static async Task<BatteryReading> Read(HidInterface endpoint, FakeReceiverTransport transport)
    {
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([endpoint], [], [])));
        return await registry.ReadAsync(device, transport, null, CancellationToken.None);
    }
}
