using Jige.Core;

namespace Jige.Tray;

internal enum AdaptationBadge { Verified, Protocol, Generic }

/// <summary>
/// 面板与设备页徽标映射（B1 / B2）：
/// 验证状态数据集中放在核心库的 DeviceCatalog 中，此处不再硬编码设备型号；
/// 面板徽标仍保持「已实机验证 / 按协议适配 / 通用适配」三种不变。
/// </summary>
internal static class DeviceVerification
{
    public static bool IsVerified(DeviceIdentity device) =>
        DeviceCatalog.GetVerification(device) == VerificationTier.HardwareVerified;

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
