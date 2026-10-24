using System.ComponentModel.Composition;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>
/// Checks the public GitHub repository for a newer stable release.
/// </summary>
[Export(typeof(GitHubUpdateService))]
internal sealed class GitHubUpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/BENZOOgataga/WindowSill-NoClipBoardHistoryLimit/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _checked;

    internal event EventHandler? StatusChanged;

    internal string CurrentVersion { get; } = GetCurrentVersion();

    internal string? LatestVersion { get; private set; }

    internal Uri? LatestReleaseUri { get; private set; }

    internal bool IsChecking { get; private set; }

    internal bool CheckFailed { get; private set; }

    internal bool HasChecked => _checked;

    internal bool IsUpdateAvailable
        => LatestVersion is not null && CompareVersions(LatestVersion, CurrentVersion) > 0;

    internal async Task CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_checked && !force)
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
                return;
            }

            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                stream,
                JsonSerializerOptions.Web,
                cancellationToken);

            LatestVersion = release?.TagName?.TrimStart('v', 'V');
            LatestReleaseUri = Uri.TryCreate(release?.HtmlUrl, UriKind.Absolute, out Uri? uri) ? uri : null;
            _checked = true;
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
        [property: JsonPropertyName("html_url")] string HtmlUrl);
}
