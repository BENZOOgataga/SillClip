using Microsoft.Extensions.Logging.Abstractions;
using WindowSill.ClipboardHistory;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.Services;
using Xunit;
using Path = System.IO.Path;

namespace IntegrationTests;

/// <summary>Exercises actual DPAPI stores and workspace transactions in isolated folders. Never accesses the OS clipboard.</summary>
public sealed class WorkspaceIntegrationTests
{
    private const string Password = "Synthetic test passphrase 123!";

    [Fact]
    public async Task BackupRoundTripRestoresPayloadsPinsCollectionsAndDeduplicates()
    {
        using var source = await Library.CreateAsync();
        var text = Text("synthetic sample");
        var pin = Text("synthetic pin");
        await source.History.ImportAsync(text, default);
        await source.Pins.ImportAsync(pin, default);
        await source.State.CreateCollectionAsync("Project");
        await source.State.AssignAsync([text.ContentSignature, pin.ContentSignature], "Project", false);
        string backup = Path.Combine(source.Root, "test.sillclip");
        Assert.Equal(2, await source.Workspace.ExportAsync(backup, Password, false));

        using var target = await Library.CreateAsync();
        var first = await target.Workspace.ImportAsync(backup, Password, false);
        Assert.Equal(2, first.Added);
        Assert.Single(target.Pins.GetPinnedItems());
        Assert.Single(target.History.GetItems());
        Assert.All(target.Workspace.GetItems(), entry => Assert.Contains("Project", target.State.CollectionsFor(entry.ContentSignature)));
        Assert.Equal(text.Text, ((PersistentClipboardItemSource)Assert.Single(target.History.GetItems()).Source).Model.Text);
        var second = await target.Workspace.ImportAsync(backup, Password, false);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, second.Duplicates);
        Assert.Empty(Directory.GetDirectories(Path.Combine(target.Root, "Transfers")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauthenticatedBackupLeavesAllExistingFilesAndMetadataUnchanged(bool truncate)
    {
        using var library = await Library.CreateAsync();
        await library.History.ImportAsync(Text("existing entry"), default);
        await library.State.CreateCollectionAsync("Keep");
        string backup = Path.Combine(library.Root, "test.sillclip");
        await library.Workspace.ExportAsync(backup, Password, false);
        if (truncate)
        {
            byte[] bytes = await File.ReadAllBytesAsync(backup);
            await File.WriteAllBytesAsync(backup, bytes[..^1]);
        }
        var before = Snapshot(library.Root);
        await Assert.ThrowsAnyAsync<Exception>(() => library.Workspace.ImportAsync(backup, truncate ? Password : "wrong password", false));
        var after = Snapshot(library.Root);
        Assert.Equal(before.Keys, after.Keys);
        foreach (string path in before.Keys) Assert.Equal(before[path], after[path]);
        Assert.Single(library.History.GetItems());
        Assert.Equal(["Keep"], library.State.Collections);
    }

    [Fact]
    public async Task MetadataCommitFailureRollsBackOnlyNewlyImportedEntries()
    {
        using var source = await Library.CreateAsync();
        await source.History.ImportAsync(Text("new import"), default);
        string backup = Path.Combine(source.Root, "test.sillclip");
        await source.Workspace.ExportAsync(backup, Password, false);
        using var target = await Library.CreateAsync();
        var existing = Text("keep existing");
        await target.History.ImportAsync(existing, default);
        // A directory at the metadata destination forces the final atomic rename to fail.
        Directory.CreateDirectory(Path.Combine(target.Root, "library-state.dat"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => target.Workspace.ImportAsync(backup, Password, false));
        Assert.Equal(existing.ContentSignature, Assert.Single(target.History.GetItems()).ContentSignature);
        Assert.Single(Directory.GetFiles(target.History.Store.Folder, "*.dat"));
    }

    [Fact]
    public async Task PauseAndPresentationSurviveRestartWithoutBackfillingPrivateCopies()
    {
        using var library = await Library.CreateAsync();
        await library.State.PauseAsync(null);
        var during = DateTimeOffset.UtcNow;
        await library.State.SetPresentationAsync(true);
        using var restarted = new LibraryStateService(library.Root);
        await restarted.LoadAsync();
        Assert.True(restarted.PresentationMode);
        Assert.False(restarted.AllowsCapture(during));
        await restarted.ResumeAsync();
        Assert.False(restarted.AllowsCapture(during));
        Assert.True(restarted.AllowsCapture(DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public async Task InvalidMetadataKeepsCaptureAndPayloadAccessBlocked()
    {
        using var library = await Library.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(library.Root, "library-state.dat"), "not encrypted metadata");
        using var restarted = new LibraryStateService(library.Root);
        await Assert.ThrowsAnyAsync<Exception>(() => restarted.LoadAsync());
        Assert.NotNull(restarted.LoadError);
        Assert.True(restarted.PresentationMode);
        Assert.False(restarted.AllowsCapture(DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ResumeAsync());
    }

    [Fact]
    public async Task NativeDeletionFailureDoesNotResurrectDeletedEntryAfterRestart()
    {
        using var library = await Library.CreateAsync();
        await library.History.ImportAsync(Text("delete this"), default);
        var item = Assert.Single(library.Workspace.GetItems());
        library.Live.FailDeletion = true;
        await Assert.ThrowsAsync<AggregateException>(() => library.Workspace.DeleteAsync([item]));
        using var restarted = new LibraryStateService(library.Root);
        await restarted.LoadAsync();
        Assert.False(restarted.AllowsItem(item.ContentSignature, item.CapturedAt));
        Assert.Empty(library.Workspace.GetItems());
    }

    [Fact]
    public async Task PresentationModeRejectsBackupAndContentResolution()
    {
        using var library = await Library.CreateAsync();
        var item = Text("private content");
        await library.History.ImportAsync(item, default);
        await library.State.SetPresentationAsync(true);
        Assert.Throws<InvalidOperationException>(() => library.Workspace.Resolve(item.ContentSignature));
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.Workspace.ExportAsync(Path.Combine(library.Root, "blocked.sillclip"), Password, false));
        Assert.False(File.Exists(Path.Combine(library.Root, "blocked.sillclip")));
    }

    [Fact]
    public async Task StorageProtectsPinsWhenAnUnpinnedCopyHasTheSameSignature()
    {
        using var library = await Library.CreateAsync();
        var item = Text("same payload in archive and pins");
        await library.History.ImportAsync(item, default);
        await library.Pins.ImportAsync(item, default);
        var overview = await library.Workspace.GetStorageAsync();
        var entry = Assert.Single(overview.Entries);
        Assert.True(entry.Item.Source.IsPinned);
        Assert.Equal(library.History.Store.StoredSize(item.Id) + library.Pins.Store.StoredSize(item.Id), entry.Bytes);
        Assert.Equal(entry.Bytes, overview.TotalBytes);
        Assert.DoesNotContain(overview.Entries, row => !row.Item.Source.IsPinned);
    }

    [Fact]
    public async Task NativeClearFailureStillRemovesLocalHistoryAndKeepsPins()
    {
        using var library = await Library.CreateAsync();
        var archived = Text("clear this archived entry");
        var pinned = Text("keep this pinned entry");
        await library.History.ImportAsync(archived, default);
        await library.Pins.ImportAsync(pinned, default);
        library.Live.FailClear = true;
        await Assert.ThrowsAsync<AggregateException>(() => library.Workspace.ClearUnpinnedAsync());
        Assert.Empty(library.History.GetItems());
        Assert.Empty(Directory.GetFiles(library.History.Store.Folder));
        Assert.Single(library.Pins.GetPinnedItems());
        Assert.False(library.State.AllowsItem(archived.ContentSignature, archived.PinnedAt));
    }

    private static PinnedClipboardItem Text(string text)
    {
        var item = new PinnedClipboardItem { Id = Guid.NewGuid().ToString("N"), Text = text, DataType = DetectedClipboardDataType.Text, PinnedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        item.ContentSignature = ClipboardSignature.Compute(item);
        return item;
    }

    private static SortedDictionary<string, byte[]> Snapshot(string root)
        => new(Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes));

    private sealed class Library : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "SillClipTests", Guid.NewGuid().ToString("N"));
        internal LibraryStateService State { get; }
        internal PersistentClipboardService History { get; }
        internal PinnedClipboardService Pins { get; }
        internal FakeLiveHistory Live { get; } = new();
        internal ClipboardWorkspaceService Workspace { get; }
        private Library()
        {
            Directory.CreateDirectory(Root);
            State = new(Root); History = new(Root, NullLogger.Instance); Pins = new(Root);
            Workspace = new(Root, State, new PasteQueueService(), History, Pins, Live);
        }
        internal static async Task<Library> CreateAsync()
        {
            var library = new Library();
            try { await library.State.LoadAsync(); return library; }
            catch { library.Dispose(); throw; }
        }
        public void Dispose()
        {
            State.Dispose();
            // Root is a full, unique directory created by this fixture, never a user data folder.
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FakeLiveHistory : ILiveClipboardHistory
    {
        internal bool FailDeletion { get; set; }
        internal bool FailClear { get; set; }
        public IReadOnlyList<ClipboardItemData> GetCachedItems() => [];
        public Task ClearAsync() => FailClear ? Task.FromException(new IOException("Synthetic native clear failure")) : Task.CompletedTask;
        public Task DeleteBySignatureAsync(string signature) => FailDeletion ? Task.FromException(new IOException("Synthetic native failure")) : Task.CompletedTask;
    }
}
