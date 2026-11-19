namespace Jige.Tray;

internal enum DiagnosticStatus { Idle, Working, Ready, Failed, EditorFailed }

internal sealed class DiagnosticExport(string path)
{
    private Task<bool>? work;
    public DiagnosticStatus Status { get; private set; }
    public bool PreviewPending { get; private set; }

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

    public static string? Label(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Working => "正在导出诊断…",
        DiagnosticStatus.Ready => "诊断已导出",
        DiagnosticStatus.Failed => "诊断导出失败",
        DiagnosticStatus.EditorFailed => "报告已保存，打开失败",
        _ => null
    };

    public async Task WriteAsync(string report, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, report, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
