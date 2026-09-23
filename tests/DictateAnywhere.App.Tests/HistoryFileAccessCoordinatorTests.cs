using DictateAnywhere.App.History;

namespace DictateAnywhere.App.Tests;

public sealed class HistoryFileAccessCoordinatorTests
{
  [Xunit.Fact]
  public async Task AcquireAsync_SerializesEquivalentPaths()
  {
    string path = CreateUniquePath();
    using HistoryFileAccessCoordinator.HistoryFileAccessLease first = await HistoryFileAccessCoordinator.AcquireAsync(path);

    Task<HistoryFileAccessCoordinator.HistoryFileAccessLease> secondTask = HistoryFileAccessCoordinator
      .AcquireAsync(Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path)))
      .AsTask();

    Xunit.Assert.False(secondTask.IsCompleted);
    first.Dispose();
    using HistoryFileAccessCoordinator.HistoryFileAccessLease second = await secondTask;
    Xunit.Assert.True(HistoryFileAccessCoordinator.IsTracked(path));
  }

  [Xunit.Fact]
  public async Task AcquireAsync_AllowsDifferentPathsConcurrently()
  {
    string firstPath = CreateUniquePath();
    string secondPath = CreateUniquePath();

    using HistoryFileAccessCoordinator.HistoryFileAccessLease first = await HistoryFileAccessCoordinator.AcquireAsync(firstPath);
    ValueTask<HistoryFileAccessCoordinator.HistoryFileAccessLease> secondTask = HistoryFileAccessCoordinator.AcquireAsync(secondPath);

    Xunit.Assert.True(secondTask.IsCompletedSuccessfully);
    using HistoryFileAccessCoordinator.HistoryFileAccessLease second = await secondTask;
  }

  [Xunit.Fact]
  public async Task AcquireAsync_CancelledWaiterReleasesItsReference()
  {
    string path = CreateUniquePath();
    using HistoryFileAccessCoordinator.HistoryFileAccessLease first = await HistoryFileAccessCoordinator.AcquireAsync(path);
    using CancellationTokenSource cancellationSource = new();
    ValueTask<HistoryFileAccessCoordinator.HistoryFileAccessLease> waiting = HistoryFileAccessCoordinator.AcquireAsync(
      path,
      cancellationSource.Token);

    cancellationSource.Cancel();

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.AsTask());
    Xunit.Assert.True(HistoryFileAccessCoordinator.IsTracked(path));
    first.Dispose();
    Xunit.Assert.False(HistoryFileAccessCoordinator.IsTracked(path));
  }

  [Xunit.Fact]
  public async Task LeaseDispose_IsIdempotentAndRemovesUnusedPath()
  {
    string path = CreateUniquePath();
    HistoryFileAccessCoordinator.HistoryFileAccessLease lease = await HistoryFileAccessCoordinator.AcquireAsync(path);

    lease.Dispose();
    lease.Dispose();

    Xunit.Assert.False(HistoryFileAccessCoordinator.IsTracked(path));
  }

  private static string CreateUniquePath()
  {
    return Path.Combine(Path.GetTempPath(), "DictateAnywhere.Tests", Guid.NewGuid().ToString("N"), "history.jsonl");
  }
}
