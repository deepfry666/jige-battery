using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jige.Tray;

internal static class ReleaseVersion
{
    public static string Current { get; } =
        typeof(ReleaseVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+', 2)[0]
        ?? typeof(ReleaseVersion).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>C2：面向界面展示的版本文本（正式版如「0.2.3」，测试版如「0.2.3 测试版（abc1234）」）。</summary>
    public static string Display { get; } = FormatDisplay(Current);

    public static bool IsPrerelease =>
        TryParseInstalled(Current, out _, out bool prerelease, out _) && prerelease;

    // 正式通道仅接受语义化版本的三个数字段，拒绝预发行标签与前导零。
    public static bool TryParse(string? value, out Version version)
    {
        if (Version.TryParse(value, out var parsed) && parsed.Build >= 0 && parsed.Revision < 0 &&
            value == parsed.ToString(3))
        {
            version = parsed;
            return true;
        }

        version = new(0, 0, 0);
        return false;
    }

    /// <summary>
    /// C2 / C3：解析本机已安装版本，兼容正式版（0.2.3）与带提交号的测试版（0.2.3-beta.abc1234）。
    /// 返回基础三段版本号以便与 GitHub 正式版比较，确保测试版不会被提示降级到更低的正式版。
    /// </summary>
    public static bool TryParseInstalled(string? value, out Version baseVersion, out bool isPrerelease, out string? commitId)
    {
        baseVersion = new(0, 0, 0);
        isPrerelease = false;
        commitId = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string clean = value.Split('+', 2)[0].Trim();
        int dash = clean.IndexOf('-');
        if (dash < 0)
            return TryParse(clean, out baseVersion);

        string core = clean[..dash];
        string suffix = clean[(dash + 1)..];
        if (suffix.Length == 0 || !TryParse(core, out baseVersion))
            return false;

        isPrerelease = true;
        commitId = ExtractCommitId(suffix);
        return true;
    }

    /// <summary>C2：把版本号格式化为人类可读文本，如「0.2.3」或「0.2.3 测试版（abc1234）」。</summary>
    public static string FormatDisplay(string? value)
    {
        if (!TryParseInstalled(value, out var baseVersion, out bool isPrerelease, out var commit))
            return value?.Split('+', 2)[0] ?? "";
        if (!isPrerelease)
            return baseVersion.ToString(3);
        return commit is { Length: > 0 }
            ? $"{baseVersion.ToString(3)} 测试版（{commit}）"
            : $"{baseVersion.ToString(3)} 测试版";
    }

    private static string? ExtractCommitId(string suffix)
    {
        // 支持 "beta.abc1234"、"pre.abc1234"、"abc1234" 等形式，取 7 位短提交号。
        string token = suffix.Contains('.') ? suffix[(suffix.LastIndexOf('.') + 1)..] : suffix;
        if (token.Length >= 7 && token.All(Uri.IsHexDigit))
            return token[..Math.Min(7, token.Length)].ToLowerInvariant();
        return null;
    }
}

internal sealed record ReleaseResponse(
    [property: JsonPropertyName("tag_name")] string? Tag,
    string? Body,
    bool Draft,
    bool Prerelease,
    ReleaseAsset?[]? Assets);

internal sealed record ReleaseAsset(
    string? Name, long Size, [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReleaseResponse))]
internal sealed partial class ReleaseJsonContext : JsonSerializerContext;

internal sealed record UpdateSource(string Name, Uri LatestUri, string AssetBasePrefix, string[] AllowedHosts)
{
    public static UpdateSource Mirror { get; } = new(
        "国内镜像",
        new("https://download.imfourj.cn/jige-battery/latest.json"),
        "https://download.imfourj.cn/jige-battery/releases/",
        ["download.imfourj.cn"]);

    public static UpdateSource GitHub { get; } = new(
        "GitHub",
        new("https://api.github.com/repos/deepfry666/jige-battery/releases/latest"),
        AvailableUpdate.Repository + "/releases/download/",
        ["api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"]);

