using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class HistoryCommandCoordinatorTests
{
  [Xunit.Fact]
  public async Task Command_AlwaysPersistsHistory()
  {
    int factoryCalls = 0;
    await using HistoryCommandCoordinator coordinator = CreateCoordinator(
      () =>
      {
        factoryCalls++;
        return new StubDictationStore();
      });

    HistoryCommandResult result = await coordinator.RecordDictationAsync(
      AppSettings.Default,
      CreateDictation("always-on"));

    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, result.Status);
    Xunit.Assert.Equal(1, factoryCalls);
    Xunit.Assert.False(coordinator.IsBusy);
  }

  [Xunit.Fact]
  public async Task UpdateAndDelete_MapStoreOutcomesToTypedResults()
  {
    StubDictationStore store = new()
    {
      Update = (_, _) => Task.FromResult(false),
      Delete = (_, _) => Task.FromResult(3),
    };
    await using HistoryCommandCoordinator coordinator = CreateCoordinator(() => store);

    HistoryCommandResult update = await coordinator.UpdateDictationAsync(
      EnabledSettings,
      CreateDictation("update"));
    HistoryCommandResult delete = await coordinator.DeleteDictationSessionsAsync(
      EnabledSettings,
      ["session-a", "session-b"]);

    Xunit.Assert.Equal(HistoryCommandStatus.NotFound, update.Status);
    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, delete.Status);
    Xunit.Assert.Equal(3, delete.AffectedCount);
  }

  [Xunit.Fact]
  public async Task Commands_AreSerializedWithoutDroppingQueuedWork()
  {
    TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool secondStarted = false;
    StubDictationStore dictationStore = new()
    {
      Record = async (_, _) =>
      {
        firstStarted.SetResult();
        await releaseFirst.Task;
      },
    };
    StubChatStore chatStore = new()
    {
      Save = (_, _) =>
      {
        secondStarted = true;
        return Task.CompletedTask;
      },
    };
    await using HistoryCommandCoordinator coordinator = CreateCoordinator(
      () => dictationStore,
      () => chatStore);

    Task<HistoryCommandResult> first = coordinator.RecordDictationAsync(
      EnabledSettings,
      CreateDictation("first"));
    await firstStarted.Task;
    Task<HistoryCommandResult> second = coordinator.SaveChatAsync(
      EnabledSettings,
      CreateChat("second"));

    Xunit.Assert.True(coordinator.IsBusy);
    Xunit.Assert.False(secondStarted);
    releaseFirst.SetResult();
    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, (await first).Status);
    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, (await second).Status);
    Xunit.Assert.True(secondStarted);
    Xunit.Assert.False(coordinator.IsBusy);
  }

  [Xunit.Fact]
  public async Task QueuedCommand_PropagatesCallerCancellationWithoutRunning()
  {
    TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int recordCalls = 0;
    StubDictationStore store = new()
    {
      Record = async (_, _) =>
      {
        Interlocked.Increment(ref recordCalls);
        firstStarted.TrySetResult();
        await releaseFirst.Task;
      },
    };
    await using HistoryCommandCoordinator coordinator = CreateCoordinator(() => store);
    Task<HistoryCommandResult> first = coordinator.RecordDictationAsync(
      EnabledSettings,
      CreateDictation("first"));
    await firstStarted.Task;
    using CancellationTokenSource cancellationSource = new();
    Task<HistoryCommandResult> second = coordinator.RecordDictationAsync(
      EnabledSettings,
      CreateDictation("second"),
      cancellationSource.Token);

    cancellationSource.Cancel();
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    Xunit.Assert.Equal(1, recordCalls);
    releaseFirst.SetResult();
    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, (await first).Status);
  }

  [Xunit.Fact]
  public async Task Command_TranslatesExpectedPersistenceFailure()
  {
    IOException failure = new("history unavailable");
    StubChatStore store = new()
    {
      Save = (_, _) => Task.FromException(failure),
    };
    await using HistoryCommandCoordinator coordinator = CreateCoordinator(
      chatStoreFactory: () => store);

    HistoryCommandResult result = await coordinator.SaveChatAsync(
      EnabledSettings,
      CreateChat("failure"));

    Xunit.Assert.Equal(HistoryCommandStatus.Unavailable, result.Status);
    Xunit.Assert.Same(failure, result.Failure);
    Xunit.Assert.False(coordinator.IsBusy);
  }

  [Xunit.Fact]
  public async Task Command_PropagatesUnexpectedFailureAndClearsBusyState()
  {
    ArgumentException failure = new("command defect");
    StubDictationStore store = new()
    {
      Record = (_, _) => Task.FromException(failure),
    };
    await using HistoryCommandCoordinator coordinator = CreateCoordinator(() => store);

    ArgumentException thrown = await Xunit.Assert.ThrowsAsync<ArgumentException>(() =>
      coordinator.RecordDictationAsync(EnabledSettings, CreateDictation("failure")));

    Xunit.Assert.Same(failure, thrown);
    Xunit.Assert.False(coordinator.IsBusy);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsAndWaitsForAcceptedCommands()
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken receivedToken = default;
    StubDictationStore store = new()
    {
      Record = async (_, cancellationToken) =>
      {
        receivedToken = cancellationToken;
        started.SetResult();
        await release.Task;
        cancellationToken.ThrowIfCancellationRequested();
      },
    };
    HistoryCommandCoordinator coordinator = CreateCoordinator(() => store);
    Task<HistoryCommandResult> command = coordinator.RecordDictationAsync(
      EnabledSettings,
      CreateDictation("active"));
    await started.Task;

    ValueTask firstDisposal = coordinator.DisposeAsync();
    ValueTask secondDisposal = coordinator.DisposeAsync();

    Xunit.Assert.True(receivedToken.IsCancellationRequested);
    Xunit.Assert.False(firstDisposal.IsCompleted);
    await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(() =>
      coordinator.RecordDictationAsync(EnabledSettings, CreateDictation("late")));
    release.SetResult();
    Xunit.Assert.Equal(HistoryCommandStatus.Canceled, (await command).Status);
    await firstDisposal;
    await secondDisposal;
    Xunit.Assert.False(coordinator.IsBusy);
  }

  private static AppSettings EnabledSettings => AppSettings.Default;

  private static HistoryCommandCoordinator CreateCoordinator(
    Func<StubDictationStore>? dictationStoreFactory = null,
    Func<StubChatStore>? chatStoreFactory = null)
  {
    return new HistoryCommandCoordinator(
      _ => dictationStoreFactory?.Invoke() ?? new StubDictationStore(),
      _ => chatStoreFactory?.Invoke() ?? new StubChatStore());
  }

  private static DictationHistoryRecord CreateDictation(string text)
  {
    return new DictationHistoryRecord(
      DateTimeOffset.UtcNow,
      "default",
      TranscriptionProviderIds.CohereLocal,
      "model",
      text,
      text,
      TimeSpan.Zero,
      TimeSpan.Zero,
      TimeSpan.Zero).Normalize();
  }

  private static ChatHistoryRecord CreateChat(string text)
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    return new ChatHistoryRecord(
      Guid.NewGuid().ToString("N"),
      text,
      now,
      now,
      ChatProviderIds.GemmaLocal,
      "model",
      [new ChatMessage(ChatMessageRoles.User, text, now)]).Normalize();
  }

  private sealed class StubDictationStore : IDictationHistoryCommandStore
  {
    public Func<DictationHistoryRecord, CancellationToken, Task> Record { get; init; } =
      (_, _) => Task.CompletedTask;

    public Func<DictationHistoryRecord, CancellationToken, Task<bool>> Update { get; init; } =
      (_, _) => Task.FromResult(true);

    public Func<IEnumerable<string>, CancellationToken, Task<int>> Delete { get; init; } =
      (_, _) => Task.FromResult(1);

    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) =>
      Record(record, cancellationToken);

    public Task<bool> UpdateAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) =>
      Update(record, cancellationToken);

    public Task<int> DeleteSessionsAsync(
      IEnumerable<string> sessionIds,
      CancellationToken cancellationToken = default) => Delete(sessionIds, cancellationToken);
  }

  private sealed class StubChatStore : IChatHistoryCommandStore
  {
    public Func<ChatHistoryRecord, CancellationToken, Task> Save { get; init; } =
      (_, _) => Task.CompletedTask;

    public Func<IEnumerable<string>, CancellationToken, Task<int>> Delete { get; init; } =
      (_, _) => Task.FromResult(1);

    public Task SaveAsync(ChatHistoryRecord record, CancellationToken cancellationToken = default) =>
      Save(record, cancellationToken);

    public Task<int> DeleteConversationsAsync(
      IEnumerable<string> conversationIds,
      CancellationToken cancellationToken = default) => Delete(conversationIds, cancellationToken);
  }
}
