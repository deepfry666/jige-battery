using System.ComponentModel;
using System.Runtime.InteropServices;
using Jige.Core.Adapters;

namespace Jige.Core.Windows;

internal static class WindowsErrors
{
    public static TransportException Last(string operation, bool openingSerial = false) =>
        FromCode(Marshal.GetLastPInvokeError(), operation, openingSerial);

    public static TransportException FromCode(int code, string operation, bool openingSerial = false)
    {
        var (issue, reason) = code switch
        {
            2 or 3 or 55 or 433 or 1167 => (ReadIssue.Disconnected, "设备找不到或已断开"),
            5 when openingSerial => (ReadIssue.Busy, "串口可能被占用（例如 INZONE Hub），也可能是端口权限不足"),
            5 or 1314 => (ReadIssue.AccessDenied, "访问权限不足"),
            32 or 33 or 170 => (ReadIssue.Busy, "设备或端口被占用"),
            121 or 1460 => (ReadIssue.Timeout, "操作超时"),
            _ => (ReadIssue.IoError, new Win32Exception(code).Message)
        };
        return new(issue, $"{operation}失败，{reason}（Win32 {code}）");
    }
}
