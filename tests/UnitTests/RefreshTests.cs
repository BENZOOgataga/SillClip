using System.Collections.ObjectModel;
using System.Collections.Specialized;
using WindowSill.ClipboardHistory.Core;
using Xunit;

namespace UnitTests;

public sealed class RefreshTests
{
    [Fact]
    public async Task BurstKeepsOnlyOnePendingRefreshAndRunsTheLatestRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new CoalescingAsyncAction(ex => throw new Exception("Unexpected refresh error", ex));
        int calls = 0;
        int lastRequest = -1;
        Task first = queue.RequestAsync(async () => { Interlocked.Increment(ref calls); started.SetResult(); await release.Task; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int request = 0; request < 1000; request++)
        {
            int captured = request;
            _ = queue.RequestAsync(() => { Interlocked.Increment(ref calls); lastRequest = captured; return Task.CompletedTask; });
        }
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.Equal(999, lastRequest);
    }

    [Fact]
    public async Task FailedRefreshDoesNotPreventLaterCopies()
    {
        var errors = new List<Exception>();
        var queue = new CoalescingAsyncAction(errors.Add);
        await queue.RequestAsync(() => throw new InvalidOperationException("Unavailable clipboard provider"));
        bool ran = false;
        await queue.RequestAsync(() => { ran = true; return Task.CompletedTask; });
        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.True(ran);
    }

    [Fact]
    public async Task DeactivationDropsPendingWork()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new CoalescingAsyncAction(_ => { });
        Task running = queue.RequestAsync(async () => { started.SetResult(); await release.Task; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool pendingRan = false;
        _ = queue.RequestAsync(() => { pendingRan = true; return Task.CompletedTask; });
        queue.CancelPending();
        release.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pendingRan);
    }

    [Fact]
    public void RepeatedSnapshotDoesNotDetachControls()
    {
        object anchor = new(), first = new(), second = new();
        var collection = new ObservableCollection<object> { anchor, first, second };
        int changes = 0;
        collection.CollectionChanged += (_, _) => changes++;
        for (int copy = 0; copy < 100; copy++) CollectionReconciler.Update(collection, new[] { anchor, first, second });
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ReorderMovesExistingControlsWithoutRemovingTheAnchor()
    {
        object anchor = new(), first = new(), second = new();
        var collection = new ObservableCollection<object> { anchor, first, second };
        var changes = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => changes.Add(args.Action);
        CollectionReconciler.Update(collection, new[] { anchor, second, first });
        Assert.Equal(new[] { NotifyCollectionChangedAction.Move }, changes);
        Assert.Same(anchor, collection[0]);
    }

    [Fact]
    public void ReconcileHandlesInsertAndEviction()
    {
        object anchor = new(), old = new(), recent = new(), newest = new();
        var collection = new ObservableCollection<object> { anchor, recent, old };
        CollectionReconciler.Update(collection, new[] { anchor, newest, recent });
        Assert.Equal(new[] { anchor, newest, recent }, collection);
    }

    [Theory]
    [InlineData(4000u, 3000u, 256u, 192u)]
    [InlineData(1u, 100000u, 1u, 256u)]
    [InlineData(100000u, 1u, 256u, 1u)]
    [InlineData(32u, 16u, 32u, 16u)]
    [InlineData(uint.MaxValue, uint.MaxValue, 256u, 256u)]
    public void ThumbnailDecodeRemainsBounded(uint width, uint height, uint expectedWidth, uint expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), ThumbnailSize.Fit(width, height, 256));

    [Theory]
    [InlineData(0u, 100u)]
    [InlineData(100u, 0u)]
    public void InvalidImageDimensionsAreRejected(uint width, uint height)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ThumbnailSize.Fit(width, height, 256));
}
