namespace WindowSill.ClipboardHistory.Core;

internal static class ReleaseRepository
{
    internal const string ReleasesApi = "https://api.github.com/repos/BENZOOgataga/SillClip/releases?per_page=100";

    internal static Uri? GetReleaseUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com"
        && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.AbsolutePath.StartsWith("/BENZOOgataga/SillClip/releases/", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.StartsWith("/BENZOOgataga/WindowSill-NoClipBoardHistoryLimit/releases/", StringComparison.OrdinalIgnoreCase))
            ? uri : null;
}
