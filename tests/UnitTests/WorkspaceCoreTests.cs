using WindowSill.ClipboardHistory.Core;
using Xunit;

namespace UnitTests;

public sealed class WorkspaceCoreTests
{
    private static HistorySearchEntry Entry(string text = "alpha beta", string type = "Text", bool pinned = false, params string[] collections)
        => new(text, type, new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), pinned, collections);

    [Theory]
    [InlineData("alpha beta", true)]
    [InlineData("\"alpha beta\"", true)]
    [InlineData("\"beta alpha\"", false)]
    [InlineData("type:text", true)]
    [InlineData("type:image", false)]
    [InlineData("after:2026-01-15 before:2026-01-15", true)]
    [InlineData("after:2026-01-16", false)]
    [InlineData("before:2026-01-14", false)]
    [InlineData("pinned:false", true)]
    [InlineData("pinned:true", false)]
    public void SearchCombinesTermsAndFilters(string query, bool expected)
        => Assert.Equal(expected, HistoryQuery.Parse(query).Matches(Entry()));

    [Theory]
    [InlineData("after:2026-02-30")]
    [InlineData("before:01/02/2026")]
    [InlineData("after:2026-01-16 before:2026-01-15")]
    [InlineData("pinned:perhaps")]
    [InlineData("type:exe")]
    [InlineData("collection:")]
    [InlineData("\"")]
    public void InvalidQueriesHaveFeedbackAndDoNotReturnUnfilteredSecrets(string text)
    {
        var query = HistoryQuery.Parse(text);
        Assert.NotNull(query.Error);
        Assert.False(query.Matches(Entry()));
    }

    [Fact]
    public void QuotedCollectionNamesCanContainSpacesAndQuotes()
        => Assert.True(HistoryQuery.Parse("collection:\"Team \\\"Blue\\\"\" type:html").Matches(Entry(type: "Html", collections: ["Team \"Blue\""])));

    [Fact]
    public void TypeAlternativesAreOrAndTextIncludesRichText()
    {
        Assert.True(HistoryQuery.Parse("type:text type:image").Matches(Entry(type: "Image")));
        Assert.True(HistoryQuery.Parse("type:text").Matches(Entry(type: "Rtf")));
    }

    [Theory]
    [InlineData("hello\r\n  world\t!", "hello world !")]
    [InlineData("  a\u00a0b  ", "a b")]
    public void SingleLineCollapsesWhitespace(string input, string expected)
        => Assert.Equal(expected, ClipboardTextTransforms.Apply(input, ClipboardTextTransform.SingleLine));

    [Fact]
    public void UrlCleanupPreservesUsefulParametersEncodingAndFragments()
        => Assert.Equal("https://example.test/path?q=a%20b&id=42#part", ClipboardTextTransforms.Apply(
            "https://example.test/path?utm_source=news&q=a%20b&fbclid=secret&id=42#part", ClipboardTextTransform.CleanUrl));

    [Fact]
    public void UrlCleanupHandlesEncodedTrackingNames()
        => Assert.Equal("https://example.test/#part", ClipboardTextTransforms.Apply("https://example.test/?%75tm_campaign=x#part", ClipboardTextTransform.CleanUrl));

    [Fact]
    public void UrlCleanupRejectsApplicationProtocols()
        => Assert.Throws<FormatException>(() => ClipboardTextTransforms.Apply("file:///C:/example.txt", ClipboardTextTransform.CleanUrl));

    [Fact]
    public void JsonFormattingPreservesNumericPrecision()
        => Assert.Contains("123456789012345678901234567890", ClipboardTextTransforms.Apply("{\"value\":123456789012345678901234567890}", ClipboardTextTransform.FormattedJson));

    [Fact]
    public void InvalidJsonDoesNotProduceAReplacementClipboardValue()
        => Assert.ThrowsAny<System.Text.Json.JsonException>(() => ClipboardTextTransforms.Apply("not json", ClipboardTextTransform.FormattedJson));

    [Fact]
    public void DiffFindsAddedAndRemovedLines()
    {
        var result = TextComparison.Compare("same\r\nold", "same\nnew");
        Assert.Equal(1, result.Added); Assert.Equal(1, result.Removed);
        Assert.Contains("- old", result.Diff); Assert.Contains("+ new", result.Diff);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void DiffHandlesEmptyDocuments()
    {
        Assert.Equal(string.Empty, TextComparison.Compare("", "").Diff);
        var result = TextComparison.Compare("", "new");
        Assert.Equal(1, result.Added); Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void LargeComparisonIsBoundedAndMarkedAsPartial()
    {
        var result = TextComparison.Compare(new string('a', 1_000_000), new string('b', 1_000_000));
        Assert.True(result.IsTruncated); Assert.InRange(result.Diff.Length, 1, 300_000);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => TextComparison.Compare("", "", cancellation.Token));
    }

    [Fact]
    public void PauseIntervalsExcludeCopiesAfterResumeAndAcrossSerialization()
    {
        var start = DateTimeOffset.UtcNow;
        var state = new LibraryState();
        state.Pause(start, TimeSpan.FromMinutes(15));
        state.Resume(start.AddMinutes(3));
        var restored = System.Text.Json.JsonSerializer.Deserialize<LibraryState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        Assert.True(restored.AllowsCapture(start.AddTicks(-1)));
        Assert.False(restored.AllowsCapture(start));
        Assert.False(restored.AllowsCapture(start.AddMinutes(2)));
        Assert.True(restored.AllowsCapture(start.AddMinutes(3)));
    }

    [Fact]
    public void TimedPauseExpiresWithoutNeedingToRewriteMetadata()
    {
        var start = DateTimeOffset.UtcNow;
        var state = new LibraryState(); state.Pause(start, TimeSpan.FromHours(1));
        Assert.NotNull(state.PausedUntil(start.AddMinutes(59)));
        Assert.Null(state.PausedUntil(start.AddHours(1)));
        Assert.False(state.AllowsCapture(start.AddMinutes(59)));
        Assert.True(state.AllowsCapture(start.AddHours(1)));
    }

    [Fact]
    public void ExplicitRestoreCanBringBackOldItemsWithoutReenablingAllClearedCopies()
    {
        var state = new LibraryState { ClearedAt = DateTimeOffset.UtcNow };
        var oldDate = state.ClearedAt.AddDays(-10);
        Assert.False(state.AllowsItem("restored", oldDate));
        state.Restored["restored"] = oldDate;
        Assert.True(state.AllowsItem("restored", oldDate));
        Assert.False(state.AllowsItem("other", oldDate));
        state.Deleted["restored"] = DateTimeOffset.UtcNow;
        Assert.False(state.AllowsItem("restored", oldDate));
    }
}
