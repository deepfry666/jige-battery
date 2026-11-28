using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class DeviceBrandTests
{
    [Fact]
    public void AllBrandsHaveValidGeometryWithinDesignBounds()
    {
        var brands = Enum.GetValues<DeviceBrand>();
        Assert.Equal(38, brands.Length);

        foreach (var brand in brands)
        {
            var geom = BrandIconData.Get(brand);
            Assert.True(geom.Contours.Length > 0, $"{brand} 缺少轮廓数据");

            short minX = short.MaxValue, minY = short.MaxValue;
            short maxX = short.MinValue, maxY = short.MinValue;
            foreach (var contour in geom.Contours)
            {
                Assert.True(contour.Length >= 6 && contour.Length % 2 == 0,
                    $"{brand} 轮廓点数无效：{contour.Length}");
                for (int i = 0; i < contour.Length; i += 2)
                {
                    short x = contour[i];
                    short y = contour[i + 1];
                    Assert.InRange(x, (short)0, (short)3200);
                    Assert.InRange(y, (short)0, (short)3200);
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            Assert.True(maxX - minX >= 800, $"{brand} 宽度过小：{maxX - minX}");
            Assert.True(maxY - minY >= 400, $"{brand} 高度过小：{maxY - minY}");
        }
    }

    [Theory]
    // 罗技分线：办公通用 logi vs 游戏线 Logitech G vs ASTRO
    [InlineData("logitech-hidpp:bolt/1", "MX Keys S", DeviceConnection.Receiver, "罗技 Bolt 接收器", nameof(DeviceBrand.Logitech))]
    [InlineData("logitech-hidpp:bolt/2", "MX Anywhere 3S", DeviceConnection.Receiver, "罗技 Bolt 接收器", nameof(DeviceBrand.Logitech))]
    [InlineData("logitech-hidpp:uni/1", "Logitech Unifying 接收器", DeviceConnection.Receiver, "罗技 Unifying 接收器", nameof(DeviceBrand.Logitech))]
    [InlineData("bt:mx3s", "MX Master 3S", DeviceConnection.Bluetooth, "Windows 蓝牙", nameof(DeviceBrand.Logitech))]
    [InlineData("logitech-hidpp:ls/1", "PRO X SUPERLIGHT 2", DeviceConnection.Receiver, "罗技 LIGHTSPEED 接收器", nameof(DeviceBrand.LogitechG))]
    [InlineData("logitech-hidpp:ls/2", "Logitech LIGHTSPEED 接收器 C547", DeviceConnection.Receiver, "罗技 LIGHTSPEED 接收器", nameof(DeviceBrand.LogitechG))]
    [InlineData("logitech-hidpp-direct:g502", "G502 X PLUS", DeviceConnection.UsbCable, "罗技 USB 直连", nameof(DeviceBrand.LogitechG))]
    [InlineData("logitech-headset:g733", "Logitech G733", DeviceConnection.Receiver, "罗技无线接收器", nameof(DeviceBrand.LogitechG))]
    [InlineData("logitech-headset:gprox2", "Logitech G PRO X 2 LIGHTSPEED", DeviceConnection.Receiver, "罗技无线接收器", nameof(DeviceBrand.LogitechG))]
    [InlineData("bt:g309", "G309 LIGHTSPEED", DeviceConnection.Bluetooth, "Windows 蓝牙", nameof(DeviceBrand.LogitechG))]
    [InlineData("bt:g915", "G915 TKL", DeviceConnection.Bluetooth, "Windows 蓝牙", nameof(DeviceBrand.LogitechG))]
    [InlineData("logitech-headset:a50", "ASTRO A50 Gen 5", DeviceConnection.Receiver, "罗技无线接收器", nameof(DeviceBrand.Astro))]
    // 索尼与华硕 ROG / TUF（含天选 TX）
    [InlineData("inzone-h9:com4", "Sony INZONE H9（一代接收器）", DeviceConnection.Receiver, null, nameof(DeviceBrand.SonyInzone))]
    [InlineData("bt:xm5", "WH-1000XM5", DeviceConnection.Bluetooth, "Windows 蓝牙", nameof(DeviceBrand.SonyInzone))]
    [InlineData("asus:kb1", "ASUS ROG 无线键盘", DeviceConnection.UsbCable, null, nameof(DeviceBrand.AsusRog))]
    [InlineData("asus-omni:r1:mouse", "ROG Harpe Ace Aim Lab Edition", DeviceConnection.Receiver, null, nameof(DeviceBrand.AsusRog))]
    [InlineData("asus-mouse:tuf1", "TUF Gaming M4 Wireless", DeviceConnection.Receiver, null, nameof(DeviceBrand.AsusTuf))]
    [InlineData("asus-mouse:tx1", "TX Gaming Mouse Mini Miku", DeviceConnection.Receiver, null, nameof(DeviceBrand.AsusTuf))]
    [InlineData("bt:tx", "华硕天选鼠标", DeviceConnection.Bluetooth, "Windows 蓝牙", nameof(DeviceBrand.AsusTuf))]
    // 其他全部品牌代表型号
    [InlineData("razer-headset:bs", "Razer BlackShark V3 Pro", DeviceConnection.Receiver, null, nameof(DeviceBrand.Razer))]
    [InlineData("razer-90b:viper", "Razer Viper V3 Pro", DeviceConnection.Receiver, null, nameof(DeviceBrand.Razer))]
    [InlineData("mouse:gw1", "G-Wolves HTX Ultra 8K", DeviceConnection.Receiver, null, nameof(DeviceBrand.GWolves))]
    [InlineData("steelseries:ss1", "SteelSeries Arctis Nova Pro Wireless", DeviceConnection.Receiver, null, nameof(DeviceBrand.SteelSeries))]
    [InlineData("hyperx:hx1", "HyperX Cloud III Wireless", DeviceConnection.Receiver, null, nameof(DeviceBrand.HyperX))]
    [InlineData("corsair:c1", "Corsair HS80 Max Wireless", DeviceConnection.Receiver, null, nameof(DeviceBrand.Corsair))]
    [InlineData("ulx:f1", "Finalmouse UltralightX", DeviceConnection.Receiver, null, nameof(DeviceBrand.Finalmouse))]
    [InlineData("mouse:p1", "Pulsar X2 CrazyLight Mini", DeviceConnection.Receiver, null, nameof(DeviceBrand.Pulsar))]
    [InlineData("mouse:h1", "Hitscan Hyperlight 8K", DeviceConnection.Receiver, null, nameof(DeviceBrand.Hitscan))]
    [InlineData("mouse:g1", "Glorious Model O Wireless", DeviceConnection.Receiver, null, nameof(DeviceBrand.Glorious))]
    [InlineData("split-compx:eg1", "Endgame Gear XM2we", DeviceConnection.Receiver, null, nameof(DeviceBrand.EndgameGear))]
    [InlineData("mouse:cg1", "Cougar Revenger Pro 4K", DeviceConnection.Receiver, null, nameof(DeviceBrand.Cougar))]
    [InlineData("mouse:rd1", "Redragon M991", DeviceConnection.UsbCable, null, nameof(DeviceBrand.Redragon))]
    [InlineData("ext:au1", "Audeze Maxwell", DeviceConnection.Receiver, null, nameof(DeviceBrand.Audeze))]
    [InlineData("ext:kc1", "Keychron Ultra-Link 8K", DeviceConnection.Receiver, null, nameof(DeviceBrand.Keychron))]
    [InlineData("mouse:atk1", "ATK F1 Ultimate", DeviceConnection.Receiver, null, nameof(DeviceBrand.Atk))]
    [InlineData("mouse:vxe1", "VXE R1 系列", DeviceConnection.Receiver, null, nameof(DeviceBrand.Vxe))]
    [InlineData("mouse:mad1", "MAD 鼠标", DeviceConnection.Receiver, null, nameof(DeviceBrand.Vxe))]
    [InlineData("mouse:vgn1", "VGN F2 Master", DeviceConnection.Receiver, null, nameof(DeviceBrand.Vgn))]
    [InlineData("ext:sc1", "Scyrox V8S", DeviceConnection.Receiver, null, nameof(DeviceBrand.Scyrox))]
    [InlineData("mouse:lm1", "Lamzu Maya X 8K", DeviceConnection.Receiver, null, nameof(DeviceBrand.Lamzu))]
    [InlineData("mouse:wl1", "WLmouse Beast X Max", DeviceConnection.Receiver, null, nameof(DeviceBrand.WLmouse))]
    [InlineData("mchose-kb:k87", "MCHOSE K87", DeviceConnection.Receiver, null, nameof(DeviceBrand.Mchose))]
    [InlineData("split-compx:dr1", "DAREU A950", DeviceConnection.Receiver, null, nameof(DeviceBrand.Dareu))]
    [InlineData("ext:aj1", "AJAZZ AJ179 V2 MAX", DeviceConnection.Receiver, null, nameof(DeviceBrand.Ajazz))]
    [InlineData("ext:ew1", "EWEADN S9", DeviceConnection.Receiver, null, nameof(DeviceBrand.Eweadn))]
    [InlineData("ext:al1", "AULA F87 Pro V2C", DeviceConnection.Receiver, null, nameof(DeviceBrand.Aula))]
    [InlineData("passive-mouse:as1", "ATTACK SHARK X11", DeviceConnection.Receiver, null, nameof(DeviceBrand.AttackShark))]
    [InlineData("passive-mouse:dl1", "Delux M600 Pro", DeviceConnection.Receiver, null, nameof(DeviceBrand.Delux))]
    [InlineData("passive-mouse:ip1", "Inphic IN9 Pro", DeviceConnection.Receiver, null, nameof(DeviceBrand.Inphic))]
    [InlineData("passive-mouse:rp1", "Rapoo VT7", DeviceConnection.Receiver, null, nameof(DeviceBrand.Rapoo))]
    [InlineData("passive-mouse:ic1", "Incott 8K 鼠标", DeviceConnection.Receiver, null, nameof(DeviceBrand.Incott))]
    // 共享占位与兜底
    [InlineData("mouse:cpx", "Compx 方案鼠标", DeviceConnection.Receiver, null, nameof(DeviceBrand.Unknown))]
    [InlineData("passive-mouse:shared", "ATTACK SHARK / Delux 鼠标", DeviceConnection.Receiver, null, nameof(DeviceBrand.Unknown))]
    [InlineData("mouse:shared2", "VXE / VGN 鼠标", DeviceConnection.Receiver, null, nameof(DeviceBrand.Unknown))]
    [InlineData("bt:other", "AirPods Pro", DeviceConnection.Bluetooth, "Windows 蓝牙", nameof(DeviceBrand.Bluetooth))]
    [InlineData("custom:dev", "未知无线外设", DeviceConnection.Receiver, null, nameof(DeviceBrand.Unknown))]
    public void ResolvesExpectedBrand(string id, string name, DeviceConnection connection, string? sourceLabel, string expectedBrand)
    {
        var identity = new DeviceIdentity(id, name, DeviceKind.Mouse, Connection: connection)
        {
            SourceLabel = sourceLabel
        };
        Assert.Equal(Enum.Parse<DeviceBrand>(expectedBrand), DeviceBrandResolver.Resolve(identity));
    }

    [Fact]
    public void AllSingleBrandCatalogEntriesResolveWithoutFallingBackToUnknown()
    {
        foreach (var entry in DeviceCatalog.Entries)
        {
            bool ambiguousPlaceholder = entry.Model.Contains("Compx 方案", StringComparison.OrdinalIgnoreCase) ||
                (entry.Model.Contains("ATTACK SHARK", StringComparison.OrdinalIgnoreCase) && entry.Model.Contains("Delux", StringComparison.OrdinalIgnoreCase)) ||
                (entry.Model.Contains("VXE", StringComparison.OrdinalIgnoreCase) && entry.Model.Contains("VGN", StringComparison.OrdinalIgnoreCase));

            var identity = new DeviceIdentity($"{entry.AdapterPrefix}{entry.VidPid}", entry.Model, entry.Kind);
            var brand = DeviceBrandResolver.Resolve(identity);
            if (ambiguousPlaceholder)
                Assert.Equal(DeviceBrand.Unknown, brand);
            else
                Assert.NotEqual(DeviceBrand.Unknown, brand);
        }
    }

    [Fact]
    public void HoverCardCarriesResolvedBrandEvenAfterUserRename()
    {
        var now = new DateTimeOffset(2026, 11, 29, 2, 0, 0, TimeSpan.Zero);
        var device = new DeviceIdentity("logitech-hidpp:ls/1", "PRO X SUPERLIGHT 2", DeviceKind.Mouse)
        {
            SourceLabel = "罗技 LIGHTSPEED 接收器"
        };
        var model = new TrayModel(new()
        {
            TrayMode = TrayDisplayMode.Custom,
            DeviceAliases = new(StringComparer.OrdinalIgnoreCase) { [device.Id] = "我的主力鼠" }
        }, new());
        model.ApplyPoll([new BatteryReading(device, 74, false, ChargingState.NotCharging, LinkState.Online, now)], false, now);

        var single = HoverCardBuilder.Build(model, device.Id, now);
        Assert.Equal(HoverCardKind.Single, single.Kind);
        Assert.Equal("我的主力鼠", single.DeviceName);
        Assert.Equal(DeviceBrand.LogitechG, single.Brand);

        var list = HoverCardBuilder.Build(model, null, now, forceList: true);
        Assert.Equal(HoverCardKind.List, list.Kind);
        Assert.Single(list.Rows);
        Assert.Equal(DeviceBrand.LogitechG, list.Rows[0].Brand);
    }
}
