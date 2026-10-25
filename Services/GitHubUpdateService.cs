using System.ComponentModel.Composition;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>
/// Checks the public GitHub repository for a newer installable release.
/// </summary>
[Export(typeof(GitHubUpdateService))]
internal sealed class GitHubUpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/BENZOOgataga/WindowSill-NoClipBoardHistoryLimit/releases?per_page=100";

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _checked;
    private DateTimeOffset _lastCheck;
    private Timer? _timer;
    private int _subscribers;

    internal void Start()
    {
        if (Interlocked.Increment(ref _subscribers) == 1)
            _timer = new Timer(_ => CheckAsync().Forget(), null, TimeSpan.Zero, TimeSpan.FromHours(6));
    }

    internal void Stop()
    {
        if (Interlocked.Decrement(ref _subscribers) == 0)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    internal event EventHandler? StatusChanged;

    internal string CurrentVersion { get; } = GetCurrentVersion();

    internal string? LatestVersion { get; private set; }

    internal Uri? LatestReleaseUri { get; private set; }

    internal bool IsChecking { get; private set; }

    internal bool CheckFailed { get; private set; }

    internal bool HasChecked => _checked;

    internal bool IsUpdateAvailable
        => LatestVersion is not null && ReleaseVersion.IsNewer(LatestVersion, CurrentVersion);

    internal async Task CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_checked && !force && DateTimeOffset.UtcNow - _lastCheck < TimeSpan.FromHours(6))
            {
                return;
            }

            IsChecking = true;
            CheckFailed = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);

            using HttpResponseMessage response = await HttpClient.GetAsync(LatestReleaseApi, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                LatestVersion = null;
                LatestReleaseUri = null;
                _checked = true;
                _lastCheck = DateTimeOffset.UtcNow;
                return;
            }

            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            GitHubRelease[] releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(
                stream,
                JsonSerializerOptions.Web,
                cancellationToken) ?? [];

            GitHubRelease? release = null;
            foreach (var candidate in releases)
            {
                if (!ReleaseVersion.IsValid(candidate.TagName)) continue;
                if (candidate.Draft || (candidate.Prerelease && !ReleaseVersion.IsPrerelease(CurrentVersion))) continue;
                if (candidate.Assets?.Any(asset => asset.Name.StartsWith("WindowSill.ClipboardHistory.NoLimits.", StringComparison.Ordinal)
                    && asset.Name.EndsWith(".wsext", StringComparison.OrdinalIgnoreCase)) != true) continue;
                if (release is null || ReleaseVersion.IsNewer(candidate.TagName, release.TagName)) release = candidate;
            }

            LatestVersion = release?.TagName?.TrimStart('v', 'V');
            LatestReleaseUri = Uri.TryCreate(release?.HtmlUrl, UriKind.Absolute, out Uri? uri)
                && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com"
                && uri.AbsolutePath.StartsWith("/BENZOOgataga/WindowSill-NoClipBoardHistoryLimit/releases/", StringComparison.OrdinalIgnoreCase) ? uri : null;
            _checked = true;
            _lastCheck = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            CheckFailed = true;
        }
        catch (HttpRequestException)
        {
            CheckFailed = true;
        }
        catch (JsonException)
        {
            CheckFailed = true;
        }
        finally
        {
            IsChecking = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            _semaphore.Release();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WindowSill-NoClipboardHistoryLimit/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string GetCurrentVersion()
    {
        Assembly assembly = typeof(GitHubUpdateService).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return (informationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0")
            .Split('+')[0];
    }

    private static int CompareVersions(string left, string right)
    {
        ParseVersion(left, out Version leftCore, out string? leftLabel);
        ParseVersion(right, out Version rightCore, out string? rightLabel);

        int coreComparison = leftCore.CompareTo(rightCore);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (leftLabel is null && rightLabel is not null)
        {
            return 1;
        }

        if (leftLabel is not null && rightLabel is null)
        {
            return -1;
        }

        return CompareLabels(leftLabel, rightLabel);
    }

    private static int CompareLabels(string? left, string? right)
    {
        string[] leftParts = (left ?? string.Empty).Split('.', '-');
        string[] rightParts = (right ?? string.Empty).Split('.', '-');

        for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            bool leftIsNumber = int.TryParse(leftParts[index], out int leftNumber);
            bool rightIsNumber = int.TryParse(rightParts[index], out int rightNumber);

            int comparison = leftIsNumber && rightIsNumber
                ? leftNumber.CompareTo(rightNumber)
                : string.Compare(leftParts[index], rightParts[index], StringComparison.OrdinalIgnoreCase);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static void ParseVersion(string value, out Version core, out string? label)
    {
        string normalized = value.Trim().TrimStart('v', 'V').Split('+')[0];
        string[] parts = normalized.Split('-', 2);
        core = Version.TryParse(parts[0], out Version? parsed) ? parsed : new Version(0, 0);
        label = parts.Length > 1 ? parts[1] : null;
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] GitHubAsset[]? Assets);

    private sealed record GitHubAsset([property: JsonPropertyName("name")] string Name);
}
