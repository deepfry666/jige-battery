using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Jige.Core;
using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Tray;

internal sealed class TrayLog(string path)
{
    private const string FormatPrefix = "[几格日志 3] ";
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

/// <summary>
/// 统一记录诊断所需的低噪声状态变化事件（D1）：
/// 仅在状态变化时记录，未识别设备去重，读取失败按小时汇总，全程无路径/用户名/序列号。
/// </summary>
internal sealed class TrayEventTracker(Action<string> write)
{
    public const long FailureSummaryIntervalMs = 3_600_000;

    private readonly object gate = new();
    private readonly HashSet<string> loggedUnknowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loggedRejections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ReadIssue, int> failureCounts = [];
    private long lastFailureSummaryMs = long.MinValue;
    private bool? lastGameMode;

    public void LogReceiverChanges(IReadOnlyList<ReceiverChange>? changes)
    {
        if (changes is null || changes.Count == 0)
            return;
        foreach (var change in changes)
        {
            string vid = change.VendorId?.ToString("X4", CultureInfo.InvariantCulture) ?? "----";
            string pid = change.ProductId?.ToString("X4", CultureInfo.InvariantCulture) ?? "----";
            string action = change.Inserted ? "接收器插入" : "接收器拔出";
            write($"{action} {vid}:{pid}（接口 {change.CollectionCount}）");
        }
    }

    public void LogDeviceLinkChange(DeviceIdentity device, LinkState? previous, LinkState current)
    {
        if (previous == current)
            return;
        if (current is not (LinkState.Online or LinkState.Offline or LinkState.Asleep))
            return;
        string name = DiagnosticPrivacy.SafeDeviceLabel(device);
        if (previous is null or LinkState.Unknown)
            write($"设备状态：{name} → {LinkLabel(current)}");
        else
            write($"设备状态变化：{name}：{LinkLabel(previous.Value)} → {LinkLabel(current)}");
    }

    public void LogUnknownReceivers(IReadOnlyList<UnknownReceiverInfo>? unknowns)
    {
        if (unknowns is null || unknowns.Count == 0)
            return;
        lock (gate)
        {
            foreach (var item in unknowns)
            {
                if (!loggedUnknowns.Add(item.Signature))
                    continue;
                string vid = item.VendorId?.ToString("X4", CultureInfo.InvariantCulture) ?? "----";
                string pid = item.ProductId?.ToString("X4", CultureInfo.InvariantCulture) ?? "----";
                string up = item.UsagePage?.ToString("X4", CultureInfo.InvariantCulture) ?? "----";
                string u = item.Usage?.ToString("X4", CultureInfo.InvariantCulture) ?? "----";
                write($"未识别设备 {vid}:{pid} {up}:{u} in={item.InputLength}/out={item.OutputLength}/feat={item.FeatureLength}");
            }
        }
    }

    public void LogSafetyGateRejection(SafetyGateRejection rejection)
    {
        string key = $"{rejection.Gate}|{rejection.Signature}|{rejection.Reason}";
        lock (gate)
        {
            if (!loggedRejections.Add(key))
                return;
        }
        write($"安全门 {rejection.Gate} 拦截 {DiagnosticPrivacy.SanitizeText(rejection.Signature)}：{DiagnosticPrivacy.SanitizeText(rejection.Reason)}");
    }

    public void ResetSafetyGateLog()
    {
        lock (gate)
            loggedRejections.Clear();
    }

    public void RecordReadFailure(ReadIssue issue, long nowMs)
    {
        if (issue == ReadIssue.None)
            return;
        lock (gate)
        {
            failureCounts[issue] = failureCounts.GetValueOrDefault(issue) + 1;
        }
        FlushReadFailuresIfDue(nowMs);
    }

    public void FlushReadFailuresIfDue(long nowMs, bool force = false)
    {
        string? summary = null;
        lock (gate)
        {
            if (failureCounts.Count == 0)
                return;
            if (!force && lastFailureSummaryMs != long.MinValue && nowMs - lastFailureSummaryMs < FailureSummaryIntervalMs)
                return;
            var parts = failureCounts.OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}={pair.Value}");
            summary = $"读取失败汇总（近一小时）：{string.Join("，", parts)}";
            failureCounts.Clear();
            lastFailureSummaryMs = nowMs;
        }
        if (summary is not null)
            write(summary);
    }

    public void LogGameMode(GameMode game)
    {
        bool inGame = game.Game;
        if (lastGameMode == inGame)
            return;
        bool hadPrevious = lastGameMode.HasValue;
        lastGameMode = inGame;
        if (!hadPrevious && !inGame)
            return;
        write(inGame ? $"进入游戏模式（{game.Status.Describe()}）" : "退出游戏模式");
    }

    public void LogUpdateCheck(string result) =>
        write($"检查更新：{DiagnosticPrivacy.SanitizeText(result, 80)}");

    private static string LinkLabel(LinkState state) => state switch
    {
        LinkState.Online => "在线",
        LinkState.Asleep => "休眠",
        LinkState.Offline => "离线",
        LinkState.Busy => "被占用",
        _ => "未知"
    };
}
