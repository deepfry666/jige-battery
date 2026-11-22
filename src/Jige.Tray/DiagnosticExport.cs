namespace Jige.Tray;

internal enum DiagnosticStatus { Idle, Working, Ready, Failed, EditorFailed, FeedbackCopied, FeedbackFailed }

internal sealed class DiagnosticExport(string path)
{
    // A4 / G4：反馈链接不包含任何报告内容或设备信息，仅打开设备报告模板。
    public const string DeviceReportIssueUrl =
        "https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml";

    private Task<bool>? work;
    public DiagnosticStatus Status { get; private set; }
    public bool PreviewPending { get; private set; }
    public static bool HasReportStatus(DiagnosticStatus status) => status is DiagnosticStatus.Ready
        or DiagnosticStatus.EditorFailed or DiagnosticStatus.FeedbackCopied or DiagnosticStatus.FeedbackFailed;

    public bool HasReport => HasReportStatus(Status);

    public bool Start(Func<Task<bool>> generate)
    {
        if (work is not null)
            return false;
        Status = DiagnosticStatus.Working;
        PreviewPending = false;
        work = Task.Run(generate);
        return true;
    }

    public Task? Work => work;

    public bool Complete()
    {
        if (work is not { IsCompleted: true } finished)
            return false;
        work = null;
        Status = finished.Status == TaskStatus.RanToCompletion && finished.Result
            ? DiagnosticStatus.Ready : DiagnosticStatus.Failed;
        PreviewPending = Status == DiagnosticStatus.Ready;
        return true;
    }

    public void PreviewOpened()
    {
        PreviewPending = false;
        Status = DiagnosticStatus.Ready;
    }

    public void EditorFailed() => Status = DiagnosticStatus.EditorFailed;
    public void FeedbackCopied() => Status = DiagnosticStatus.FeedbackCopied;
    public void FeedbackFailed() => Status = DiagnosticStatus.FeedbackFailed;

    public static string? Label(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Working => "正在导出诊断…",
        DiagnosticStatus.Ready => "诊断已导出",
        DiagnosticStatus.Failed => "诊断导出失败",
        DiagnosticStatus.EditorFailed => "报告已保存，打开失败",
        DiagnosticStatus.FeedbackCopied => "已复制报告，请在浏览器粘贴",
        DiagnosticStatus.FeedbackFailed => "复制或打开反馈页失败",
        _ => null
    };

    public async Task WriteAsync(string report, CancellationToken token)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, report, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temporary, path, overwrite: true);
                    break;
                }
                catch (Exception error) when (attempt < 2 && error is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(30 * (attempt + 1), token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
