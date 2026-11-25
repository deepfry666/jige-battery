using System.Text;
using System.Text.Json;

namespace Jige.Tray;

internal enum UpdatePhase
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Verifying,
    Error
}

internal enum UpdateNoteKind
{
    Heading,
    Bullet,
    Paragraph
}

internal sealed record UpdateNoteBlock(UpdateNoteKind Kind, string Text);

internal readonly record struct UpdateProgress(long DownloadedBytes, long TotalBytes, bool Verifying)
{
    public double? Fraction => Verifying
        ? 1.0
        : TotalBytes > 0 ? Math.Clamp((double)DownloadedBytes / TotalBytes, 0, 1) : null;

    public string SummaryText
    {
        get
        {
            if (Verifying)
                return "正在核对大小与 SHA-256…";
            if (TotalBytes <= 0)
                return DownloadedBytes > 0 ? $"已下载 {FormatBytes(DownloadedBytes)}" : "正在连接下载服务器…";
            int percent = (int)Math.Clamp(Math.Round((double)DownloadedBytes * 100.0 / TotalBytes), 0, 100);
            return $"已下载 {FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)} · {percent}%";
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            bytes = 0;
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F0} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}

internal sealed record UpdateViewState(
    UpdatePhase Phase,
    string CurrentVersionLabel,
    bool IsInstalled,
    AvailableUpdate? Available = null,
    UpdateNoteBlock[]? NoteBlocks = null,
    UpdateProgress? Progress = null,
    string? ErrorMessage = null,
    DateTimeOffset? CheckedAt = null,
    VerifiedPackage? Ready = null)
{
    public UpdateNoteBlock[] Notes => NoteBlocks ?? [];
    public bool IsBusy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Verifying;

    /// <summary>后台已下载并校验好新版安装包，等待用户点「重启并升级」。</summary>
    public bool IsReady => Phase == UpdatePhase.Available && Available is not null && Ready is not null && IsInstalled;

    public static UpdateViewState Idle(string currentVersionLabel, bool isInstalled) =>
        new(UpdatePhase.Idle, currentVersionLabel, isInstalled);

    public static UpdateViewState Checking(string currentVersionLabel, bool isInstalled) =>
        new(UpdatePhase.Checking, currentVersionLabel, isInstalled);

    public static UpdateViewState UpToDate(string currentVersionLabel, bool isInstalled, DateTimeOffset checkedAt) =>
        new(UpdatePhase.UpToDate, currentVersionLabel, isInstalled, CheckedAt: checkedAt);

    public static UpdateViewState Found(string currentVersionLabel, bool isInstalled, AvailableUpdate update,
        DateTimeOffset checkedAt) =>
        new(UpdatePhase.Available, currentVersionLabel, isInstalled,
            Available: update,
            NoteBlocks: ReleaseNotesFormatter.Parse(update.Notes),
            CheckedAt: checkedAt);

    public UpdateViewState WithProgress(UpdateProgress progress) => this with
    {
        Phase = progress.Verifying ? UpdatePhase.Verifying : UpdatePhase.Downloading,
        Progress = progress,
        ErrorMessage = null
    };

    public UpdateViewState WithReady(VerifiedPackage package) => this with
    {
        Phase = UpdatePhase.Available,
        Progress = null,
        ErrorMessage = null,
        Ready = package
    };

    public bool ShouldClearReadyOnCancel(bool installing) =>
        Phase == UpdatePhase.Verifying || (installing && Ready is not null);

    public UpdateViewState BackToAvailable(bool clearReady = false) => Available is null
        ? Idle(CurrentVersionLabel, IsInstalled)
        : this with
        {
            Phase = UpdatePhase.Available,
            Progress = null,
            ErrorMessage = null,
            Ready = clearReady ? null : Ready
        };

    public static UpdateViewState Failed(string currentVersionLabel, bool isInstalled, string message,
        AvailableUpdate? available = null) =>
        new(UpdatePhase.Error, currentVersionLabel, isInstalled,
            Available: available,
            NoteBlocks: available is null ? null : ReleaseNotesFormatter.Parse(available.Notes),
            ErrorMessage: message);

    public static string ExplainError(Exception error) => error switch
    {
        TimeoutException => "请求或下载超时，请检查网络后重试。",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } =>
            "更新服务器暂时限流，请稍后再试。",
        HttpRequestException { StatusCode: { } status } => $"更新服务器请求失败（HTTP {(int)status}）。",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "无法解析更新服务器的地址。",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "无法与更新服务器建立安全连接。",
        HttpRequestException or HttpIOException => "无法连接更新服务器，或下载连接已经中断。",
        JsonException => "无法读取更新服务器返回的发行信息。",
        InvalidDataException when !string.IsNullOrWhiteSpace(error.Message) => error.Message,
        IOException or UnauthorizedAccessException => "无法读写更新缓存，请检查磁盘空间后重试。",
        System.ComponentModel.Win32Exception => "无法启动更新程序。",
        _ => "检查或更新时发生未知错误。"
    };
}

internal static class ReleaseNotesFormatter
{
    public const string EmptyFallback = "此版本未附更新说明。";

    public static UpdateNoteBlock[] Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [new(UpdateNoteKind.Paragraph, EmptyFallback)];

        List<UpdateNoteBlock> blocks = [];
        string normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (string rawLine in normalized.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || IsHorizontalRule(line))
                continue;

            UpdateNoteKind kind;
            if (line.StartsWith('#'))
            {
                int i = 0;
                while (i < line.Length && line[i] == '#')
                    i++;
                string heading = line[i..].Trim();
                if (heading.Length == 0)
                    continue;
                kind = UpdateNoteKind.Heading;
                line = heading;
            }
            else if (TryStripBullet(line, out string bulletText))
            {
                if (bulletText.Length == 0)
                    continue;
                kind = UpdateNoteKind.Bullet;
                line = bulletText;
            }
            else
            {
                kind = UpdateNoteKind.Paragraph;
            }

            string cleaned = CleanInlineMarkdown(line);
            if (cleaned.Length > 0)
                blocks.Add(new(kind, cleaned));
        }

        return blocks.Count > 0 ? [.. blocks] : [new(UpdateNoteKind.Paragraph, EmptyFallback)];
    }

    private static bool IsHorizontalRule(string line)
    {
        if (line.Length < 3)
            return false;
        char first = line[0];
        if (first is not ('-' or '*' or '_'))
            return false;
        foreach (char c in line)
        {
            if (c != first && c != ' ')
                return false;
        }
        return true;
    }

    private static bool TryStripBullet(string line, out string content)
    {
        if (line.Length >= 2 && (line[0] is '-' or '*' or '+' or '•') && char.IsWhiteSpace(line[1]))
        {
            content = line[2..].Trim();
            return true;
        }
        content = "";
        return false;
    }

    internal static string CleanInlineMarkdown(string text)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            // Markdown 链接：[label](url) -> label
            if (text[i] == '[')
            {
                int closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i + 1 && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    int closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 2)
                    {
                        sb.Append(CleanInlineMarkdown(text[(i + 1)..closeBracket]));
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            // 粗体 / 斜体 / 行内代码标记剥离
            if (i + 1 < text.Length &&
                ((text[i] == '*' && text[i + 1] == '*') || (text[i] == '_' && text[i + 1] == '_')))
            {
                i += 2;
                continue;
            }
            if (text[i] == '`')
            {
                i++;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }
        return sb.ToString().Trim();
    }
}
