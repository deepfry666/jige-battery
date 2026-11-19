namespace Jige.Core.Adapters;

internal enum MouseFamily { CompxOut, CompxFeature, BitMouse, XviOld, XviNew, Unknown }
internal enum MouseEvidence { MultipleSources, SingleSource, Unverified }
internal enum MouseChannel { OutputInput, FeatureInput, Feature }
internal enum BitMouseReply { WithoutChecksum, WithChecksum }

internal sealed record MouseProtocolShape(ushort UsagePage, ushort? Usage, ushort Length, byte ReportId, MouseChannel Channel)
{
    public bool Matches(HidInterface hid) =>
        hid.MetadataIssue == ReadIssue.None && hid.UsagePage == UsagePage && hid.Usage.HasValue &&
        (!Usage.HasValue || hid.Usage == Usage) &&
        (Channel == MouseChannel.Feature || hid.InputLength == Length) &&
        (Channel == MouseChannel.OutputInput ? hid.OutputLength == Length : hid.FeatureLength == Length);
}

internal sealed record MouseReceiver(
    string Model, ushort VendorId, ushort? ProductId, MouseFamily Family, MouseEvidence Evidence,
    bool Enabled = false, BitMouseReply BitReply = BitMouseReply.WithoutChecksum)
{
    public MouseProtocolShape? Shape => MouseReceiverCatalog.Shape(Family, VendorId);
    public bool Matches(HidInterface hid) => ProductId.HasValue && hid.VendorId == VendorId &&
        hid.ProductId == ProductId && Shape?.Matches(hid) == true &&
        (Family is not (MouseFamily.XviOld or MouseFamily.XviNew) || hid.InterfaceNumber == 2);
}

internal static class MouseReceiverCatalog
{
    private const ushort Atk = 0x373B;
    private const ushort GWolves = 0x33E4;

    public static MouseProtocolShape? Shape(MouseFamily family, ushort vendor) => family switch
    {
        MouseFamily.CompxOut when vendor == GWolves => new(0xFF02, null, 17, 8, MouseChannel.OutputInput),
        MouseFamily.CompxOut => new(0xFF02, 2, 17, 8, MouseChannel.OutputInput),
        MouseFamily.CompxFeature => new(0xFF04, null, 17, 8, MouseChannel.FeatureInput),
        MouseFamily.BitMouse => new(0xFF05, 1, 64, 8, MouseChannel.OutputInput),
        MouseFamily.XviOld or MouseFamily.XviNew => new(0xFFFF, 0, 65, 0, MouseChannel.Feature),
        _ => null
    };

