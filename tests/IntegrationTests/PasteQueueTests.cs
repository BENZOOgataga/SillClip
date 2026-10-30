using WindowSill.ClipboardHistory.Services;
using Xunit;

namespace IntegrationTests;

public sealed class PasteQueueTests
{
    [Fact]
    public void DuplicateContentsKeepIndependentQueuePositions()
    {
        var queue = new PasteQueueService();
        queue.Add(["first", "second", "first"]);
        var snapshot = queue.Entries;
        Assert.Equal(3, snapshot.Select(entry => entry.Id).Distinct().Count());
        queue.Move(snapshot[2].Id, -1);
        Assert.Equal([snapshot[0].Id, snapshot[2].Id, snapshot[1].Id], queue.Entries.Select(entry => entry.Id));
        queue.Remove(snapshot[0].Id);
        Assert.Equal(["first", "second"], queue.Entries.Select(entry => entry.Signature));
        Assert.Equal(3, snapshot.Count);
    }

    [Fact]
    public void InvalidMovesAndMissingEntriesDoNotCorruptOrder()
    {
        var queue = new PasteQueueService();
        queue.Add(["a", "b"]);
        var snapshot = queue.Entries;
        queue.Move(snapshot[0].Id, -1);
        queue.Move(snapshot[1].Id, 1);
        queue.Move(Guid.NewGuid(), 1);
        queue.Remove(Guid.NewGuid());
        Assert.Equal(snapshot, queue.Entries);
        queue.Clear();
        Assert.Empty(queue.Entries);
    }
}
