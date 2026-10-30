using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using Path = System.IO.Path;

namespace WindowSill.ClipboardHistory.Services;

internal sealed record StorageEntry(ClipboardItemData Item, long Bytes);
internal sealed record StorageOverview(long TotalBytes, IReadOnlyList<StorageEntry> Entries);
internal sealed record ImportResult(int Added, int Duplicates, int Skipped);

/// <summary>Serializes library mutations and coordinates add-only backup restores.</summary>
[Export(typeof(ClipboardWorkspaceService))]
internal sealed class ClipboardWorkspaceService
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly PersistentClipboardService _history;
    private readonly PinnedClipboardService _pins;
    private readonly ILiveClipboardHistory _live;
    private readonly string _transferFolder;
    internal LibraryStateService State { get; }
    internal PasteQueueService Queue { get; }

    [ImportingConstructor]
    public ClipboardWorkspaceService(IPluginInfo pluginInfo, LibraryStateService state, PasteQueueService queue,
        PersistentClipboardService history, PinnedClipboardService pins, ClipboardHistoryDataService live)
        : this(ClipboardDataMigration.ForkFolder(pluginInfo.GetPluginDataFolder()), state, queue, history, pins, live) { }

    internal ClipboardWorkspaceService(string folder, LibraryStateService state, PasteQueueService queue,
        PersistentClipboardService history, PinnedClipboardService pins, ILiveClipboardHistory live)
    {
        State = state; Queue = queue; _history = history; _pins = pins; _live = live;
        _transferFolder = Path.Combine(folder, "Transfers");
    }

    internal async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new GateLease(_operations);
    }

    internal IReadOnlyList<ClipboardItemData> GetItems(bool includeArchive = true)
    {
        var pins = _pins.GetPinnedItems().Where(item => State.AllowsPinnedItem(item.ContentSignature, item.CapturedAt));
        var remainder = (includeArchive ? _history.GetItems() : []).Concat(_live.GetCachedItems())
            .Where(item => State.AllowsItem(item.ContentSignature, item.CapturedAt));
        return pins.Concat(remainder.OrderByDescending(item => item.CapturedAt))
            .DistinctBy(item => item.ContentSignature).ToArray();
    }

    internal ClipboardItemData Resolve(string signature)
    {
        if (State.PresentationMode) throw new InvalidOperationException("Clipboard content is hidden in presentation mode.");
        return GetItems().FirstOrDefault(item => item.ContentSignature == signature)
            ?? throw new InvalidOperationException("This clipboard entry is no longer available.");
    }

    internal async Task DeleteAsync(IReadOnlyList<ClipboardItemData> items, CancellationToken cancellationToken = default)
    {
        using var operation = await EnterAsync(cancellationToken);
        string[] signatures = items.Select(item => item.ContentSignature).Distinct(StringComparer.Ordinal).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        // Commit the deletion journal first. A failed payload removal must not restore deleted content at startup.
        await State.MarkDeletedAsync(signatures);
        var failures = new List<Exception>();
        // Once committed, finish the chosen deletion even if the popup closes. New cancellation applies before commit.
        foreach (string signature in signatures)
        {
            try { await _live.DeleteBySignatureAsync(signature); }
            catch (Exception ex) { failures.Add(ex); }
            foreach (var pin in _pins.GetPinnedItems().Where(pin => pin.ContentSignature == signature))
            {
                try { await _pins.UnpinAsync(pin.Source.Id); }
                catch (Exception ex) { failures.Add(ex); }
            }
        }
        try { await _history.DeleteManyAsync(signatures); }
        catch (Exception ex) { failures.Add(ex); }
        _history.NotifyChanged(); _pins.NotifyChanged();
        if (failures.Count > 0) throw new AggregateException("Some clipboard payloads could not be deleted; their deletion journal remains active.", failures);
    }

    internal async Task ClearUnpinnedAsync()
    {
        using var operation = await EnterAsync();
        await State.ClearUnpinnedAsync();
        var failures = new List<Exception>();
        try { await _live.ClearAsync(); }
        catch (Exception ex) { failures.Add(ex); }
        try { await _history.ClearAsync(); }
        catch (Exception ex) { failures.Add(ex); }
        if (failures.Count > 0) throw new AggregateException("Some clipboard history could not be cleared.", failures);
    }

    internal async Task TogglePinAsync(ClipboardItemData item)
    {
        using var operation = await EnterAsync();
        if (item.Source.IsPinned) await _pins.UnpinAsync(item.Source.Id);
        else await Task.Run(() => _pins.PinAsync(item));
    }

    internal async Task<StorageOverview> GetStorageAsync(CancellationToken cancellationToken = default)
    {
        using var operation = await EnterAsync(cancellationToken);
        return await Task.Run(() =>
        {
            var entries = _history.GetItems().Select(item => new StorageEntry(item, _history.Store.StoredSize(item.Source.Id)))
                .Concat(_pins.GetPinnedItems().Select(item => new StorageEntry(item, _pins.Store.StoredSize(item.Source.Id))))
                .GroupBy(entry => entry.Item.ContentSignature, StringComparer.Ordinal)
                // A payload can exist in both stores. Treat it as pinned if either copy is pinned,
                // so cleanup cannot delete that pin through an unpinned duplicate row.
                .Select(group => new StorageEntry(group.OrderByDescending(entry => entry.Item.Source.IsPinned)
                    .ThenByDescending(entry => entry.Item.CapturedAt).First().Item, group.Sum(entry => entry.Bytes)))
                .OrderByDescending(entry => entry.Bytes).ToArray();
            return new StorageOverview(entries.Sum(entry => entry.Bytes), entries);
        }, cancellationToken);
    }

    internal async Task<int> ExportAsync(string path, string password, bool includeLocalFileReferences,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (State.PresentationMode) throw new InvalidOperationException("Disable presentation mode before exporting.");
        ClipboardItemData[] items;
        using (await EnterAsync(cancellationToken)) items = GetItems().ToArray();
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        int exported = 0;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                await PortableClipboardArchive.WriteAsync(output, password, Entries(cancellationToken), cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (State.PresentationMode) throw new InvalidOperationException("Presentation mode was enabled during export.");
            File.Move(temporary, destination, overwrite: true);
            return exported;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        async IAsyncEnumerable<PortableClipboardEntry> Entries([EnumeratorCancellation] CancellationToken token)
        {
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (item.DataType == DetectedClipboardDataType.File && !includeLocalFileReferences) continue;
                PinnedClipboardItem snapshot;
                string[] collections;
                // Hold the mutation gate only while reading one payload. Encoding and disk writes must not block capture.
                using (await EnterAsync(token))
                {
                    var current = Resolve(item.ContentSignature);
                    snapshot = await SnapshotAsync(current);
                    collections = State.CollectionsFor(item.ContentSignature).ToArray();
                }
                var entry = new PortableClipboardEntry
                {
                    Item = snapshot,
                    IsPinned = item.Source.IsPinned,
                    Image = snapshot.ImageBytes,
                    Collections = collections
                };
                try { yield return entry; }
                finally { if (entry.Image is not null) CryptographicOperations.ZeroMemory(entry.Image); snapshot.ImageBytes = null; }
                exported++;
                progress?.Report(exported);
            }
        }
    }

    internal async Task<ImportResult> ImportAsync(string path, string password, bool allowLocalFileReferences,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (State.PresentationMode) throw new InvalidOperationException("Disable presentation mode before importing.");
        string staging = Path.GetFullPath(Path.Combine(_transferFolder, Guid.NewGuid().ToString("N")));
        var staged = new List<(string Id, bool Pinned, string Signature, string[] Collections)>();
        var added = new List<(string Id, bool Pinned, string Signature)>();
        int skipped = 0, duplicates = 0;
        IDisposable? commitLease = null;
        try
        {
            var store = new PinnedClipboardStore(staging, "Items");
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
            {
                await PortableClipboardArchive.ReadToStagingAsync(input, password, async (entry, token) =>
                {
                    try
                    {
                        if (!entry.NormalizeForImport(allowLocalFileReferences)) { skipped++; return; }
                        await store.SaveAsync(entry.Item, token);
                        staged.Add((entry.Item.Id, entry.IsPinned, entry.Item.ContentSignature, entry.Collections));
                        progress?.Report(staged.Count);
                    }
                    finally { if (entry.Image is not null) CryptographicOperations.ZeroMemory(entry.Image); entry.Item.ImageBytes = null; }
                }, cancellationToken);
            }

            // No existing payload or metadata is changed until every archive record and its footer authenticate.
            commitLease = await EnterAsync(cancellationToken);
            if (State.PresentationMode) throw new InvalidOperationException("Presentation mode was enabled during import.");
            var existingHistory = _history.GetItems().ToDictionary(item => item.ContentSignature, item => item.CapturedAt, StringComparer.Ordinal);
            var existingPins = _pins.GetPinnedItems().ToDictionary(item => item.ContentSignature, item => item.CapturedAt, StringComparer.Ordinal);
            var memberships = new Dictionary<string, (DateTimeOffset, string[])>(StringComparer.Ordinal);
            foreach (var row in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = await store.ReadModelAsync(row.Id, cancellationToken) ?? throw new InvalidDataException("Staged entry is unavailable.");
                var destination = row.Pinned ? existingPins : existingHistory;
                bool duplicate = destination.ContainsKey(row.Signature) || !row.Pinned && existingPins.ContainsKey(row.Signature);
                var capturedAt = existingPins.GetValueOrDefault(row.Signature,
                    existingHistory.GetValueOrDefault(row.Signature, model.PinnedAt));
                string[] collections = memberships.TryGetValue(row.Signature, out var previous)
                    ? previous.Item2.Concat(row.Collections).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : row.Collections;
                memberships[row.Signature] = (capturedAt, collections);
                if (duplicate) { duplicates++; continue; }
                byte[]? image = null;
                try
                {
                    if (model.HasImage) model.ImageBytes = image = await store.ReadImageAsync(row.Id);
                    bool imported = row.Pinned ? await _pins.ImportAsync(model, cancellationToken) : await _history.ImportAsync(model, cancellationToken);
                    if (imported)
                    {
                        added.Add((model.Id, row.Pinned, row.Signature));
                        destination[row.Signature] = model.PinnedAt;
                    }
                }
                finally { if (image is not null) CryptographicOperations.ZeroMemory(image); model.ImageBytes = null; }
            }
            await State.MergeImportedCollectionsAsync(memberships);
            _history.NotifyChanged(); _pins.NotifyChanged();
            return new(added.Count, duplicates, skipped);
        }
        catch (Exception original)
        {
            var failures = new List<Exception>();
            foreach (var row in added.AsEnumerable().Reverse())
            {
                try
                {
                    if (row.Pinned) await _pins.UnpinAsync(row.Id);
                    else await _history.DeleteBySignatureAsync(row.Signature);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            if (failures.Count > 0) throw new AggregateException("Import failed and rollback could not remove all new entries.", [original, .. failures]);
            throw;
        }
        finally
        {
            commitLease?.Dispose();
            string root = Path.GetFullPath(_transferFolder) + Path.DirectorySeparatorChar;
            if (staging.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private async Task<PinnedClipboardItem> SnapshotAsync(ClipboardItemData data)
    {
        PinnedClipboardItem model;
        PinnedClipboardStore? store = null;
        if (data.Source is PinnedClipboardItemSource pin) { model = pin.Model; store = _pins.Store; }
        else if (data.Source is PersistentClipboardItemSource archived) { model = archived.Model; store = _history.Store; }
        else
        {
            var live = await ClipboardContentCapturer.CaptureAsync(data.Source.Data, data.DataType);
            live.PinnedAt = data.CapturedAt;
            return live;
        }
        var copy = JsonSerializer.Deserialize<PinnedClipboardItem>(JsonSerializer.Serialize(model, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;
        if (copy.HasImage) copy.ImageBytes = await store.ReadImageAsync(copy.Id);
        copy.PinnedAt = data.CapturedAt;
        return copy;
    }

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