    public bool IsAllowedHost(string host) => AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    public string AssetUrl(string tag, string name) => $"{AssetBasePrefix}{tag}/{name}";
}

internal sealed record AvailableUpdate(
    Version Version,
    string Notes,
    Uri Page,
    ReleaseAsset Installer,
    ReleaseAsset Checksums,
    UpdateSource? Source = null,
    AvailableUpdate? Fallback = null)
{
    public const string Repository = "https://github.com/deepfry666/jige-battery";
    public const string MirrorRoot = "https://download.imfourj.cn/jige-battery";
    public const string ChecksumsName = "SHA256SUMS.txt";
    public const int MaxChecksumsSize = 64 * 1024;
    private const long MaxInstallerSize = 512L * 1024 * 1024;

    public UpdateSource EffectiveSource => Source ?? UpdateSource.GitHub;

    public static AvailableUpdate? Select(ReleaseResponse? release, Version current, bool currentIsPrerelease = false) =>
        Select(release, current, UpdateSource.GitHub, currentIsPrerelease);

    public static AvailableUpdate? Select(ReleaseResponse? release, Version current, UpdateSource source,
        bool currentIsPrerelease = false)
    {
        if (release is null || release.Draft || release.Prerelease ||
            release.Tag is not { Length: > 1 } tag || tag[0] != 'v' ||
            !ReleaseVersion.TryParse(tag[1..], out var version))
            throw new InvalidDataException($"{source.Name} 没有返回有效的正式版本。");
        // 测试版（例如 0.2.19-beta.xxx）允许升级到同号正式版（v0.2.19），但不降级到更低版本。
        if (version < current || (version == current && !currentIsPrerelease))
            return null;

        var installer = FindAsset($"Jige-Setup-{version}-x64.exe", MaxInstallerSize);
        var checksums = FindAsset(ChecksumsName, MaxChecksumsSize);
        var notes = release.Body?.Trim();
        if (string.IsNullOrEmpty(notes))
            notes = "此版本未附更新说明。";
        else
            notes = ShortenNotes(notes, 1600);

        var page = new Uri($"{Repository}/releases/tag/{tag}");
        AvailableUpdate? fallback = null;
        if (ReferenceEquals(source, UpdateSource.Mirror))
        {
            var githubInstaller = installer with { DownloadUrl = UpdateSource.GitHub.AssetUrl(tag, installer.Name!) };
            var githubChecksums = checksums with { DownloadUrl = UpdateSource.GitHub.AssetUrl(tag, checksums.Name!) };
            fallback = new(version, notes, page, githubInstaller, githubChecksums, UpdateSource.GitHub);
        }

        return new(version, notes, page, installer, checksums, source, fallback);

        ReleaseAsset FindAsset(string name, long maxSize)
        {
            var matches = release.Assets?.OfType<ReleaseAsset>().Where(asset => asset.Name == name).ToArray();
            if (matches is not [var match] || match.Size <= 0 || match.Size > maxSize ||
                match.DownloadUrl != source.AssetUrl(tag, name))
                throw new InvalidDataException($"发行版缺少有效的 {name}。");
            return match;
        }
    }

    private static string ShortenNotes(string raw, int limit)
    {
        if (raw.Length <= limit)
            return raw;
        // 优先在换行边界截断，避免把 Markdown 链接 `[文本](url)` 从中间腰斩后暴露残缺 URL。
        int cut = raw.LastIndexOf('\n', Math.Min(limit - 1, raw.Length - 1));
        if (cut > limit / 2)
            return raw[..cut].TrimEnd() + "\n…";
        return Presentation.Shorten(raw, limit);
    }
}

/// <summary>已下载并通过大小与 SHA-256 校验的安装包；安装前会按同一校验值再核对一次。</summary>
internal sealed record VerifiedPackage(string Path, long Size, byte[] Sha256);

