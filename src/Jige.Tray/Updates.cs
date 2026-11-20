using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Jige.Tray;

internal static class ReleaseVersion
{
    public static string Current { get; } =
        typeof(ReleaseVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+', 2)[0];

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

internal sealed record AvailableUpdate(Version Version, string Notes, Uri Page, ReleaseAsset Installer, ReleaseAsset Checksums)
{
    public const string Repository = "https://github.com/deepfry666/jige-battery";
    public const string ChecksumsName = "SHA256SUMS.txt";
    public const int MaxChecksumsSize = 64 * 1024;
    private const long MaxInstallerSize = 512L * 1024 * 1024;

    public static AvailableUpdate? Select(ReleaseResponse? release, Version current)
    {
        if (release is null || release.Draft || release.Prerelease ||
            release.Tag is not { Length: > 1 } tag || tag[0] != 'v' ||
            !ReleaseVersion.TryParse(tag[1..], out var version))
            throw new InvalidDataException("GitHub 没有返回有效的正式版本。");
        if (version <= current)
            return null;

        var installer = FindAsset($"Jige-Setup-{version}-x64.exe", MaxInstallerSize);
        var checksums = FindAsset(ChecksumsName, MaxChecksumsSize);
        var notes = release.Body?.Trim();
        if (string.IsNullOrEmpty(notes))
            notes = "此版本未附更新说明。";
        else
            notes = Presentation.Shorten(notes, 1600);

        return new(version, notes, new($"{Repository}/releases/tag/{tag}"), installer, checksums);

        ReleaseAsset FindAsset(string name, long maxSize)
        {
            var matches = release.Assets?.OfType<ReleaseAsset>().Where(asset => asset.Name == name).ToArray();
            if (matches is not [var match] || match.Size <= 0 || match.Size > maxSize ||
                match.DownloadUrl != $"{Repository}/releases/download/{tag}/{name}")
                throw new InvalidDataException($"发行版缺少有效的 {name}。");
            return match;
        }
    }
}

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
                files.Clear();
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

    public void Clear()
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // 首次运行或之前已清理。
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

internal sealed class UpdateClient(HttpClient http)
{
    // C3：GitHub REST API 的 /releases/latest 端点按规范仅返回最新正式 Release，自动排除 draft 和 prerelease；
    // 客户端在 AvailableUpdate.Select 中仍会再次拒绝 draft/prerelease 及非规范三段版本标签，且要求新正式版严格高于当前基础版本。
    public static readonly Uri Latest = new("https://api.github.com/repos/deepfry666/jige-battery/releases/latest");

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jige-Battery");
        return client;
    }

    public async Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken token)
    {
        using var response = await http.GetAsync(Latest, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidDataException("GitHub 上暂时没有可访问的正式版本。");
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync(ReleaseJsonContext.Default.ReleaseResponse, token)
            .ConfigureAwait(false);
        return AvailableUpdate.Select(release, current);
    }

    public Task<string> DownloadAsync(AvailableUpdate update, UpdateFiles files, CancellationToken token) =>
        DownloadAsync(update, files, progress: null, token);

    public async Task<string> DownloadAsync(AvailableUpdate update, UpdateFiles files,
        IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        string path = files.PreparePackage(update.Installer.Name!);
        try
        {
            progress?.Report(new(0, update.Installer.Size, Verifying: false));
            using var checksums = new MemoryStream();
            await CopyAssetAsync(update.Checksums, checksums, onBytes: null, token).ConfigureAwait(false);
            string manifest = System.Text.Encoding.UTF8.GetString(checksums.GetBuffer(), 0, (int)checksums.Length);
            var hash = UpdateFiles.ReadChecksum(manifest, update.Installer.Name!);
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true))
            {
                await CopyAssetAsync(update.Installer, file,
                    downloaded => progress?.Report(new(downloaded, update.Installer.Size, Verifying: false)),
                    token).ConfigureAwait(false);
            }
            progress?.Report(new(update.Installer.Size, update.Installer.Size, Verifying: true));
            await UpdateFiles.VerifyAsync(path, update.Installer.Size, hash, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return path;
        }
        catch
        {
            files.Clear();
            throw;
        }
    }

    private async Task CopyAssetAsync(ReleaseAsset asset, Stream destination, Action<long>? onBytes, CancellationToken token)
    {
        var url = new Uri(asset.DownloadUrl!);
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 &&
                response.Headers.Location is { } location)
            {
                url = new Uri(url, location);
                // GitHub 下载地址会跳转到存放发行资源的 GitHub CDN。
                if (url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
                    url.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new InvalidDataException("发行资源跳转到了未知的下载地址。");
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
                throw new InvalidDataException($"{asset.Name} 的下载大小与发行记录不符。");
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > asset.Size)
                    break;
                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                onBytes?.Invoke(total);
            }
            if (total != asset.Size)
                throw new InvalidDataException($"{asset.Name} 的下载大小与发行记录不符。");
            return;
        }
        throw new InvalidDataException("发行资源的下载跳转次数过多。");
    }
}
