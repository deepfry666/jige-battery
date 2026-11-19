using System.ComponentModel;
using Jige.Core;
using Jige.Core.Adapters;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class TrayFilesTests
{
    [Fact]
    public async Task DiagnosticActionUsesOneWriterAndKeepsPreviousReportAfterCancellation()
    {
        using CancellationTokenSource cancelled = new();
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            string path = new TrayFiles(folder.FullName).DiagnosticsPath;
            var export = new DiagnosticExport(path);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(export.Start(async () =>
            {
                await release.Task;
                await export.WriteAsync("第一份报告", CancellationToken.None);
                return true;
            }));
            Assert.False(export.Start(() => throw new InvalidOperationException("重复导出")));
            Assert.Equal(DiagnosticStatus.Working, export.Status);
            Assert.False(export.Complete());
            release.SetResult();

            await export.Work!;
            Assert.True(export.Complete());
            Assert.Equal(DiagnosticStatus.Ready, export.Status);
            Assert.Equal("第一份报告", File.ReadAllText(path));
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                export.WriteAsync("取消的报告", cancelled.Token));
            Assert.Equal("第一份报告", File.ReadAllText(path));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(folder.FullName)));
            Assert.True(export.Start(() => Task.FromResult(false)));
            await export.Work!;
            Assert.True(export.Complete());
            Assert.Equal(DiagnosticStatus.Failed, export.Status);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void LogsContainOnlyNewSanitizedEventsAndRotateAtOneMegabyte()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(folder.FullName, "jige.log");
            File.WriteAllText(path, "2000-01-01 00:00:00 原始窗口类名 example-private\n");
            var log = new TrayLog(path);
            var message = TrayLog.Describe("访问失败", new Win32Exception(5, @"D:\private\file"));
            log.Write($"{message} {TrayLog.Foreground(ForegroundKind.Other)}");
            string recent = log.Recent(80);
            Assert.Contains("访问失败", recent);
            Assert.Contains("Win32Exception HRESULT=0x", recent);
            Assert.Contains("Win32=5", recent);
            Assert.Contains("其他程序", recent);
            Assert.DoesNotContain("private", recent);
            Assert.Equal("系统窗口", TrayLog.Foreground(ForegroundKind.System));
            Assert.Equal("本程序", TrayLog.Foreground(ForegroundKind.Own));

            using (var existing = File.OpenWrite(path))
                existing.SetLength(1024 * 1024 - 1);
            log.Write("轮换之后");
            Assert.Equal(2, Directory.GetFiles(folder.FullName).Length);
            Assert.All(Directory.GetFiles(folder.FullName), file => Assert.True(new FileInfo(file).Length <= 1024 * 1024));
            Assert.Contains("轮换之后", log.Recent(1));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void CountsPhysicalUnmatchedVendorReceiversOnlyOnce()
    {
        var known = new HidInterface("known", "hid-1", 0x373B, 0x1031, null, 0xFF02, 2, 17, 17, 0, 2);
        var unknown = known with { ReceiverId = "unknown", VendorId = 0x1234, Path = "hid-2" };
        var keyboard = known with { ReceiverId = "keyboard", UsagePage = 1, Path = "hid-3" };
        var inventory = new DeviceInventory([known, unknown, keyboard,
            unknown with { ReceiverId = "UNKNOWN", Path = "hid-4", Usage = 3 }], [], []);
        Assert.Equal(1, PollResult.CountUnknown(inventory, AdapterRegistry.CreateDefault().Detect(inventory)));
    }
}
