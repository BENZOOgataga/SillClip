using WindowSill.ClipboardHistory;
using WindowSill.ClipboardHistory.Core;
using Xunit;

namespace UnitTests;

public sealed class ClipboardTests
{
    [Fact]
    public void LargeUnicodeAndRichPayloadsProduceStableSignatures()
    {
        var item = new PinnedClipboardItem
        {
            DataType = DetectedClipboardDataType.Text,
            Text = string.Concat(Enumerable.Repeat("Texte long éèà 😀\r\n", 100000)),
            Html = "<html><broken-tag>",
            Rtf = "{\\rtf1 malformed"
        };
        string signature = ClipboardSignature.Compute(item);
        Assert.Equal(64, signature.Length);
        Assert.Equal(signature, ClipboardSignature.Compute(item));
    }

    [Theory]
    [InlineData("v0.10.1-unlimited.10", "0.10.1-unlimited.2", true)]
    [InlineData("0.10.1", "0.10.1-unlimited.10", true)]
    [InlineData("0.10.1-unlimited.1", "0.10.1", false)]
    [InlineData("invalid", "0.10.1", false)]
    [InlineData("0.10.1+build2", "0.10.1+build1", false)]
    public void ReleaseComparisonUsesSemanticVersions(string candidate, string current, bool expected)
        => Assert.Equal(expected, ReleaseVersion.IsNewer(candidate, current));

    [Theory]
    [InlineData("AuditOnly9!", true, "********")]
    [InlineData("AuditOnly9!", false, "AuditOnly9!")]
    [InlineData("A normal sentence with spaces", true, "A normal sentence with spaces")]
    [InlineData("AuditOnly9_", true, "********")]
    public void PrivacyProtectsDisplayAndPreview(string text, bool hide, string expected)
        => Assert.Equal(expected, ClipboardPrivacy.Protect(text, hide));

    [Theory]
    [InlineData(DetectedClipboardDataType.Rtf)]
    [InlineData(DetectedClipboardDataType.Html)]
    [InlineData(DetectedClipboardDataType.Text)]
    [InlineData(DetectedClipboardDataType.Uri)]
    public void RichPayloadParticipatesInSignature(DetectedClipboardDataType type)
    {
        var first = new PinnedClipboardItem { DataType = type, Text = "same text", Html = "<b>a</b>", Rtf = "{a}" };
        var second = new PinnedClipboardItem { DataType = type, Text = "same text", Html = "<i>a</i>", Rtf = "{b}" };
        Assert.NotEqual(ClipboardSignature.Compute(first), ClipboardSignature.Compute(second));
    }

    [Fact]
    public void SignatureDoesNotDependOnCaptureDateOrIdentifier()
    {
        var first = new PinnedClipboardItem { DataType = DetectedClipboardDataType.Text, Text = "sample" };
        var second = new PinnedClipboardItem { DataType = DetectedClipboardDataType.Text, Text = "sample", PinnedAt = DateTimeOffset.MinValue };
        Assert.Equal(ClipboardSignature.Compute(first), ClipboardSignature.Compute(second));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unversioned-release")]
    public void InvalidReleaseTagsAreRejected(string? tag)
        => Assert.False(ReleaseVersion.IsValid(tag));

    [Fact]
    public void UnchangedWindowsHistoryDoesNotAlternateRetainedItems()
    {
        var old = new PinnedClipboardItem { Id = "old", ContentSignature = "old", PinnedAt = DateTimeOffset.UnixEpoch };
        var recent = new PinnedClipboardItem { Id = "new", ContentSignature = "new", PinnedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) };
        var archive = new Dictionary<string, PinnedClipboardItem>();
        for (int pass = 0; pass < 10; pass++)
        {
            foreach (var item in new[] { old, recent }) archive.TryAdd(item.Id, item);
            foreach (var expired in HistoryRetention.Expired(archive.Values, 1, new HashSet<string>())) archive.Remove(expired.Id);
            Assert.Equal("new", Assert.Single(archive).Key);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnlimitedDoesNotPrune(int limit)
        => Assert.Empty(HistoryRetention.Expired(Enumerable.Range(0, 10000).Select(_ => new PinnedClipboardItem()), limit, new HashSet<string>()));

    [Fact]
    public void PinsAreOutsideRetentionLimit()
    {
        var items = new[]
        {
            new PinnedClipboardItem { Id = "pin", ContentSignature = "pin", PinnedAt = DateTimeOffset.UnixEpoch },
            new PinnedClipboardItem { Id = "old", ContentSignature = "old", PinnedAt = DateTimeOffset.UnixEpoch },
            new PinnedClipboardItem { Id = "new", ContentSignature = "new", PinnedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) }
        };
        Assert.Equal("old", Assert.Single(HistoryRetention.Expired(items, 1, new HashSet<string> { "pin" })).Id);
    }

    [Theory]
    [InlineData(5000, 5000, 1560, 680)]
    [InlineData(-5000, -5000, 0, 0)]
    [InlineData(100, 100, 100, 100)]
    public void PopupStaysInsideWorkArea(int x, int y, int expectedX, int expectedY)
        => Assert.Equal((expectedX, expectedY), PopupPlacement.Clamp(x, y, 360, 400, 0, 0, 1920, 1080));

    [Fact]
    public void PopupHandlesRemovedAndSmallMonitors()
        => Assert.Equal((-800, 0), PopupPlacement.Clamp(-900, -20, 360, 400, -800, 0, 200, 300));

    [Fact]
    public void MigrationPreservesOriginalAndDoesNotOverwriteExistingData()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipboard-migration-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string legacy = Path.Combine(root, "WindowSill.ClipboardHistory", "History");
            string current = Path.Combine(root, "WindowSill.ClipboardHistory.NoLimits");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "example.dat"), "encrypted-example");
            ClipboardDataMigration.Prepare(current);
            Assert.True(File.Exists(Path.Combine(legacy, "example.dat")));
            string target = Path.Combine(current, "History", "example.dat");
            Assert.Equal("encrypted-example", File.ReadAllText(target));
            File.WriteAllText(target, "newer-encrypted-example");
            ClipboardDataMigration.Prepare(current);
            Assert.Equal("newer-encrypted-example", File.ReadAllText(target));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ForkUsesSeparateDataFolderWhenSdkKeysStorageByAssemblyName()
    {
        string sdkFolder = Path.Combine(Path.GetTempPath(), "PluginsData", "WindowSill.ClipboardHistory");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "PluginsData", "WindowSill.ClipboardHistory.NoLimits"),
            ClipboardDataMigration.ForkFolder(sdkFolder));
    }
}
