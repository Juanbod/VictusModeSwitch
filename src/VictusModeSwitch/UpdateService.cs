using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VictusModeSwitch;

internal sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/Juanbod/VictusModeSwitch";
    public const string InstallerAssetName = "VictusModeSwitch-Setup.exe";
    public const string ChecksumAssetName = "VictusModeSwitch-Setup.exe.sha256";

    private const string LatestReleaseApi =
        "https://api.github.com/repos/Juanbod/VictusModeSwitch/releases/latest";
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(LatestReleaseApi, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("GitHub returned an empty release response.");
            var version = ParseVersion(release.TagName)
                ?? throw new InvalidOperationException($"Invalid release tag '{release.TagName}'.");
            var installer = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, InstallerAssetName, StringComparison.OrdinalIgnoreCase));
            var checksum = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase));

            if (version <= AppVersion.Current)
            {
                return UpdateCheckResult.UpToDate(version);
            }

            if (installer is null || checksum is null)
            {
                throw new InvalidOperationException("The release does not contain a verified installer.");
            }

            return UpdateCheckResult.Available(new UpdateInfo(
                version,
                release.TagName,
                release.HtmlUrl,
                release.Body ?? string.Empty,
                installer.DownloadUrl,
                checksum.DownloadUrl));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Error("Не удалось проверить обновления", exception);
            return UpdateCheckResult.Failed(exception.Message);
        }
    }

    public async Task<string> DownloadVerifiedInstallerAsync(
        UpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var versionDirectory = Path.Combine(AppPaths.UpdateDirectory, update.TagName);
        Directory.CreateDirectory(versionDirectory);
        var installerPath = Path.Combine(versionDirectory, InstallerAssetName);
        var temporaryPath = installerPath + ".download";
        var checksumPath = installerPath + ".sha256";

        using (var response = await Client.GetAsync(
                   update.InstallerUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                if (length is > 0)
                {
                    progress?.Report((int)Math.Clamp(total * 100 / length.Value, 0, 100));
                }
            }
        }

        var checksumText = await Client.GetStringAsync(update.ChecksumUrl, cancellationToken);
        var expectedHash = ParseChecksum(checksumText)
            ?? throw new InvalidOperationException("The release checksum is invalid.");
        await using (var stream = File.OpenRead(temporaryPath))
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash),
                    Convert.FromHexString(actualHash)))
            {
                File.Delete(temporaryPath);
                throw new InvalidOperationException("The installer SHA-256 checksum does not match.");
            }
        }

        File.Move(temporaryPath, installerPath, true);
        File.WriteAllText(checksumPath, $"{expectedHash.ToLowerInvariant()}  {InstallerAssetName}\n");
        return installerPath;
    }

    public static void QueueInstallerAfterExit(string installerPath)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--install-update");
        startInfo.ArgumentList.Add(Path.GetFullPath(installerPath));
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows could not start the update helper.");
    }

    public static void InstallAfterParentExit(string installerPath, int parentProcessId)
    {
        var fullPath = Path.GetFullPath(installerPath);
        var updateRoot = Path.GetFullPath(AppPaths.UpdateDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(fullPath), InstallerAssetName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The installer path is outside the verified update directory.");
        }

        VerifyInstaller(fullPath);
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            if (!parent.WaitForExit(20000))
            {
                throw new TimeoutException("The running app did not exit before the update timeout.");
            }
        }
        catch (ArgumentException)
        {
            // The parent process has already exited.
        }

        Thread.Sleep(350);
        StartInstaller(fullPath);
    }

    public static void CleanupDownloads()
    {
        var root = Path.GetFullPath(AppPaths.UpdateDirectory);
        if (!Directory.Exists(root))
        {
            return;
        }

        var guardedRoot = root + Path.DirectorySeparatorChar;
        var removed = 0;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                var fullPath = Path.GetFullPath(directory);
                if (!fullPath.StartsWith(guardedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.Delete(fullPath, recursive: true);
                removed++;
            }
            catch (Exception exception)
            {
                Log.Warning($"Could not remove an old update download: {exception.Message}");
            }
        }

        if (removed > 0)
        {
            Log.Info($"Removed {removed} old update download folder(s)");
        }
    }

    internal static void VerifyInstaller(string installerPath)
    {
        var expectedHash = ParseChecksum(File.ReadAllText(installerPath + ".sha256"))
            ?? throw new InvalidOperationException("The saved installer checksum is invalid.");
        using var stream = File.OpenRead(installerPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedHash),
                Convert.FromHexString(actualHash)))
        {
            throw new InvalidOperationException("The installer changed after verification.");
        }
    }

    private static void StartInstaller(string installerPath)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART",
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows could not start the installer.");
    }

    internal static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var value = tag.Trim().TrimStart('v', 'V');
        var suffix = value.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0)
        {
            value = value[..suffix];
        }

        return Version.TryParse(value, out var version) ? version : null;
    }

    internal static string? ParseChecksum(string? value)
    {
        var token = value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token?.Length != 64 || token.Any(character => !Uri.IsHexDigit(character)))
        {
            return null;
        }

        return token.ToUpperInvariant();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VictusModeSwitch", AppVersion.Display));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = RepositoryUrl;

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; init; } = string.Empty;
    }
}

internal enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Failed
}

internal sealed record UpdateInfo(
    Version Version,
    string TagName,
    string ReleaseUrl,
    string ReleaseNotes,
    string InstallerUrl,
    string ChecksumUrl);

internal sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? LatestVersion,
    UpdateInfo? Update,
    string? Error)
{
    public static UpdateCheckResult UpToDate(Version version) =>
        new(UpdateCheckStatus.UpToDate, version, null, null);

    public static UpdateCheckResult Available(UpdateInfo update) =>
        new(UpdateCheckStatus.Available, update.Version, update, null);

    public static UpdateCheckResult Failed(string error) =>
        new(UpdateCheckStatus.Failed, null, null, error);
}
