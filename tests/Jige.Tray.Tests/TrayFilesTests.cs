using System.ComponentModel;
using Jige.Core;
using Jige.Core.Adapters;
using Jige.Tray;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class TrayFilesTests
{
    [Theory]
    [InlineData(15, 30)]
    [InlineData(45, 30)]
    [InlineData(46, 60)]
    [InlineData(72, 60)]
    [InlineData(90, 60)]
    [InlineData(91, 120)]
    [InlineData(int.MinValue, 30)]
    [InlineData(int.MaxValue, 120)]
    public void LoadMigratesLegacyTrayModeAndSavesTheNearestAvailableInterval(int saved, int expected)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var files = new TrayFiles(folder.FullName);
            File.WriteAllText(files.ConfigPath, $$"""
                { "trayMode": "PerDevice", "pollSeconds": {{saved}}, "trayExcludedDeviceIds": ["mouse"] }
                """);

            Assert.True(files.TryLoadSettings(out var settings));
            Assert.Equal(TrayDisplayMode.Custom, settings.TrayMode);
            Assert.False(settings.IsInTray("mouse"));
            Assert.Equal(expected, settings.PollSeconds);
            Assert.Single(SettingsPanel.PollChoices, option => option.Target.Value == expected);
            var json = File.ReadAllText(files.ConfigPath);
            Assert.Contains("\"trayMode\": \"Custom\"", json);
            Assert.Contains($"\"pollSeconds\": {expected}", json);
            Assert.True(files.TryLoadSettings(out var reloaded));
            Assert.Equal(expected, reloaded.PollSeconds);
            Assert.False(reloaded.IsInTray("mouse"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void StateMigrationRemovesUnreadHistoryAndDevicePreferencesButPreservesRealReadings()
    {
        const string empty = "logitech-hidpp:bolt/1/wpid-B378";
        const string emptyOther = "unread-device";
        const string remembered = "logitech-hidpp:bolt/1/unit-keyboard";
        const string historyOnly = "history-only";
        const string onlineOnly = "online-only";
        var at = DateTimeOffset.UnixEpoch;
        var keyboard = new DeviceIdentity(remembered, "MX Keys S", DeviceKind.Keyboard);
        var settings = new TraySettings()
            .WithAlias(empty, "旧条目")
            .WithAlias(remembered, "公司键盘") with
        {
            HiddenDeviceIds = [empty.ToUpperInvariant(), emptyOther, remembered],
            TrayExcludedDeviceIds = [empty, remembered]
        };
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var files = new TrayFiles(folder.FullName);
            files.SaveSettings(settings);
            files.SaveState(new()
            {
                Readings = new() { [remembered] = new(keyboard, 70, false, at) },
                History = new()
                {
                    [empty] = new(at, null, []),
                    [emptyOther] = new(at, null, []),
                    [remembered] = new(at, null, []),
                    [historyOnly] = new(at, null, [new(at, 55, false, ChargingState.NotCharging)]),
                    [onlineOnly] = new(at, at, [])
                },
                Reminders = new() { [empty] = new(at) },
                RemovedOfflineDevices = [empty]
            });

            Assert.True(files.TryLoadSettings(out var loaded));
            var state = files.LoadState(ref loaded);
            Assert.Equal(keyboard, new TrayModel(loaded, state).OrderedForManagement().Single().Device);
            Assert.DoesNotContain(empty, state.History.Keys);
            Assert.DoesNotContain(emptyOther, state.History.Keys);
            Assert.Equal(3, files.LoadState().History.Count);
            Assert.Empty(files.LoadState().Reminders);
            Assert.Empty(files.LoadState().RemovedOfflineDevices);
            Assert.True(files.TryLoadSettings(out var reloaded));
            Assert.Equal(remembered, Assert.Single(reloaded.HiddenDeviceIds));
            Assert.Equal(remembered, Assert.Single(reloaded.TrayExcludedDeviceIds));
            Assert.Equal("公司键盘", Assert.Single(reloaded.DeviceAliases).Value);

            var lastWrite = File.GetLastWriteTimeUtc(files.StatePath);
            files.LoadState(ref reloaded);
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(files.StatePath));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnreadStateMigrationCanRetryAfterConfigurationWriteFails()
    {
        const string id = "logitech-hidpp:bolt/1/wpid-B378";
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var files = new TrayFiles(folder.FullName);
            files.SaveSettings(new() { TrayExcludedDeviceIds = [id] });
            files.SaveState(new() { History = new() { [id] = new(DateTimeOffset.UnixEpoch, null, []) } });
            var obstruction = Directory.CreateDirectory(files.ConfigPath + ".tmp");
            Assert.True(files.TryLoadSettings(out var loaded));
            List<string> errors = [];
            var state = files.LoadState(ref loaded, (message, _) => errors.Add(message));
            Assert.Single(errors);
            Assert.Empty(new TrayModel(loaded, state).OrderedForManagement());
            Assert.Contains(id, files.LoadState().History.Keys);

            obstruction.Delete();
            Assert.True(files.TryLoadSettings(out var reloaded));
            Assert.False(reloaded.IsInTray(id));
            Assert.Empty(files.LoadState(ref reloaded).History);
            Assert.True(files.TryLoadSettings(out var migrated));
            Assert.Empty(migrated.TrayExcludedDeviceIds);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnreadDevicesDoNotAppearUntilTheFirstSuccessfulBatteryReading()
    {
        var at = DateTimeOffset.UnixEpoch;
        var device = new DeviceIdentity("mouse", "Mouse", DeviceKind.Mouse);
        var model = new TrayModel(new(), new());
        model.ApplyPoll([new(device, null, false, ChargingState.Unknown, LinkState.Online, at)], false, at);
        Assert.Empty(model.OrderedForManagement());
        Assert.Null(model.BuildMainPanel().Hero);
        Assert.Empty(model.Snapshot().History);

        model.ApplyPoll([new(device, 0, false, ChargingState.NotCharging, LinkState.Online, at)], false, at);
        Assert.Equal(0, Assert.Single(model.OrderedForManagement()).Percentage);
    }

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
            File.WriteAllText(path, "2000-01-01 00:00:00 原始窗口类名 example-private\n" +
                "[几格日志 2] 2000-01-01 00:00:01 Logitech private-serial\n");
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