internal static class UpdateInstallation
{
    public static bool Run(UpdateFiles files, Func<string> downloadVerifiedPackage,
        Func<bool> isClosing, Action<string> launch)
    {
        bool started = false;
        try
        {
            string package = downloadVerifiedPackage();
            if (isClosing())
                return false;
            launch(package);
            started = true;
            return true;
        }
        finally
        {
            if (!started)
                files.TryClear();
        }
    }

    public static async Task<bool> RunAsync(UpdateFiles files, Func<Task<string>> downloadVerifiedPackageAsync,
        Func<bool> isClosing, Action<string> launch)
    {
        bool started = false;
        try
        {
            string package = await downloadVerifiedPackageAsync().ConfigureAwait(false);
            if (isClosing())
                return false;
            launch(package);
            started = true;
            return true;
        }
        finally
        {
            if (!started)
                files.TryClear();
        }
    }
}

internal sealed class UpdateFiles(string path)
{
    public string PreparePackage(string name)
    {
        Clear();
        Directory.CreateDirectory(path);
        return Path.Combine(path, name);
    }

    public void TryDeletePartAndPruneEmpty(string partPath)
    {
        try
        {
            if (File.Exists(partPath))
                File.Delete(partPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        try
        {
            // 仅在目录为空时删除 updates 目录，既满足失败/取消后的清理语义，又避免误删并发启动的新下载。
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path, recursive: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Clear()
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // 首次运行或之前已经清理。
        }
    }

    public void TryClear()
    {
        try
        {
            Clear();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 下载失败或取消后的善后清理不得覆盖原异常；占用中的文件交由后续 CleanupUpdates 重试。
        }
    }

    public static byte[] ReadChecksum(string text, string name)
    {
        string? match = null;
        foreach (string line in text.TrimStart('\uFEFF').Split('\n'))
        {
            var fields = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields is not [var hash, var filename] || filename.TrimStart('*') != name)
                continue;
            if (match is not null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("SHA256SUMS.txt 中的安装包校验值无效或重复。");
            match = hash;
        }

        return match is null
            ? throw new InvalidDataException("SHA256SUMS.txt 缺少安装包校验值。")
            : Convert.FromHexString(match);
    }

    /// <summary>重新核对之前下载好的安装包；文件被删除、改动或无法读取时返回 false，由调用方改为重新下载。</summary>
    public static bool TryReverify(VerifiedPackage package)
    {
        try
        {
            using var stream = File.OpenRead(package.Path);
            if (stream.Length != package.Size)
                return false;
            var actual = SHA256.HashData(stream);
            return actual.AsSpan().SequenceEqual(package.Sha256);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task VerifyAsync(string path, long size, byte[] checksum, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length != size)
            throw new InvalidDataException("安装包大小与发行记录不符。");
        var actual = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        if (!actual.AsSpan().SequenceEqual(checksum))
            throw new InvalidDataException("安装包的 SHA-256 校验失败。");
    }
}

internal sealed class UpdateClient(
    HttpClient http,
    IReadOnlyList<UpdateSource>? sources = null,
    TimeSpan? mirrorCheckTimeout = null,
    TimeProvider? timeProvider = null,
    TimeSpan? downloadStallTimeout = null)
{
    // C3：GitHub REST API 的 /releases/latest 端点按规范仅返回最新正式 Release，自动排除 draft 和 prerelease；
    // 客户端默认优先查询国内镜像（download.imfourj.cn），不可达或失败时自动回退 GitHub；
    // 无论来源为何，AvailableUpdate.Select 都会再次拒绝 draft/prerelease 及非规范三段版本标签，且要求新正式版高于当前版本（测试版可升级同号正式版）。
    public static readonly Uri Latest = UpdateSource.GitHub.LatestUri;
    public static readonly Uri MirrorLatest = UpdateSource.Mirror.LatestUri;
    public static readonly TimeSpan DefaultMirrorCheckTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DefaultDownloadStallTimeout = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<UpdateSource> sources = sources is { Count: > 0 }
        ? sources
        : [UpdateSource.Mirror, UpdateSource.GitHub];
    private readonly TimeSpan primaryCheckTimeout = mirrorCheckTimeout ?? DefaultMirrorCheckTimeout;
    private readonly TimeSpan stallTimeout = downloadStallTimeout ?? DefaultDownloadStallTimeout;
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            // 禁止自动跟随跳转：所有 3xx 重定向必须由 GetFollowAsync 逐跳校验 HTTPS 与源白名单。
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jige-Battery");
        return client;
    }

    public async Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken token,
        bool currentIsPrerelease = false)
    {
        bool anySucceeded = false;
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            bool hasFallback = i + 1 < sources.Count;
            // 有备用源或前序源已确认无新版时使用短预算（默认 8s）；末源/单源也受停滞预算（默认 30s）保护，避免无限挂死。
            TimeSpan budget = (hasFallback || anySucceeded) ? primaryCheckTimeout : stallTimeout;
            bool useAttemptTimeout = budget > TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan;
            using var attemptTimeout = useAttemptTimeout
                ? new CancellationTokenSource(budget, timeProvider)
                : null;
            using var attemptCts = attemptTimeout is not null
                ? CancellationTokenSource.CreateLinkedTokenSource(token, attemptTimeout.Token)
                : null;
            var attemptToken = attemptCts?.Token ?? token;

            try
            {
                var found = await CheckSourceAsync(source, current, currentIsPrerelease, attemptToken).ConfigureAwait(false);
                anySucceeded = true;
                // 若首选源已找到严格高于当前版本的新正式版，直接采用以保持镜像优先策略；
                // 仅当该源返回 null（可能镜像滞后）时继续向回退源确认是否存在更高版本。
                if (found is not null)
                    return found;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (attemptTimeout?.IsCancellationRequested == true && !(hasFallback || anySucceeded))
            {
                throw new TimeoutException($"检查 {source.Name} 更新超时。");
            }
            catch (Exception error) when ((hasFallback || anySucceeded) && IsFallbackEligible(error, token))
            {
                // 国内镜像超时、连接失败或返回无效内容时静默回退 GitHub；
                // 若首选源已成功确认无新版而回退源（如 GitHub）不可达，返回 null 而不误报错误。
            }
        }

        if (anySucceeded)
            return null;
        throw new InvalidOperationException("未配置有效的更新源。");
    }

    private async Task<AvailableUpdate?> CheckSourceAsync(UpdateSource source, Version current,
        bool currentIsPrerelease, CancellationToken token)
    {
        using var response = await GetFollowAsync(source.LatestUri, source, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidDataException($"{source.Name} 上暂时没有可访问的正式版本。");
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync(ReleaseJsonContext.Default.ReleaseResponse, token)
            .ConfigureAwait(false);
        return AvailableUpdate.Select(release, current, source, currentIsPrerelease);
    }

    public Task<string> DownloadAsync(AvailableUpdate update, UpdateFiles files, CancellationToken token) =>
        DownloadAsync(update, files, progress: null, token);

    public async Task<string> DownloadAsync(AvailableUpdate update, UpdateFiles files,
        IProgress<UpdateProgress>? progress, CancellationToken token) =>
        (await DownloadVerifiedAsync(update, files, progress, token).ConfigureAwait(false)).Path;

    public async Task<VerifiedPackage> DownloadVerifiedAsync(AvailableUpdate update, UpdateFiles files,
        IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        try
        {
            return await DownloadFromSourceAsync(update, files, progress, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (update.Fallback is { } fallback && IsFallbackEligible(error, token))
        {
            return await DownloadFromSourceAsync(fallback, files, progress, token).ConfigureAwait(false);
        }
    }

    private async Task<VerifiedPackage> DownloadFromSourceAsync(AvailableUpdate update, UpdateFiles files,
        IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        var source = update.EffectiveSource;
        string path = files.PreparePackage(update.Installer.Name!);
        string partPath = $"{path}.{Guid.NewGuid():N}.part";
        try
        {
            progress?.Report(new(0, update.Installer.Size, Verifying: false));
            using var checksums = new MemoryStream();
            await CopyAssetAsync(update.Checksums, source, checksums, onBytes: null, token).ConfigureAwait(false);
            string manifest = System.Text.Encoding.UTF8.GetString(checksums.GetBuffer(), 0, (int)checksums.Length);
            var hash = UpdateFiles.ReadChecksum(manifest, update.Installer.Name!);
            await using (var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true))
            {
                await CopyAssetAsync(update.Installer, source, file,
                    downloaded => progress?.Report(new(downloaded, update.Installer.Size, Verifying: false)),
                    token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
            }
            progress?.Report(new(update.Installer.Size, update.Installer.Size, Verifying: true));
            // 仅在 .part 临时文件完整通过大小与 SHA-256 校验后再原子重命名为最终 .exe，绝不落地未校验安装包。
            await UpdateFiles.VerifyAsync(partPath, update.Installer.Size, hash, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(partPath, path, overwrite: true);
            return new(path, update.Installer.Size, hash);
        }
        catch
        {
            files.TryDeletePartAndPruneEmpty(partPath);
            throw;
        }
    }

    private static bool IsFallbackEligible(Exception error, CancellationToken outerToken) => error switch
    {
        TimeoutException => true,
        OperationCanceledException => !outerToken.IsCancellationRequested,
        HttpRequestException or HttpIOException or JsonException or InvalidDataException or
            CryptographicException or UriFormatException => true,
        _ => false
    };

    private async Task<HttpResponseMessage> GetFollowAsync(Uri initialUrl, UpdateSource source,
        HttpCompletionOption completion, CancellationToken token)
    {
        var url = initialUrl;
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            var response = await http.GetAsync(url, completion, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 &&
                response.Headers.Location is { } location)
            {
                response.Dispose();
                url = new Uri(url, location);
                // 仅允许同一更新源白名单内的 HTTPS 默认端口跳转。
                if (url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
                    !source.IsAllowedHost(url.Host))
                    throw new InvalidDataException("发行资源跳转到了未知的下载地址。");
                continue;
            }
            return response;
        }
        throw new InvalidDataException("发行资源跳转次数过多。");
    }

    private async Task CopyAssetAsync(ReleaseAsset asset, UpdateSource source, Stream destination,
        Action<long>? onBytes, CancellationToken token)
    {
        bool useStall = stallTimeout > TimeSpan.Zero && stallTimeout != Timeout.InfiniteTimeSpan;
        using var headerTimeout = useStall ? new CancellationTokenSource(stallTimeout, timeProvider) : null;
        using var headerCts = headerTimeout is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(token, headerTimeout.Token)
            : null;
        var headerToken = headerCts?.Token ?? token;

        HttpResponseMessage response;
        Stream sourceStream;
        try
        {
            response = await GetFollowAsync(new Uri(asset.DownloadUrl!), source,
                HttpCompletionOption.ResponseHeadersRead, headerToken).ConfigureAwait(false);
            try
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
                    throw new InvalidDataException($"{asset.Name} 的下载大小与发行记录不符。");
                sourceStream = await response.Content.ReadAsStreamAsync(headerToken).ConfigureAwait(false);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && headerTimeout?.IsCancellationRequested == true)
        {
            throw new TimeoutException($"下载 {asset.Name} 响应超时。");
        }

        using var ownedResponse = response;
        await using var ownedStream = sourceStream;
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read;
            if (useStall)
            {
                using var readTimeout = new CancellationTokenSource(stallTimeout, timeProvider);
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(token, readTimeout.Token);
                try
                {
                    read = await ownedStream.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && readTimeout.IsCancellationRequested)
                {
                    throw new TimeoutException($"下载 {asset.Name} 连接停滞超时。");
                }
            }
            else
            {
                read = await ownedStream.ReadAsync(buffer, token).ConfigureAwait(false);
            }
            if (read <= 0)
                break;
            total += read;
            if (total > asset.Size)
                break;
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            onBytes?.Invoke(total);
        }
        if (total != asset.Size)
            throw new InvalidDataException($"{asset.Name} 的下载大小与发行记录不符。");
    }
}
