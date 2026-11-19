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

    internal static string Pad(string prefix, int bytes) => prefix.PadRight(bytes * 2, '0');

    internal static async Task<BatteryReading> Read(HidInterface endpoint, FakeReceiverTransport transport)
    {
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([endpoint], [], [])));
        return await registry.ReadAsync(device, transport, null, CancellationToken.None);
    }
}
