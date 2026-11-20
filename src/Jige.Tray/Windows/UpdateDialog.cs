using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using static Windows.Win32.UI.Controls.TASKDIALOG_FLAGS;
using static Windows.Win32.UI.Controls.TASKDIALOG_NOTIFICATIONS;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed class UpdateDialog(HWND owner, UpdateFiles files, Action<string>? logResult = null)
{
    private const int Cancel = 2, Install = 100, Release = 101;
    private const uint CloseMessage = 0x0010; // WM_CLOSE
    private Progress? progress;
    private HWND window;
    private bool closing;

    public bool Run()
    {
        try
        {
            string current = ReleaseVersion.Current;
            string display = ReleaseVersion.Display;
            if (!ReleaseVersion.TryParseInstalled(current, out var version, out bool isPrerelease, out _))
                throw new InvalidDataException($"无法识别当前版本 {current}。");
            string currentLabel = isPrerelease ? display : $"v{display}";

            using var http = UpdateClient.CreateHttpClient();
            var client = new UpdateClient(http);
            var update = Wait("正在检查更新…", "正在从 GitHub 查询最新正式版。",
                TimeSpan.FromSeconds(25), token => client.CheckAsync(version, token));
            if (closing)
                return false;
            if (update is null)
            {
                logResult?.Invoke($"已是最新版本 {currentLabel}");
                Show("已是最新版本", $"当前版本 {currentLabel}。");
                return false;
            }
            logResult?.Invoke($"发现新版本 v{update.Version}（当前 {currentLabel}）");

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            bool installed = InstallPaths.IsInstalledExecutable(Environment.ProcessPath, local);
            string content = $"当前版本 {currentLabel}\n\n{update.Notes}";
            if (!installed)
                content += "\n\n当前运行的是独立构建。请从 Release 获取正式安装器。";

            int choice = Show($"有新版本 v{update.Version}", content, install: installed, release: true);
            if (!closing && choice == Release)
            {
                using var browser = Process.Start(new ProcessStartInfo(update.Page.AbsoluteUri) { UseShellExecute = true });
            }
            return !closing && choice == Install && InstallUpdate(client, update);
        }
        catch (OperationCanceledException)
        {
            // 用户取消或安装器要求退出时结束当前操作。
            logResult?.Invoke("已取消");
            return false;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or
            HttpRequestException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            logResult?.Invoke($"失败（{error.GetType().Name}）");
            if (!closing)
                Show("更新未完成", Explain(error));
            return false;
        }
    }

    public void Close()
    {
        closing = true;
        progress?.Cancellation.Cancel();
        if (!window.IsNull)
            PInvoke.PostMessage(window, CloseMessage, default, default);
    }

    private bool InstallUpdate(UpdateClient client, AvailableUpdate update) =>
        UpdateInstallation.Run(files,
            () => Wait($"正在下载 v{update.Version}…", "正在下载安装包并核对大小和 SHA-256。",
                TimeSpan.FromMinutes(10), token => client.DownloadAsync(update, files, token)),
            () => closing,
            LaunchInstaller);

    private static void LaunchInstaller(string package)
    {
        var start = new ProcessStartInfo(package)
        {
            ArgumentList = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" },
            UseShellExecute = false
        };
        using var installer = Process.Start(start) ?? throw new InvalidOperationException("无法启动几格安装器。");
    }

    private static string Explain(Exception error) => error switch
    {
        HttpRequestException { StatusCode: { } status } => $"GitHub 请求失败（HTTP {(int)status}）。",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "无法解析 GitHub 的地址。",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "无法与 GitHub 建立安全连接。",
        HttpRequestException or HttpIOException => "无法连接 GitHub，或下载连接已经中断。",
        JsonException => "无法读取 GitHub 返回的发行信息。",
        _ => error.Message
    };

    private T Wait<T>(string title, string content, TimeSpan limit, Func<CancellationToken, Task<T>> operation)
    {
        using var cancellation = new CancellationTokenSource(limit);
        var task = Task.Run(() => operation(cancellation.Token));
        var state = new Progress(task, cancellation);
        progress = state;
        try
        {
            Show(title, content, working: true);
            if (state.CancelledByUser || closing)
                throw new OperationCanceledException();
            return task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!state.CancelledByUser && !closing && cancellation.IsCancellationRequested)
        {
            throw new IOException("请求超时，请检查网络后重试。");
        }
        finally
        {
            progress = null;
            if (!task.IsCompleted)
            {
                cancellation.Cancel();
                // 等待已取消的任务清理文件，避免下一次下载与上一次重叠。
                try { task.GetAwaiter().GetResult(); }
                catch (Exception error) { Desktop.Log("更新窗口处理失败", error); }
            }
        }
    }

    private unsafe int Show(string title, string content, bool install = false, bool release = false, bool working = false)
    {
        var state = GCHandle.Alloc(this);
        try
        {
            fixed (char* caption = "几格 · 检查更新", header = title, body = content,
                installText = "下载并安装", releaseText = "查看 Release")
            {
                TASKDIALOG_BUTTON* buttons = stackalloc TASKDIALOG_BUTTON[2];
                uint count = 0;
                if (install)
                    buttons[count++] = new() { nButtonID = Install, pszButtonText = new(installText) };
                if (release)
                    buttons[count++] = new() { nButtonID = Release, pszButtonText = new(releaseText) };
                var dialog = new TASKDIALOGCONFIG
                {
                    cbSize = (uint)sizeof(TASKDIALOGCONFIG),
                    hwndParent = owner,
                    dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | (working ? TDF_CALLBACK_TIMER : 0),
                    dwCommonButtons = working || release
                        ? TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CANCEL_BUTTON
                        : TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CLOSE_BUTTON,
                    pszWindowTitle = new(caption),
                    pszMainInstruction = new(header),
                    pszContent = new(body),
                    cButtons = count,
                    pButtons = buttons,
                    nDefaultButton = working || release ? Cancel : 0,
                    pfCallback = &DialogCallback,
                    lpCallbackData = GCHandle.ToIntPtr(state),
                    cxWidth = 270
                };
                Graphics.Check(PInvoke.TaskDialogIndirect(in dialog, out int result, out _, out _), "显示更新窗口");
                return result;
            }
        }
        finally
        {
            window = default;
            state.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT DialogCallback(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam, nint data)
    {
        var dialog = (UpdateDialog)GCHandle.FromIntPtr(data).Target!;
        try
        {
            switch ((TASKDIALOG_NOTIFICATIONS)message)
            {
                case TDN_CREATED:
                    dialog.window = hwnd;
                    break;
                case TDN_BUTTON_CLICKED when dialog.progress is { Completing: false } progress:
                    progress.CancelledByUser = true;
                    progress.Cancellation.Cancel();
                    return (HRESULT)1; // 清理结束后再关闭窗口。
                case TDN_TIMER when dialog.progress is { Task.IsCompleted: true } done:
                    done.Completing = true;
                    PInvoke.SendMessage(hwnd, (uint)TASKDIALOG_MESSAGES.TDM_CLICK_BUTTON, (WPARAM)Cancel, default);
                    break;
            }
        }
        catch (Exception error)
        {
            Desktop.Log("更新窗口处理失败", error);
        }
        return default;
    }

    private sealed record Progress(Task Task, CancellationTokenSource Cancellation)
    {
        public bool Completing { get; set; }
        public bool CancelledByUser { get; set; }
    }
}
