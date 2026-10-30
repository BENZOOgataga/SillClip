using System.ComponentModel.Composition;

namespace WindowSill.ClipboardHistory.Services;

internal sealed record PasteQueueEntry(Guid Id, string Signature);

/// <summary>The queue retains identifiers, not decrypted copies of clipboard payloads.</summary>
[Export(typeof(PasteQueueService))]
internal sealed class PasteQueueService
{
    private readonly object _gate = new();
    private readonly List<PasteQueueEntry> _entries = [];
    internal event EventHandler? Changed;
    internal IReadOnlyList<PasteQueueEntry> Entries { get { lock (_gate) return _entries.ToArray(); } }

    internal void Add(IEnumerable<string> signatures)
    {
        lock (_gate) _entries.AddRange(signatures.Where(value => !string.IsNullOrEmpty(value)).Select(value => new PasteQueueEntry(Guid.NewGuid(), value)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Remove(Guid id)
    {
        lock (_gate) _entries.RemoveAll(entry => entry.Id == id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Move(Guid id, int offset)
    {
        lock (_gate)
        {
            int index = _entries.FindIndex(entry => entry.Id == id);
            if (index < 0 || index + offset < 0 || index + offset >= _entries.Count) return;
            var entry = _entries[index]; _entries.RemoveAt(index); _entries.Insert(index + offset, entry);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Clear()
    {
        lock (_gate) _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
