using NuGet.Versioning;

namespace WindowSill.ClipboardHistory.Core;

internal static class ReleaseVersion
{
    internal static bool IsValid(string? version)
        => version is not null && NuGetVersion.TryParse(version.TrimStart('v', 'V'), out _);

    internal static bool IsNewer(string candidate, string current)
        => NuGetVersion.TryParse(candidate.TrimStart('v', 'V'), out var next)
        && NuGetVersion.TryParse(current.TrimStart('v', 'V'), out var installed)
        && VersionComparer.VersionRelease.Compare(next, installed) > 0;

    internal static bool IsPrerelease(string version)
        => NuGetVersion.TryParse(version.TrimStart('v', 'V'), out var parsed) && parsed.IsPrerelease;
}
