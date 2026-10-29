using WindowSill.ClipboardHistory.Core;
using Xunit;

namespace UnitTests;

public sealed class ReleaseRepositoryTests
{
    [Theory]
    [InlineData("https://github.com/BENZOOgataga/SillClip/releases/tag/v0.10.4", true)]
    [InlineData("https://github.com/BENZOOgataga/WindowSill-NoClipBoardHistoryLimit/releases/tag/v0.10.3", true)]
    [InlineData("https://github.com/benzoogataga/sillclip/releases/tag/v0.10.4", true)]
    [InlineData("https://github.com/BENZOOgataga/SillClip-extra/releases/tag/v0.10.4", false)]
    [InlineData("https://github.com/another-owner/SillClip/releases/tag/v0.10.4", false)]
    [InlineData("https://github.com/BENZOOgataga/another-repo/releases/tag/v0.10.4", false)]
    [InlineData("https://github.com.example.org/BENZOOgataga/SillClip/releases/tag/v0.10.4", false)]
    [InlineData("http://github.com/BENZOOgataga/SillClip/releases/tag/v0.10.4", false)]
    [InlineData("https://someone@github.com/BENZOOgataga/SillClip/releases/tag/v0.10.4", false)]
    [InlineData("https://github.com:444/BENZOOgataga/SillClip/releases/tag/v0.10.4", false)]
    [InlineData("https://github.com/BENZOOgataga/SillClip/releases/../../another-repo/releases/tag/v0.10.4", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReleaseLinksStayWithinTheCurrentOrLegacyRepository(string? value, bool expected)
        => Assert.Equal(expected, ReleaseRepository.GetReleaseUri(value) is not null);
}
