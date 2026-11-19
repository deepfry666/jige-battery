using Jige.Core;

namespace Jige.Tray;

internal enum AdaptationBadge { Verified, Protocol, Generic }

internal static class DeviceVerification
{
    // 用适配器的身份前缀和原始型号判定，用户修改的显示名称不参与匹配。
    private static readonly (string Prefix, string Model)[] Verified =
    [
        ("inzone-h9:", "Sony INZONE H9（一代接收器）"), // 054C:0E53。
        ("mouse:", "G-Wolves HTX Ultra 8K") // 游狼 HTX Ultra 8K，33E4:5617。
    ];

    public static bool IsVerified(DeviceIdentity device) => Verified.Any(entry =>
        device.Id.StartsWith(entry.Prefix, StringComparison.OrdinalIgnoreCase) &&
        StringComparer.OrdinalIgnoreCase.Equals(entry.Model, device.Name));

    public static AdaptationBadge Badge(DeviceIdentity device) =>
        IsVerified(device) ? AdaptationBadge.Verified :
        device.Source == AdaptationSource.Generic ? AdaptationBadge.Generic :
        AdaptationBadge.Protocol;

    public static string Label(AdaptationBadge badge) => badge switch
    {
        AdaptationBadge.Verified => "已实机验证",
        AdaptationBadge.Generic => "通用适配",
        _ => "按协议适配"
    };

    public static string Label(DeviceIdentity device) => Label(Badge(device));
}
