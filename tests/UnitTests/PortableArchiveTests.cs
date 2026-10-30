using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using WindowSill.ClipboardHistory;
using WindowSill.ClipboardHistory.Core;
using Xunit;

namespace UnitTests;

public sealed class PortableArchiveTests
{
    private const string Passphrase = "sample backup passphrase 2026";
    private static PortableClipboardEntry Text(string value = "example") => new()
    {
        Item = new() { Text = value, DataType = DetectedClipboardDataType.Text, PinnedAt = DateTimeOffset.UnixEpoch },
        Collections = ["Project samples"]
    };

    private static async IAsyncEnumerable<PortableClipboardEntry> Entries(PortableClipboardEntry[] items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items) { cancellationToken.ThrowIfCancellationRequested(); yield return item; await Task.Yield(); }
    }

    private static async Task<byte[]> WriteAsync(params PortableClipboardEntry[] items)
    {
        using var stream = new MemoryStream();
        await PortableClipboardArchive.WriteAsync(stream, Passphrase, Entries(items));
        return stream.ToArray();
    }

    private static async Task<List<PortableClipboardEntry>> ReadAsync(byte[] bytes, string password = Passphrase)
    {
        var staged = new List<PortableClipboardEntry>();
        using var stream = new MemoryStream(bytes);
        await PortableClipboardArchive.ReadToStagingAsync(stream, password, (entry, _) => { staged.Add(entry); return Task.CompletedTask; });
        return staged;
    }

    [Fact]
    public async Task RoundTripPreservesContentPinsDatesCollectionsAndImages()
    {
        var text = Text("Unicode: café, 日本語, 😀"); text.IsPinned = true;
        var image = new PortableClipboardEntry { Item = new() { DataType = DetectedClipboardDataType.Image, HasImage = true }, Image = [1, 2, 3, 4] };
        var restored = await ReadAsync(await WriteAsync(text, image));
        Assert.Equal(text.Item.Text, restored[0].Item.Text);
        Assert.True(restored[0].IsPinned);
        Assert.Equal(DateTimeOffset.UnixEpoch, restored[0].Item.PinnedAt);
        Assert.Equal(text.Collections, restored[0].Collections);
        Assert.Equal(image.Image, restored[1].Image);
    }

    [Fact]
    public async Task ArchiveBytesDoNotContainPlaintextAndEachExportIsRandomized()
    {
        byte[] first = await WriteAsync(Text("private synthetic sample"));
        byte[] second = await WriteAsync(Text("private synthetic sample"));
        Assert.False(first.SequenceEqual(second));
        Assert.DoesNotContain("private synthetic sample", System.Text.Encoding.UTF8.GetString(first));
    }

    [Fact]
    public async Task WrongPasswordDoesNotProduceAnyCommittedEntries()
    {
        byte[] archive = await WriteAsync(Text());
        await Assert.ThrowsAnyAsync<CryptographicException>(() => ReadAsync(archive, "different passphrase"));
    }

    [Fact]
    public async Task EmptyArchivesStillAuthenticateThePassword()
    {
        byte[] archive = await WriteAsync();
        Assert.Empty(await ReadAsync(archive));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => ReadAsync(archive, "wrong password"));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(45)]
    public async Task SaltFileIdentityAndCiphertextAreAuthenticated(int offset)
    {
        byte[] archive = await WriteAsync(Text()); archive[offset] ^= 0x40;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => ReadAsync(archive));
    }

    [Fact]
    public async Task TruncationAtAnEntryBoundaryCannotLookLikeACompleteArchive()
    {
        byte[] archive = await WriteAsync(Text());
        int firstLength = BinaryPrimitives.ReadInt32BigEndian(archive.AsSpan(41));
        byte[] truncated = archive[..(40 + 5 + firstLength + 16)];
        await Assert.ThrowsAsync<EndOfStreamException>(() => ReadAsync(truncated));
    }

    [Fact]
    public async Task CorruptedFooterAndTrailingGarbageAreRejected()
    {
        byte[] archive = await WriteAsync(Text());
        byte[] corrupt = archive.ToArray(); corrupt[^1] ^= 1;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => ReadAsync(corrupt));
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync([.. archive, 0]));
    }

    [Fact]
    public async Task ReorderedRecordsAreRejected()
    {
        byte[] archive = await WriteAsync(Text("one"), Text("two"));
        int firstSize = 5 + BinaryPrimitives.ReadInt32BigEndian(archive.AsSpan(41)) + 16;
        int secondOffset = 40 + firstSize;
        int secondSize = 5 + BinaryPrimitives.ReadInt32BigEndian(archive.AsSpan(secondOffset + 1)) + 16;
        byte[] reordered = [.. archive.AsSpan(0, 40), .. archive.AsSpan(secondOffset, secondSize), .. archive.AsSpan(40, firstSize), .. archive.AsSpan(secondOffset + secondSize)];
        await Assert.ThrowsAnyAsync<CryptographicException>(() => ReadAsync(reordered));
    }

    [Fact]
    public async Task UntrustedRecordLengthIsRejectedBeforeAllocation()
    {
        byte[] archive = await WriteAsync(Text());
        BinaryPrimitives.WriteInt32BigEndian(archive.AsSpan(41), int.MaxValue);
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(archive));
    }

    [Fact]
    public void ImportNeverReusesArchiveIdsSignaturesOrActiveApplicationMetadata()
    {
        var entry = Text(); entry.Item.Id = "../../outside"; entry.Item.ContentSignature = "forged";
        entry.Item.ApplicationLink = "custom:perform-action"; entry.Item.UserActivity = "{}";
        Assert.True(entry.NormalizeForImport(false));
        Assert.True(Guid.TryParseExact(entry.Item.Id, "N", out _));
        Assert.NotEqual("forged", entry.Item.ContentSignature);
        Assert.Null(entry.Item.ApplicationLink); Assert.Null(entry.Item.UserActivity);
    }

    [Fact]
    public void FileReferencesRequireOptInAndAreRestoredAsInertText()
    {
        var entry = new PortableClipboardEntry { Item = new() { DataType = DetectedClipboardDataType.File, FilePaths = ["C:\\Samples\\example.txt"] } };
        Assert.False(entry.NormalizeForImport(false));
        Assert.True(entry.NormalizeForImport(true));
        Assert.Equal(DetectedClipboardDataType.Text, entry.Item.DataType);
        Assert.Null(entry.Item.FilePaths);
        Assert.Contains("example.txt", entry.Item.Text);
    }

    [Theory]
    [InlineData("\\\\server\\share\\example.txt")]
    [InlineData("\\\\?\\C:\\example.txt")]
    [InlineData("relative.txt")]
    public void RemoteDeviceAndRelativePathsCannotBeRestored(string path)
    {
        var entry = new PortableClipboardEntry { Item = new() { DataType = DetectedClipboardDataType.File, FilePaths = [path] } };
        Assert.Throws<InvalidDataException>(() => entry.NormalizeForImport(true));
    }
}
