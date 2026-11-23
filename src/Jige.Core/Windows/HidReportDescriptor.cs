using Jige.Core.Adapters;

namespace Jige.Core.Windows;

internal static class HidReportDescriptor
{
    private const int Success = 0x00110000; // HIDP_STATUS_SUCCESS

    public static string? ListIds(Func<byte, int> initialize)
    {
        List<string> ids = [];
        for (int id = 0; id <= byte.MaxValue; id++)
        {
            int status = initialize((byte)id);
            if (status == Success)
                ids.Add($"{id:X2}");
            else if (status != TransportException.ReportIdNotFound)
                return null; // 摘要未知，打开接口时仍须严格校验实际需要的编号。
        }
        return ids.Count == 0 ? "00(raw)" : string.Join(',', ids);
    }

    public static bool IsDeclared(int type, byte id, int status) => status switch
    {
        Success => true,
        TransportException.ReportIdNotFound => false,
        _ => throw new TransportException(ReadIssue.IoError,
            $"校验 HID 报告 ID {id:X2} 失败（类型 {type}，0x{status:X8}）", nativeCode: status)
    };

    public static void Require(int type, byte id, string? enumeratedIds, Func<byte, int> initialize)
    {
        if (IsDeclared(type, id, initialize(id)))
            return;
        // 雷蛇等设备的原始无编号报告有长度，但所有 ID 都明确返回“不存在”。
        if (id == 0)
        {
            for (int candidate = 1; candidate <= byte.MaxValue; candidate++)
            {
                if (IsDeclared(type, (byte)candidate, initialize((byte)candidate)))
                    throw Missing();
            }
            return;
        }
        throw Missing();

        TransportException Missing()
        {
            if (id == 0 && enumeratedIds == "00(raw)" ||
                enumeratedIds?.Split(',').Contains($"{id:X2}", StringComparer.OrdinalIgnoreCase) == true)
                return new(ReadIssue.Disconnected, "枚举时声明的 HID 报告编号已变更，请重新枚举");
            return new(ReadIssue.IoError, $"HID 描述符未声明报告 ID {id:X2}（类型 {type}）",
                nativeCode: TransportException.ReportIdNotFound);
        }
    }
}
