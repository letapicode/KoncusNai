using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class HistoryQueryCoordinatorTests
{
  [Xunit.Fact]
  public async Task QueryLatestAsync_AlwaysReadsStore()
  {
    int reads = 0;
    await using HistoryQueryCoordinator coordinator = CreateCoordinator((_, _, _) =>
    {
      reads++;
      return Task.FromResult<IReadOnlyList<DictationHistoryRecord>>(Array.Empty<DictationHistoryRecord>());
    });

    HistoryQueryResult? result = await coordinator.QueryLatestAsync(
      AppSettings.Default,
      "query");

    Xunit.Assert.NotNull(result);
    Xunit.Assert.Equal(HistoryQueryStatus.Ready, result.Status);
    Xunit.Assert.Equal("query", result.SearchText);
    Xunit.Assert.Empty(result.Records);
    Xunit.Assert.Equal(1, reads);
  }

  [Xunit.Fact]
  public async Task QueryLatestAsync_ReturnsAFilteredStableSnapshot()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    DictationHistoryRecord matching = CreateRecord("match", now);
    DictationHistoryRecord excluded = CreateRecord("other", now.AddMinutes(-1));
    int readLimit = 0;
    await using HistoryQueryCoordinator coordinator = CreateCoordinator(
      (_, limit, _) =>
      {
        readLimit = limit;
        return Task.FromResult<IReadOnlyList<DictationHistoryRecord>>([matching, excluded]);
      });

    HistoryQueryResult? result = await coordinator.QueryLatestAsync(
      AppSettings.Default,
      "match");

    Xunit.Assert.NotNull(result);
    Xunit.Assert.Equal(HistoryQueryStatus.Ready, result.Status);
    Xunit.Assert.Equal("match", result.SearchText);
    Xunit.Assert.Equal(200, readLimit);
    Xunit.Assert.Equal(matching.EntryId, Xunit.Assert.Single(result.Records).EntryId);
  }

  [Xunit.Fact]
  public async Task QueryLatestAsync_CancelsStaleQueryBeforeStartingReplacement()
  {
    TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int reads = 0;
    await using HistoryQueryCoordinator coordinator = CreateCoordinator(async (_, _, cancellationToken) =>
    {
      if (Interlocked.Increment(ref reads) == 1)
      {
        firstStarted.SetResult();
        await releaseFirst.Task;
        cancellationToken.ThrowIfCancellationRequested();
      }

      return [CreateRecord("latest", DateTimeOffset.UtcNow)];
    });
    AppSettings settings = AppSettings.Default;

    Task<HistoryQueryResult?> first = coordinator.QueryLatestAsync(settings, "first");
    await firstStarted.Task;
    Task<HistoryQueryResult?> second = coordinator.QueryLatestAsync(settings, "latest");

    releaseFirst.SetResult();
    Xunit.Assert.Null(await first);
    HistoryQueryResult? latest = await second;
    Xunit.Assert.NotNull(latest);
    Xunit.Assert.Equal("latest", latest.SearchText);
    Xunit.Assert.Equal(2, reads);
  }

  [Xunit.Fact]
  public async Task QueryLatestAsync_PropagatesCallerCancellation()
  {
    await using HistoryQueryCoordinator coordinator = CreateCoordinator(async (_, _, cancellationToken) =>
    {
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      return Array.Empty<DictationHistoryRecord>();
    });
    using CancellationTokenSource cancellationSource = new();

    Task<HistoryQueryResult?> query = coordinator.QueryLatestAsync(
      AppSettings.Default,
      string.Empty,
      cancellationSource.Token);
    cancellationSource.Cancel();

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
  }

  [Xunit.Fact]
  public async Task QueryLatestAsync_TranslatesExpectedPersistenceFailure()
  {
    IOException failure = new("history unavailable");
    await using HistoryQueryCoordinator coordinator = CreateCoordinator(
      (_, _, _) => Task.FromException<IReadOnlyList<DictationHistoryRecord>>(failure));

    HistoryQueryResult? result = await coordinator.QueryLatestAsync(
      AppSettings.Default,
      string.Empty);

    Xunit.Assert.NotNull(result);
    Xunit.Assert.Equal(HistoryQueryStatus.Unavailable, result.Status);
    Xunit.Assert.Same(failure, result.Failure);
    Xunit.Assert.Empty(result.Records);
  }

  [Xunit.Fact]
  public async Task QueryLatestAsync_PropagatesUnexpectedFailure()
  {
    ArgumentException failure = new("query defect");
    await using HistoryQueryCoordinator coordinator = CreateCoordinator(
      (_, _, _) => Task.FromException<IReadOnlyList<DictationHistoryRecord>>(failure));

    ArgumentException thrown = await Xunit.Assert.ThrowsAsync<ArgumentException>(() =>
      coordinator.QueryLatestAsync(
        AppSettings.Default,
        string.Empty));

    Xunit.Assert.Same(failure, thrown);
  }

  private static HistoryQueryCoordinator CreateCoordinator(
    Func<AppSettings, int, CancellationToken, Task<IReadOnlyList<DictationHistoryRecord>>> readAsync)
  {
    return new HistoryQueryCoordinator(readAsync);
  }

  private static DictationHistoryRecord CreateRecord(string text, DateTimeOffset createdUtc)
  {
    return new DictationHistoryRecord(
      createdUtc,
      "default",
      TranscriptionProviderIds.CohereLocal,
      "model",
      text,
      text,
      TimeSpan.Zero,
      TimeSpan.Zero,
      TimeSpan.Zero).Normalize();
  }
}
