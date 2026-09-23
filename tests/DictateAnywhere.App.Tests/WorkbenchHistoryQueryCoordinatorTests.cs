using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchHistoryQueryCoordinatorTests
{
  [Xunit.Fact]
  public async Task QueryLatestAsync_AlwaysReadsStores()
  {
    int reads = 0;
    await using WorkbenchHistoryQueryCoordinator coordinator = CreateCoordinator(
      (_, _, _) =>
      {
        reads++;
        return Task.FromResult<IReadOnlyList<DictationHistoryRecord>>(Array.Empty<DictationHistoryRecord>());
      },
      (_, _, _) =>
      {
        reads++;
        return Task.FromResult<IReadOnlyList<ChatHistoryRecord>>(Array.Empty<ChatHistoryRecord>());
      });

    WorkbenchHistoryQueryResult? result = await coordinator.QueryLatestAsync(
      AppSettings.Default,
      "query");

    Xunit.Assert.NotNull(result);
    Xunit.Assert.Equal(WorkbenchHistoryQueryStatus.Ready, result.Status);
    Xunit.Assert.Equal("query", result.SearchText);
    Xunit.Assert.Empty(result.DictationGroups);
    Xunit.Assert.Empty(result.Chats);
    Xunit.Assert.Equal(2, reads);
  }

  [Xunit.Fact]
  public async Task QueryLatestAsync_ReturnsAFilteredStableSnapshot()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    DictationHistoryRecord matchingDictation = CreateDictation("match", now);
    DictationHistoryRecord excludedDictation = CreateDictation("other", now.AddDays(-1));
    ChatHistoryRecord matchingChat = CreateChat("match", now);
    ChatHistoryRecord excludedChat = CreateChat("other", now.AddMinutes(-1));
    int dictationLimit = 0;
    int chatLimit = 0;
    await using WorkbenchHistoryQueryCoordinator coordinator = CreateCoordinator(
      (_, limit, _) =>
      {
        dictationLimit = limit;
        return Task.FromResult<IReadOnlyList<DictationHistoryRecord>>([matchingDictation, excludedDictation]);
      },
      (_, limit, _) =>
      {
        chatLimit = limit;
        return Task.FromResult<IReadOnlyList<ChatHistoryRecord>>([matchingChat, excludedChat]);
      });

    WorkbenchHistoryQueryResult? result = await coordinator.QueryLatestAsync(
      AppSettings.Default,
      "match");

    Xunit.Assert.NotNull(result);
    Xunit.Assert.Equal(WorkbenchHistoryQueryStatus.Ready, result.Status);
    Xunit.Assert.Equal("match", result.SearchText);
    Xunit.Assert.Equal(100, dictationLimit);
    Xunit.Assert.Equal(100, chatLimit);
    Xunit.Assert.Equal(matchingDictation.EntryId, Xunit.Assert.Single(Xunit.Assert.Single(result.DictationGroups)).EntryId);
    Xunit.Assert.Equal(matchingChat.ConversationId, Xunit.Assert.Single(result.Chats).ConversationId);
  }

  [Xunit.Fact]
  public async Task QueryLatestAsync_CancelsStaleQueryBeforeStartingReplacement()
  {
    TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int dictationReads = 0;
    await using WorkbenchHistoryQueryCoordinator coordinator = CreateCoordinator(
      async (_, _, cancellationToken) =>
      {
        if (Interlocked.Increment(ref dictationReads) == 1)
        {
          firstStarted.SetResult();
          await releaseFirst.Task;
          cancellationToken.ThrowIfCancellationRequested();
        }

        return [CreateDictation("latest", DateTimeOffset.UtcNow)];
      },
      (_, _, _) => Task.FromResult<IReadOnlyList<ChatHistoryRecord>>(Array.Empty<ChatHistoryRecord>()));
    AppSettings settings = AppSettings.Default;

    Task<WorkbenchHistoryQueryResult?> first = coordinator.QueryLatestAsync(settings, "first");
    await firstStarted.Task;
    Task<WorkbenchHistoryQueryResult?> second = coordinator.QueryLatestAsync(settings, "latest");

    releaseFirst.SetResult();
    Xunit.Assert.Null(await first);
    WorkbenchHistoryQueryResult? latest = await second;
    Xunit.Assert.NotNull(latest);
    Xunit.Assert.Equal("latest", latest.SearchText);
    Xunit.Assert.Equal(2, dictationReads);
  }

  [Xunit.Fact]
  public async Task QueryLatestAsync_PropagatesCallerCancellation()
  {
    await using WorkbenchHistoryQueryCoordinator coordinator = CreateCoordinator(
      async (_, _, cancellationToken) =>
      {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return Array.Empty<DictationHistoryRecord>();
      },
      (_, _, _) => Task.FromResult<IReadOnlyList<ChatHistoryRecord>>(Array.Empty<ChatHistoryRecord>()));
    using CancellationTokenSource cancellationSource = new();

    Task<WorkbenchHistoryQueryResult?> query = coordinator.QueryLatestAsync(
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
    await using WorkbenchHistoryQueryCoordinator coordinator = CreateCoordinator(
      (_, _, _) => Task.FromException<IReadOnlyList<DictationHistoryRecord>>(failure),
      (_, _, _) => Task.FromResult<IReadOnlyList<ChatHistoryRecord>>(Array.Empty<ChatHistoryRecord>()));

    WorkbenchHistoryQueryResult? result = await coordinator.QueryLatestAsync(
      AppSettings.Default,
      string.Empty);

    Xunit.Assert.NotNull(result);
    Xunit.Assert.Equal(WorkbenchHistoryQueryStatus.Unavailable, result.Status);
    Xunit.Assert.Same(failure, result.Failure);
    Xunit.Assert.Empty(result.DictationGroups);
    Xunit.Assert.Empty(result.Chats);
  }

  private static WorkbenchHistoryQueryCoordinator CreateCoordinator(
    Func<AppSettings, int, CancellationToken, Task<IReadOnlyList<DictationHistoryRecord>>> readDictationsAsync,
    Func<AppSettings, int, CancellationToken, Task<IReadOnlyList<ChatHistoryRecord>>> readChatsAsync)
  {
    return new WorkbenchHistoryQueryCoordinator(
      readDictationsAsync,
      readChatsAsync);
  }

  private static DictationHistoryRecord CreateDictation(string text, DateTimeOffset createdUtc)
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

  private static ChatHistoryRecord CreateChat(string text, DateTimeOffset createdUtc)
  {
    return new ChatHistoryRecord(
      Guid.NewGuid().ToString("N"),
      text,
      createdUtc,
      createdUtc,
      ChatProviderIds.GemmaLocal,
      "model",
      [new ChatMessage(ChatMessageRoles.User, text, createdUtc)]).Normalize();
  }
}
