using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Jige.Core.Adapters;

namespace Jige.Tray;

internal sealed class TrayLog(string path)
{
    private const string FormatPrefix = "[几格日志 2] ";
    private const int Limit = 1024 * 1024;
    private readonly object gate = new();

    public static string Describe(string message, Exception error)
    {
        int? code = error switch
        {
            Win32Exception native => native.NativeErrorCode,
            TransportException transport => transport.NativeCode,
            _ => null
        };
        string nativeCode = code is { } number ? $" Win32={number}" : "";
        return $"{message}（{error.GetType().Name} HRESULT=0x{error.HResult:X8}{nativeCode}）";
    }

    public static string Foreground(ForegroundKind kind) => kind switch
    {
        ForegroundKind.Own => "本程序",
        ForegroundKind.System => "系统窗口",
        _ => "其他程序"
    };

    public void Write(string message)
    {
        Debug.WriteLine(message);
        string line = $"{FormatPrefix}{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message.ReplaceLineEndings(" ")}{Environment.NewLine}";
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length + Encoding.UTF8.GetByteCount(line) > Limit)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, line);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public string Recent(int count)
    {
        try
        {
            lock (gate)
            {
                // 旧版本的日志可能含原始异常或窗口类名，只附带新版条目。
                var files = new[] { path + ".1", path };
                return string.Join(Environment.NewLine, files.Where(File.Exists).SelectMany(File.ReadLines)
                    .Where(line => line.StartsWith(FormatPrefix, StringComparison.Ordinal))
                    .TakeLast(count).Select(line => line[FormatPrefix.Length..]));
            }
        }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }
}