    private static readonly MouseReceiver[] Models =
    [
        new("ATK 1K 接收器（X1 Pro 实测）", Atk, 0x101A, MouseFamily.CompxOut, MouseEvidence.SingleSource, true),
        new("ATK X1 Pro Max", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK X1 Ultra", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK X1 Ultimate", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK F1 Pro", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK F1 Pro Max", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK F1 Ultimate", Atk, 0x1031, MouseFamily.CompxOut, MouseEvidence.SingleSource, true),
        new("ATK F1 Ultimate 2.0", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK F1 Ultra", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK A9", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK A9 Pro Max", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK A9 Ultra", Atk, 0x111A, MouseFamily.CompxFeature, MouseEvidence.SingleSource),
        new("ATK 8K 接收器（A9 Ultra 替换候选）", Atk, 0x101B, MouseFamily.Unknown, MouseEvidence.Unverified),
        new("ATK A9 Ultimate", Atk, 0x11D9, MouseFamily.CompxOut, MouseEvidence.MultipleSources, true),
        new("ATK A9 Air+", Atk, 0x1278, MouseFamily.CompxOut, MouseEvidence.SingleSource, true),
        new("ATK U2 Pro Max", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK U2 Ultra", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK U2 Ultimate", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK U2 Mini", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK ZERO", Atk, 0x1155, MouseFamily.BitMouse, MouseEvidence.MultipleSources, true),
        new("ATK NK NANO 接收器（A9 Plus NK / A9 3.0 NK 候选）", Atk, 0x1216,
            MouseFamily.BitMouse, MouseEvidence.SingleSource, false, BitMouseReply.WithChecksum),
        new("ATK A9 Plus NK 接收器（星闪）", Atk, 0x10C9, MouseFamily.CompxOut, MouseEvidence.SingleSource),
        new("ATK A9 Pro Max 2.0 NK", Atk, 0x11DC, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK A9 Pro Max 2.0 NK（另一候选）", Atk, 0x11DB, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK A9 Ultimate+", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK X1 V2 Ultimate 2.0+", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK F1 V3 Ultimate+", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK F1 V3 Ultra Max", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK U2 V2 Ultimate", Atk, null, MouseFamily.Unknown, MouseEvidence.Unverified),
        new("ATK U2 Plus NK", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("G-Wolves Receiver RS（Fenrir Pro / HTX Ultra 实测）", GWolves, 0x3854,
            MouseFamily.CompxOut, MouseEvidence.MultipleSources),
        new("G-Wolves Receiver RS（Lycan / HTS Plus / HTS Plus Pro / Warg / HTX Mini / HTX Mini Asym / Fenrir Asym 候选）",
            GWolves, 0x3854, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("G-Wolves Receiver RS", GWolves, 0x3854, MouseFamily.XviNew, MouseEvidence.Unverified),
        new("G-Wolves Fenrir Pro 8K", GWolves, 0x3617, MouseFamily.XviNew, MouseEvidence.SingleSource, true),
        new("G-Wolves Fenrir Asym 8K", GWolves, 0x3517, MouseFamily.XviOld, MouseEvidence.Unverified),
        new("G-Wolves Fenrir Asym 8K", GWolves, 0x3517, MouseFamily.XviNew, MouseEvidence.Unverified),
        new("G-Wolves HTX Mini 8K", GWolves, 0x2717, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves Fenrir Max 8K", GWolves, 0x3717, MouseFamily.XviOld, MouseEvidence.SingleSource),
        new("G-Wolves HTS Plus 8K", GWolves, 0x5417, MouseFamily.XviOld, MouseEvidence.SingleSource),
        new("G-Wolves HTS Ultra", GWolves, 0x5317, MouseFamily.XviOld, MouseEvidence.SingleSource),
        new("G-Wolves HT-S2", GWolves, 0x7913, MouseFamily.XviOld, MouseEvidence.SingleSource),
        new("G-Wolves HT-S2 Pro", GWolves, 0x7917, MouseFamily.XviOld, MouseEvidence.SingleSource),
        new("G-Wolves HTR", GWolves, 0x7713, MouseFamily.XviOld, MouseEvidence.SingleSource),
        new("G-Wolves HTR Pro", GWolves, 0x7717, MouseFamily.XviOld, MouseEvidence.SingleSource),
        new("G-Wolves HSK Plus Lite", GWolves, 0x7203, MouseFamily.XviOld, MouseEvidence.Unverified),
        new("G-Wolves HSK Plus ACE-2", GWolves, null, MouseFamily.XviOld, MouseEvidence.Unverified),
        new("G-Wolves HTX Ultra 8K", GWolves, 0x5617, MouseFamily.XviNew, MouseEvidence.SingleSource),
        new("G-Wolves HSK Pro 2.0 8K", GWolves, 0x6817, MouseFamily.XviNew, MouseEvidence.SingleSource),
        new("G-Wolves HTM Plus", GWolves, 0x3817, MouseFamily.XviNew, MouseEvidence.SingleSource),
        new("G-Wolves VUK", GWolves, 0x3917, MouseFamily.XviOld, MouseEvidence.SingleSource)
    ];

    public static bool IsVendor(ushort? vid) => vid is Atk or GWolves;
    public static bool IsListed(ushort? vid, ushort? pid) =>
        pid.HasValue && Models.Any(row => row.VendorId == vid && row.ProductId == pid);

    public static IEnumerable<MouseReceiver> Candidates(ushort? vid, ushort? pid) =>
        Models.Where(row => row.VendorId == vid && row.ProductId == pid && pid.HasValue);
}
