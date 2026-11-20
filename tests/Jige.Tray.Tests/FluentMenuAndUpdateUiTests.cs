using System.Net;
using System.Text.Json;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class FluentMenuAndUpdateUiTests
{
    [Fact]
    public void MenuItemsCoverAllCommandsWithSeparatorBeforeExit()
    {
        Assert.Equal(
            [
                TrayMenuCommand.Refresh,
                TrayMenuCommand.Settings,
                TrayMenuCommand.Updates,
                TrayMenuCommand.Diagnostics,
                TrayMenuCommand.Exit
            ],
            TrayMenuModel.Items.Select(item => item.Command).ToArray());

        for (int i = 0; i < TrayMenuModel.Items.Length; i++)
        {
            bool expectSeparator = TrayMenuModel.Items[i].Command == TrayMenuCommand.Exit;
            Assert.Equal(expectSeparator, TrayMenuModel.Items[i].SeparatorBefore);
            Assert.Equal(expectSeparator, TrayMenuModel.SeparatorY(i).HasValue);
        }
    }

    [Fact]
    public void MenuGeometryIsNonOverlappingAndHitTestsAccurately()
    {
        float previousBottom = 0;
        for (int i = 0; i < TrayMenuModel.Items.Length; i++)
        {
            var rect = TrayMenuModel.ItemRect(i);
            Assert.Equal(TrayMenuModel.PaddingX, rect.X);
            Assert.Equal(TrayMenuModel.Width - TrayMenuModel.PaddingX * 2, rect.Width);
            Assert.Equal(TrayMenuModel.ItemHeight, rect.Height);
            Assert.True(rect.Y >= previousBottom);
            Assert.Equal(i, TrayMenuModel.Hit(rect.X + 10, rect.Y + rect.Height / 2f));
            Assert.Equal(TrayMenuModel.Items[i].Command, TrayMenuModel.CommandAt(i));

            if (TrayMenuModel.SeparatorY(i) is float sepY)
            {
                Assert.True(sepY > previousBottom && sepY < rect.Y);
                Assert.Equal(-1, TrayMenuModel.Hit(TrayMenuModel.Width / 2f, sepY));
            }
            previousBottom = rect.Y + rect.Height;
        }

        Assert.Equal((int)MathF.Ceiling(previousBottom + TrayMenuModel.PaddingY), TrayMenuModel.TotalHeight);
        Assert.Equal(-1, TrayMenuModel.Hit(1, 1));
        Assert.Equal(-1, TrayMenuModel.Hit(TrayMenuModel.Width - 1, TrayMenuModel.TotalHeight - 1));
        Assert.Equal(TrayMenuCommand.None, TrayMenuModel.CommandAt(-1));
        Assert.Equal(TrayMenuCommand.None, TrayMenuModel.CommandAt(99));
    }

    [Fact]
    public void MenuKeyboardNavigationWrapsCleanly()
    {
        int last = TrayMenuModel.Items.Length - 1;
        Assert.Equal(0, TrayMenuModel.MoveSelection(-1, +1));
        Assert.Equal(last, TrayMenuModel.MoveSelection(-1, -1));
        Assert.Equal(1, TrayMenuModel.MoveSelection(0, +1));
        Assert.Equal(last, TrayMenuModel.MoveSelection(0, -1));
        Assert.Equal(0, TrayMenuModel.MoveSelection(last, +1));
        Assert.Equal(last - 1, TrayMenuModel.MoveSelection(last, -1));
    }

    [Fact]
    public void MenuPlacementStaysInsideWorkAreaNearRightEdge()
    {
        var screen = new Bounds(0, 0, 1920, 1080);
        var work = new Bounds(0, 0, 1920, 1040);
        var trayIcon = new Bounds(1890, 1044, 1914, 1076);

        var placed = TrayMenuModel.Place(trayIcon, screen, work, 176, TrayMenuModel.TotalHeight, gapPx: 10, edgeMarginPx: 8);
        Assert.True(placed.Right <= work.Right - 8);
        Assert.True(placed.Left >= work.Left + 8);
        Assert.Equal(work.Bottom - 10, placed.Bottom);
        Assert.Equal(TrayMenuModel.TotalHeight, placed.Height);
    }

    [Fact]
    public void ReleaseNotesFormatterParsesHeadingsBulletsInlineMarkdownAndRules()
    {
        string markdown = """
            ## 新功能
            - **雷蛇 90B** 全系适配，支持 `72` 款鼠标与 `15` 款键盘
            * 修复 [PR #1](https://github.com/deepfry666/jige-battery/pull/1) 中的重复项
            ---
            ### 备注
            保留只读策略，不写入设备闪存。
            """;

        var blocks = ReleaseNotesFormatter.Parse(markdown);
        Assert.Equal(5, blocks.Length);
        Assert.Equal(new UpdateNoteBlock(UpdateNoteKind.Heading, "新功能"), blocks[0]);
        Assert.Equal(new UpdateNoteBlock(UpdateNoteKind.Bullet, "雷蛇 90B 全系适配，支持 72 款鼠标与 15 款键盘"), blocks[1]);
        Assert.Equal(new UpdateNoteBlock(UpdateNoteKind.Bullet, "修复 PR #1 中的重复项"), blocks[2]);
        Assert.Equal(new UpdateNoteBlock(UpdateNoteKind.Heading, "备注"), blocks[3]);
        Assert.Equal(new UpdateNoteBlock(UpdateNoteKind.Paragraph, "保留只读策略，不写入设备闪存。"), blocks[4]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n --- \n  ")]
    public void ReleaseNotesFormatterFallsBackWhenEmpty(string? raw)
    {
        var blocks = ReleaseNotesFormatter.Parse(raw);
        var single = Assert.Single(blocks);
        Assert.Equal(new UpdateNoteBlock(UpdateNoteKind.Paragraph, ReleaseNotesFormatter.EmptyFallback), single);
    }

    [Fact]
    public void UpdateProgressFormatsSizesFractionsAndVerifyingSummary()
    {
        Assert.Equal("512 B", UpdateProgress.FormatBytes(512));
        Assert.Equal("128 KB", UpdateProgress.FormatBytes(128 * 1024));
        Assert.Equal("12.5 MB", UpdateProgress.FormatBytes((long)(12.5 * 1024 * 1024)));

        var half = new UpdateProgress(5 * 1024 * 1024, 10 * 1024 * 1024, Verifying: false);
        Assert.Equal(0.5, half.Fraction!.Value, 3);
        Assert.Equal("已下载 5.0 MB / 10.0 MB · 50%", half.SummaryText);

        var verifying = new UpdateProgress(10 * 1024 * 1024, 10 * 1024 * 1024, Verifying: true);
        Assert.Equal(1.0, verifying.Fraction!.Value, 3);
        Assert.Equal("正在核对大小与 SHA-256…", verifying.SummaryText);
    }

    [Fact]
    public void UpdateViewStateTransitionsAndErrorExplanationsWork()
    {
        var now = DateTimeOffset.Parse("2026-11-20T05:00:00Z");
        var idle = UpdateViewState.Idle("v0.2.8", isInstalled: true);
        Assert.False(idle.IsBusy);

        var checking = UpdateViewState.Checking("v0.2.8", isInstalled: true);
        Assert.True(checking.IsBusy);

        var update = new AvailableUpdate(
            new Version(0, 2, 9),
            "- 新增界面优化",
            new Uri("https://github.com/deepfry666/jige-battery/releases/tag/v0.2.9"),
            new ReleaseAsset("Jige-Setup-0.2.9-x64.exe", 1024, "https://github.com/deepfry666/jige-battery/releases/download/v0.2.9/Jige-Setup-0.2.9-x64.exe"),
            new ReleaseAsset("SHA256SUMS.txt", 80, "https://github.com/deepfry666/jige-battery/releases/download/v0.2.9/SHA256SUMS.txt"));

        var found = UpdateViewState.Found("v0.2.8", isInstalled: true, update, now);
        Assert.Equal(UpdatePhase.Available, found.Phase);
        Assert.False(found.IsBusy);
        Assert.Equal(new UpdateNoteBlock(UpdateNoteKind.Bullet, "新增界面优化"), Assert.Single(found.Notes));

        var downloading = found.WithProgress(new(512, 1024, Verifying: false));
        Assert.Equal(UpdatePhase.Downloading, downloading.Phase);
        Assert.True(downloading.IsBusy);

        var verifying = downloading.WithProgress(new(1024, 1024, Verifying: true));
        Assert.Equal(UpdatePhase.Verifying, verifying.Phase);
        Assert.True(verifying.IsBusy);

        var back = verifying.BackToAvailable();
        Assert.Equal(UpdatePhase.Available, back.Phase);
        Assert.Null(back.Progress);

        Assert.Equal("GitHub 请求失败（HTTP 403）。",
            UpdateViewState.ExplainError(new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden)));
        Assert.Equal("无法读取 GitHub 返回的发行信息。",
            UpdateViewState.ExplainError(new JsonException("bad json")));
        Assert.Equal("校验失败", UpdateViewState.ExplainError(new InvalidDataException("校验失败")));
    }

    [Theory]
    [InlineData(0, 700, PanelLayout.UpdatesCompactHeight)]
    [InlineData(1, 700, PanelLayout.UpdatesAvailableHeight)]
    [InlineData(1, 320, 320)]
    [InlineData(0, 240, PanelLayout.UpdatesCompactHeight)]
    public void UpdatesPageSizingAdaptsBetweenCompactAndAvailableStates(int phaseHint, int availableHeight, int expectedHeight)
    {
        Assert.Equal(new PanelSize(expectedHeight, 0), PanelLayout.Size(PanelPage.Updates, phaseHint, availableHeight));
    }

    [Fact]
    public async Task DownloadAsyncReportsByteProgressAndVerifyingPhase()
    {
        const string package = "Jige-Setup-0.10.0-x64.exe";
        const string hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        const string releaseRoot = AvailableUpdate.Repository + "/releases/download/v0.10.0/";
        string sums = $"{hash}  {package}\n";

        using var http = new HttpClient(new StubHandler(uri => uri.AbsoluteUri switch
        {
            releaseRoot + AvailableUpdate.ChecksumsName => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sums) },
            releaseRoot + package => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("abc") },
            _ => throw new InvalidOperationException($"Unexpected URI {uri}")
        }));

        var update = new AvailableUpdate(
            new Version(0, 10, 0),
            "notes",
            new Uri(AvailableUpdate.Repository + "/releases/tag/v0.10.0"),
            new ReleaseAsset(package, 3, releaseRoot + package),
            new ReleaseAsset(AvailableUpdate.ChecksumsName, sums.Length, releaseRoot + AvailableUpdate.ChecksumsName));

        var dir = Directory.CreateTempSubdirectory("jige-update-progress-");
        try
        {
            var files = new UpdateFiles(Path.Combine(dir.FullName, "updates"));
            List<UpdateProgress> events = [];
            var progress = new SyncProgress<UpdateProgress>(events.Add);

            string path = await new UpdateClient(http).DownloadAsync(update, files, progress, default);
            Assert.True(File.Exists(path));
            Assert.True(events.Count >= 3);
            Assert.Equal(new UpdateProgress(0, 3, Verifying: false), events[0]);
            Assert.Contains(events, e => !e.Verifying && e.DownloadedBytes == 3 && e.TotalBytes == 3);
            Assert.Equal(new UpdateProgress(3, 3, Verifying: true), events[^1]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private sealed class StubHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request.RequestUri!));
    }
}
