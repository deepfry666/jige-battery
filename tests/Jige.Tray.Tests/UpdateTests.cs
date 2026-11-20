using System.Net;
using System.Text.Json;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class UpdateTests : IDisposable
{
    private const string Package = "Jige-Setup-0.10.0-x64.exe";
    private const string Hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string ReleaseRoot = AvailableUpdate.Repository + "/releases/download/v0.10.0/";
    private const string MirrorReleaseRoot = AvailableUpdate.MirrorRoot + "/releases/v0.10.0/";
    private const string Sums = Hash + "  " + Package + "\n";
    private static readonly Version Current = new(0, 9, 9);
    private readonly string dataFolder = Directory.CreateTempSubdirectory("jige-test-").FullName;

    [Theory]
    [InlineData("0.10.0", true)]
    [InlineData("1.0.0", true)]
    [InlineData("0.9.9", false)]
    [InlineData("0.9.8", false)]
    public void ComparesStableVersionsNumerically(string text, bool newer)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var candidate));
        Assert.Equal(newer, candidate > Current);
        Assert.True(ReleaseVersion.TryParseInstalled(ReleaseVersion.Current, out _, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("01.2.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3.0")]
    [InlineData("1.2.3-beta.1")]
    [InlineData("1.2.3+build")]
    public void RejectsNonCanonicalVersions(string text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void ParsesGitHubJsonAndSelectsOnlyMatchingAssets()
    {
        var release = JsonSerializer.Deserialize(Json(), ReleaseJsonContext.Default.ReleaseResponse);
        var update = Assert.IsType<AvailableUpdate>(AvailableUpdate.Select(release, Current));
        Assert.Equal(new Version(0, 10, 0), update.Version);
        Assert.Equal("更新内容", update.Notes);
        Assert.Equal(AvailableUpdate.Repository + "/releases/tag/v0.10.0", update.Page.AbsoluteUri);
        Assert.Equal(Package, update.Installer.Name);
        Assert.Equal(3, update.Installer.Size);
        Assert.Equal(AvailableUpdate.ChecksumsName, update.Checksums.Name);
        Assert.Null(AvailableUpdate.Select(release, new(0, 10, 0)));
        Assert.Null(AvailableUpdate.Select(release, new(1, 0, 0)));
    }

    [Theory]
    [InlineData("\"draft\":true", "\"draft\":false")]
    [InlineData("\"prerelease\":true", "\"prerelease\":false")]
    [InlineData("v0.10.0-beta.1", "v0.10.0")]
    [InlineData("Jige-Setup-0.10.0-arm64.exe", Package)]
    [InlineData("SHA256SUMS-missing.txt", AvailableUpdate.ChecksumsName)]
    [InlineData("\"size\":0", "\"size\":3")]
    [InlineData("https://example.com/", AvailableUpdate.Repository)]
    public void RejectsUnsuitableReleaseAndAssets(string replacement, string original)
    {
        var release = JsonSerializer.Deserialize(Json().Replace(original, replacement, StringComparison.Ordinal),
            ReleaseJsonContext.Default.ReleaseResponse);
        Assert.Throws<InvalidDataException>(() => AvailableUpdate.Select(release, Current));
    }

    [Fact]
    public void RejectsDuplicateAsset()
    {
        var release = JsonSerializer.Deserialize(Json(), ReleaseJsonContext.Default.ReleaseResponse)!;
        var assets = release.Assets!;
        Assert.Throws<InvalidDataException>(() => AvailableUpdate.Select(release with { Assets = [.. assets, assets[1]] },
            Current));
    }

    [Fact]
    public void ReadsTextAndBinaryStyleChecksumWithBomAndCrlf()
    {
        string text = $"\uFEFF{Hash}  other-file.exe\r\n{Hash.ToUpperInvariant()} *{Package}\r\n";
        Assert.Equal(Convert.FromHexString(Hash), UpdateFiles.ReadChecksum(text, Package));
    }

    [Fact]
    public void RejectsMissingMalformedAndDuplicateChecksums()
    {
        foreach (string contents in new[] { "", $"bad  {Package}", Sums + Sums })
            Assert.Throws<InvalidDataException>(() => UpdateFiles.ReadChecksum(contents, Package));
    }

    [Fact]
    public async Task VerifiesBothSizeAndHash()
    {
        string path = Path.Combine(dataFolder, Package);
        await File.WriteAllTextAsync(path, "abc");
        byte[] checksum = Convert.FromHexString(Hash);
        await UpdateFiles.VerifyAsync(path, 3, checksum, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateFiles.VerifyAsync(path, 4, checksum, default));
        await File.WriteAllTextAsync(path, "abd");
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateFiles.VerifyAsync(path, 3, checksum, default));
    }

    [Fact]
    public async Task ReplacesOldPackageAndPreservesSettingsAndCacheOnCleanup()
    {
        var trayFiles = new TrayFiles(dataFolder);
        trayFiles.SaveSettings(new() { StartWithWindows = true, TrayMode = TrayDisplayMode.LowestBattery });
        var readAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        trayFiles.SaveState(new() { Reminders = { ["鼠标"] = new(readAt) } });
        var folder = Path.Combine(dataFolder, "updates");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "old.exe"), "old");
        await File.WriteAllTextAsync(Path.Combine(folder, "old.tmp"), "interrupted");
        var path = trayFiles.Updates.PreparePackage(Package);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
        await File.WriteAllTextAsync(path, "abc");
        Assert.Equal(path, Assert.Single(Directory.GetFiles(folder)));

        trayFiles.Updates.Clear();
        trayFiles.Updates.Clear();
        Assert.False(Directory.Exists(folder));
        Assert.True(trayFiles.TryLoadSettings(out var settings));
        Assert.True(settings.StartWithWindows);
        Assert.Equal(TrayDisplayMode.LowestBattery, settings.TrayMode);
        Assert.Equal(readAt, trayFiles.LoadState().Reminders["鼠标"].Due);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("abd", false)]
    [InlineData("ab", false)]
    [InlineData("abcd", false)]
    public async Task DownloadKeepsOnlyVerifiedPackage(string payload, bool valid)
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Responses((uri, _) =>
        {
            requests.Add(uri.AbsoluteUri);
            var response = uri.AbsoluteUri switch
            {
                var url when url == UpdateClient.Latest.AbsoluteUri => Ok(Json()),
                ReleaseRoot + AvailableUpdate.ChecksumsName => Ok(Sums),
                ReleaseRoot + Package => new(HttpStatusCode.Found)
                {
                    Headers = { Location = new("https://release-assets.githubusercontent.com/package") }
                },
                "https://release-assets.githubusercontent.com/package" => Ok(payload),
                _ => throw new InvalidOperationException($"意外请求 {uri}")
            };
            return Task.FromResult(response);
        }));
        var client = new UpdateClient(http, [UpdateSource.GitHub]);
        var update = Assert.IsType<AvailableUpdate>(await client.CheckAsync(Current, default));
        Assert.Equal(UpdateClient.Latest.AbsoluteUri, Assert.Single(requests));

        string folder = Path.Combine(dataFolder, "updates");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "older.exe"), "old");
        var files = new UpdateFiles(folder);
        if (valid)
        {
            string file = await client.DownloadAsync(update, files, default);
            Assert.Equal(file, Assert.Single(Directory.GetFiles(folder)));
            Assert.Equal("abc", await File.ReadAllTextAsync(file));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(update, files, default));
            Assert.False(Directory.Exists(folder));
        }
        Assert.Equal(4, requests.Count);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("abd", false)]
    public void ConfirmedInstallVerifiesPackageAndLaunchesWithoutAGameQuery(string payload, bool valid)
    {
        using var http = new HttpClient(new Responses((uri, _) => Task.FromResult(uri.AbsoluteUri switch
        {
            ReleaseRoot + AvailableUpdate.ChecksumsName => Ok(Sums),
            ReleaseRoot + Package => Ok(payload),
            _ => throw new InvalidOperationException()
        })));
        var release = JsonSerializer.Deserialize(Json(), ReleaseJsonContext.Default.ReleaseResponse);
        var update = Assert.IsType<AvailableUpdate>(AvailableUpdate.Select(release, Current));
        var client = new UpdateClient(http);
        string folder = Path.Combine(dataFolder, "updates");
        var files = new UpdateFiles(folder);
        string? launched = null;

        bool Install() => UpdateInstallation.Run(files, () => DownloadVerifiedPackage(client, update, files),
            isClosing: () => false, launch: path => launched = path);

        if (valid)
        {
            Assert.True(Install());
            Assert.Equal(Assert.Single(Directory.GetFiles(folder)), launched);
            Assert.Equal("abc", File.ReadAllText(launched!));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => Install());
            Assert.Null(launched);
            Assert.False(Directory.Exists(folder));
        }
    }

    [Fact]
    public void ClosingAfterDownloadClearsPackageWithoutLaunching()
    {
        string folder = Path.Combine(dataFolder, "updates");
        var files = new UpdateFiles(folder);
        bool started = UpdateInstallation.Run(files, () =>
        {
            Directory.CreateDirectory(folder);
            string package = Path.Combine(folder, Package);
            File.WriteAllText(package, "abc");
            return package;
        }, isClosing: () => true, launch: _ => throw new InvalidOperationException("安装器不应启动"));

        Assert.False(started);
        Assert.False(Directory.Exists(folder));
    }

    private static string DownloadVerifiedPackage(UpdateClient client, AvailableUpdate update, UpdateFiles files) =>
        client.DownloadAsync(update, files, default).GetAwaiter().GetResult();

    [Fact]
    public async Task CancelsDownloadAndClearsStagingDirectory()
    {
        var downloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Responses(async (uri, token) =>
        {
            if (uri.AbsoluteUri.EndsWith(AvailableUpdate.ChecksumsName, StringComparison.Ordinal))
                return Ok(Sums);
            downloading.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }));
        var client = new UpdateClient(http);
        var release = JsonSerializer.Deserialize(Json(), ReleaseJsonContext.Default.ReleaseResponse);
        var update = Assert.IsType<AvailableUpdate>(AvailableUpdate.Select(release, Current));
        string folder = Path.Combine(dataFolder, "updates");
        using var cancel = new CancellationTokenSource();
        var download = client.DownloadAsync(update, new(folder), cancel.Token);
        await downloading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task ReportsNetworkAndMissingReleaseErrors()
    {
        using var clientDefaults = UpdateClient.CreateHttpClient();
        Assert.Equal("Jige-Battery", clientDefaults.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Null(clientDefaults.DefaultRequestHeaders.Authorization);

        using var missing = new HttpClient(new Responses((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(missing).CheckAsync(Current, default));

        using var limited = new HttpClient(new Responses((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new UpdateClient(limited).CheckAsync(Current, default));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task PrefersMirrorForCheckAndDownload()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Responses((uri, _) =>
        {
            requests.Add(uri.AbsoluteUri);
            var response = uri.AbsoluteUri switch
            {
                var url when url == UpdateClient.MirrorLatest.AbsoluteUri => Ok(Json(MirrorReleaseRoot)),
                MirrorReleaseRoot + AvailableUpdate.ChecksumsName => Ok(Sums),
                MirrorReleaseRoot + Package => Ok("abc"),
                _ => throw new InvalidOperationException($"意外请求 {uri}")
            };
            return Task.FromResult(response);
        }));
        var client = new UpdateClient(http);
        var update = Assert.IsType<AvailableUpdate>(await client.CheckAsync(Current, default));
        Assert.Equal(UpdateSource.Mirror, update.EffectiveSource);
        Assert.NotNull(update.Fallback);
        Assert.Equal(UpdateSource.GitHub, update.Fallback!.EffectiveSource);
        Assert.Equal(UpdateClient.MirrorLatest.AbsoluteUri, Assert.Single(requests));

        string folder = Path.Combine(dataFolder, "updates");
        string file = await client.DownloadAsync(update, new(folder), default);
        Assert.Equal("abc", await File.ReadAllTextAsync(file));
        Assert.Equal(
            [
                UpdateClient.MirrorLatest.AbsoluteUri,
                MirrorReleaseRoot + AvailableUpdate.ChecksumsName,
                MirrorReleaseRoot + Package
            ],
            requests);
    }

    [Fact]
    public async Task FallsBackToGitHubWhenMirrorCheckFailsOrTimesOut()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Responses(async (uri, token) =>
        {
            requests.Add(uri.AbsoluteUri);
            if (uri == UpdateClient.MirrorLatest)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            }
            if (uri == UpdateClient.Latest)
                return Ok(Json());
            throw new InvalidOperationException($"意外请求 {uri}");
        }));
        var client = new UpdateClient(http, mirrorCheckTimeout: TimeSpan.FromMilliseconds(50));
        var update = Assert.IsType<AvailableUpdate>(await client.CheckAsync(Current, default));
        Assert.Equal(UpdateSource.GitHub, update.EffectiveSource);
        Assert.Null(update.Fallback);
        Assert.Equal([UpdateClient.MirrorLatest.AbsoluteUri, UpdateClient.Latest.AbsoluteUri], requests);
    }

    [Fact]
    public async Task FallsBackToGitHubWhenMirrorDownloadFails()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Responses((uri, _) =>
        {
            requests.Add(uri.AbsoluteUri);
            var response = uri.AbsoluteUri switch
            {
                var url when url == UpdateClient.MirrorLatest.AbsoluteUri => Ok(Json(MirrorReleaseRoot)),
                MirrorReleaseRoot + AvailableUpdate.ChecksumsName => Ok(Sums),
                MirrorReleaseRoot + Package => new(HttpStatusCode.BadGateway),
                ReleaseRoot + AvailableUpdate.ChecksumsName => Ok(Sums),
                ReleaseRoot + Package => Ok("abc"),
                _ => throw new InvalidOperationException($"意外请求 {uri}")
            };
            return Task.FromResult(response);
        }));
        var client = new UpdateClient(http);
        var update = Assert.IsType<AvailableUpdate>(await client.CheckAsync(Current, default));
        string folder = Path.Combine(dataFolder, "updates");
        string file = await client.DownloadAsync(update, new(folder), default);
        Assert.Equal("abc", await File.ReadAllTextAsync(file));
        Assert.Equal(
            [
                UpdateClient.MirrorLatest.AbsoluteUri,
                MirrorReleaseRoot + AvailableUpdate.ChecksumsName,
                MirrorReleaseRoot + Package,
                ReleaseRoot + AvailableUpdate.ChecksumsName,
                ReleaseRoot + Package
            ],
            requests);
    }

    [Fact]
    public async Task DoesNotFallBackWhenUserCancelsMirrorCheck()
    {
        var requests = new List<string>();
        using var cts = new CancellationTokenSource();
        using var http = new HttpClient(new Responses((uri, _) =>
        {
            requests.Add(uri.AbsoluteUri);
            cts.Cancel();
            throw new TaskCanceledException();
        }));
        var client = new UpdateClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CheckAsync(Current, cts.Token));
        Assert.Equal(UpdateClient.MirrorLatest.AbsoluteUri, Assert.Single(requests));
    }

    [Fact]
    public async Task RefusesAssetRedirectOutsideMirror()
    {
        using var http = new HttpClient(new Responses((uri, _) =>
        {
            Assert.Equal(MirrorReleaseRoot + AvailableUpdate.ChecksumsName, uri.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new("https://example.com/sums") }
            });
        }));
        var release = JsonSerializer.Deserialize(Json(MirrorReleaseRoot), ReleaseJsonContext.Default.ReleaseResponse);
        var update = Assert.IsType<AvailableUpdate>(AvailableUpdate.Select(release, Current, UpdateSource.Mirror)) with
        {
            Fallback = null
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(http).DownloadAsync(update,
            new(Path.Combine(dataFolder, "updates")), default));
    }

    [Fact]
    public async Task RefusesAssetRedirectOutsideGitHub()
    {
        using var http = new HttpClient(new Responses((uri, _) =>
        {
            Assert.Equal(ReleaseRoot + AvailableUpdate.ChecksumsName, uri.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new("https://example.com/sums") }
            });
        }));
        var release = JsonSerializer.Deserialize(Json(), ReleaseJsonContext.Default.ReleaseResponse);
        var update = Assert.IsType<AvailableUpdate>(AvailableUpdate.Select(release, Current));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(http).DownloadAsync(update,
            new(Path.Combine(dataFolder, "updates")), default));
    }

    [Fact]
    public void RepairsSingleAutostartCommandAndQuotesInstalledPath()
    {
        var installed = InstallPaths.InstalledExecutable(@"C:\Users\Some Name\AppData\Local");
        Assert.True(InstallPaths.IsInstalledExecutable(installed.ToUpperInvariant(), @"C:\Users\Some Name\AppData\Local"));
        Assert.False(InstallPaths.IsInstalledExecutable(@"C:\Downloads\Jige.exe", @"C:\Users\Some Name\AppData\Local"));
        string? command = InstallPaths.AutostartCommand(true, installed);
        Assert.Equal($"\"{installed}\"", command);
        Assert.True(InstallPaths.ShouldRepairAutostart(null, command));
        Assert.True(InstallPaths.ShouldRepairAutostart("\"C:\\Old\\Jige.Tray.exe\"", command));
        Assert.True(InstallPaths.ShouldRepairAutostart(installed, command));
        Assert.False(InstallPaths.ShouldRepairAutostart(command?.ToUpperInvariant(), command));
        Assert.Null(InstallPaths.AutostartCommand(false, installed));
        Assert.True(InstallPaths.ShouldRepairAutostart(command, null));
        Assert.False(InstallPaths.ShouldRepairAutostart(null, null));
    }

    private static string Json(string root = ReleaseRoot) => $$"""
        {"tag_name":"v0.10.0","body":" 更新内容 ","draft":false,"prerelease":false,"assets":[
            {"name":"Source.zip","size":999,"browser_download_url":"{{root}}Source.zip"},
            {"name":"{{Package}}","size":3,"browser_download_url":"{{root}}{{Package}}"},
            {"name":"{{AvailableUpdate.ChecksumsName}}","size":{{Sums.Length}},"browser_download_url":"{{root}}{{AvailableUpdate.ChecksumsName}}"}
        ]}
        """;

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private sealed class Responses(Func<Uri, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request.RequestUri!, cancellationToken);
    }

    public void Dispose() => Directory.Delete(dataFolder, recursive: true);
}
