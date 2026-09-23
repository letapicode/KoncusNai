using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "WorkbenchHistoryController owns and disposes its injected query coordinator.")]
public sealed class WorkbenchHistoryControllerTests
{
  [Xunit.Fact]
  public async Task RefreshAsync_MapsQuerySnapshotToPresentationState()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    DictationHistoryRecord dictation = CreateDictation("matching dictation", now);
    ChatHistoryRecord chat = CreateChat("matching chat", now);
    RecordingDiagnostics diagnostics = new();
    await using WorkbenchHistoryController controller = new(
      CreateQueryCoordinator([dictation], [chat]),
      CreateCommandCoordinator(),
      diagnostics);

    WorkbenchHistoryViewState? state = await controller.RefreshAsync(AppSettings.Default, "matching");

    Xunit.Assert.NotNull(state);
    Xunit.Assert.True(state!.IsAvailable);
    Xunit.Assert.Equal("matching", state.SearchText);
    Xunit.Assert.Equal(dictation.EntryId, Xunit.Assert.Single(Xunit.Assert.Single(state.DictationItems).Records).EntryId);
    Xunit.Assert.Equal(chat.ConversationId, Xunit.Assert.Single(state.ChatItems).Record.ConversationId);
    Xunit.Assert.Equal("1 matching session(s).", state.DictationStatus);
    Xunit.Assert.Equal("1 matching chat(s).", state.ChatStatus);
    Xunit.Assert.Empty(diagnostics.Errors);
  }

  [Xunit.Fact]
  public async Task RefreshAsync_PersistenceFailure_ReturnsUnavailableStateAndRecordsFailure()
  {
    IOException failure = new("simulated history failure");
    WorkbenchHistoryQueryCoordinator query = new(
      (_, _, _) => Task.FromException<IReadOnlyList<DictationHistoryRecord>>(failure),
      (_, _, _) => Task.FromResult<IReadOnlyList<ChatHistoryRecord>>(Array.Empty<ChatHistoryRecord>()));
    RecordingDiagnostics diagnostics = new();
    await using WorkbenchHistoryController controller = new(query, CreateCommandCoordinator(), diagnostics);

    WorkbenchHistoryViewState? state = await controller.RefreshAsync(AppSettings.Default, string.Empty);

    Xunit.Assert.NotNull(state);
    Xunit.Assert.False(state!.IsAvailable);
    Xunit.Assert.Equal("History unavailable. See Diagnostics.", state.DictationStatus);
    Xunit.Assert.Equal("History unavailable. See Diagnostics.", state.ChatStatus);
    Xunit.Assert.Contains(diagnostics.Errors, item => ReferenceEquals(item.Exception, failure));
  }

  [Xunit.Fact]
  public async Task SaveDictationEditAsync_NormalizesAndPersistsUpdatedRecord()
  {
    RecordingDictationStore store = new();
    RecordingDiagnostics diagnostics = new();
    DictationHistoryRecord original = CreateDictation("original", DateTimeOffset.UtcNow) with
    {
      Source = string.Empty,
    };
    await using WorkbenchHistoryController controller = new(
      CreateQueryCoordinator([], []),
      CreateCommandCoordinator(store),
      diagnostics);

    WorkbenchHistoryMutationResult result = await controller.SaveDictationEditAsync(
      AppSettings.Default,
      original,
      "  edited text  ");

    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, result.Status);
    Xunit.Assert.Equal("History edits saved.", result.Message);
    Xunit.Assert.Equal("edited text", result.DictationRecord?.FinalText);
    Xunit.Assert.Equal("workbench-history-edit", result.DictationRecord?.Source);
    Xunit.Assert.Equal("edited text", Xunit.Assert.Single(store.Updated).FinalText);
    Xunit.Assert.True(result.ShouldRefresh);
    Xunit.Assert.Empty(diagnostics.Errors);
  }

  [Xunit.Fact]
  public async Task SaveDictationEditAsync_RejectsEmptyTextWithoutPersistence()
  {
    RecordingDictationStore store = new();
    await using WorkbenchHistoryController controller = new(
      CreateQueryCoordinator([], []),
      CreateCommandCoordinator(store),
      new RecordingDiagnostics());

    WorkbenchHistoryMutationResult result = await controller.SaveDictationEditAsync(
      AppSettings.Default,
      CreateDictation("original", DateTimeOffset.UtcNow),
      "   ");

    Xunit.Assert.Equal(HistoryCommandStatus.Canceled, result.Status);
    Xunit.Assert.Equal("Cannot save an empty history entry.", result.Message);
    Xunit.Assert.Empty(store.Updated);
    Xunit.Assert.False(result.ShouldRefresh);
  }

  [Xunit.Fact]
  public async Task DeleteChatsAsync_MapsAffectedCountToPresentationResult()
  {
    RecordingChatStore store = new(deleteCount: 2);
    ChatHistoryRecord first = CreateChat("first", DateTimeOffset.UtcNow);
    ChatHistoryRecord second = CreateChat("second", DateTimeOffset.UtcNow.AddMinutes(-1));
    await using WorkbenchHistoryController controller = new(
      CreateQueryCoordinator([], []),
      CreateCommandCoordinator(chatStore: store),
      new RecordingDiagnostics());

    WorkbenchHistoryMutationResult result = await controller.DeleteChatsAsync(
      AppSettings.Default,
      [ChatHistoryItemViewModel.FromRecord(first), ChatHistoryItemViewModel.FromRecord(second)]);

    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, result.Status);
    Xunit.Assert.Equal(2, result.AffectedCount);
    Xunit.Assert.Equal("Deleted 2 chats.", result.Message);
    Xunit.Assert.True(result.ShouldRefresh);
    Xunit.Assert.Equal(
      new[] { first.ConversationId, second.ConversationId },
      Xunit.Assert.Single(store.DeletedConversationBatches));
  }

  [Xunit.Fact]
  public async Task SaveChatConversationAsync_NormalizesPersistsAndMapsSuccess()
  {
    RecordingChatStore store = new(deleteCount: 1);
    ChatHistoryRecord chat = CreateChat("conversation", DateTimeOffset.UtcNow) with
    {
      Title = "  Conversation notes  ",
    };
    await using WorkbenchHistoryController controller = new(
      CreateQueryCoordinator([], []),
      CreateCommandCoordinator(chatStore: store),
      new RecordingDiagnostics());

    WorkbenchHistoryMutationResult result = await controller.SaveChatConversationAsync(
      AppSettings.Default,
      chat);

    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, result.Status);
    Xunit.Assert.Equal("Chat saved.", result.Message);
    Xunit.Assert.Equal("Conversation notes", result.ChatRecord?.Title);
    Xunit.Assert.Equal("Conversation notes", Xunit.Assert.Single(store.Saved).Title);
  }

  private static WorkbenchHistoryQueryCoordinator CreateQueryCoordinator(
    IReadOnlyList<DictationHistoryRecord> dictations,
    IReadOnlyList<ChatHistoryRecord> chats) => new(
      (_, _, _) => Task.FromResult(dictations),
      (_, _, _) => Task.FromResult(chats));

  private static HistoryCommandCoordinator CreateCommandCoordinator(
    IDictationHistoryCommandStore? dictationStore = null,
    IChatHistoryCommandStore? chatStore = null) => new(
      _ => dictationStore ?? new NoOpDictationStore(),
      _ => chatStore ?? new NoOpChatStore());

  private static DictationHistoryRecord CreateDictation(string text, DateTimeOffset createdUtc) =>
    new DictationHistoryRecord(
      createdUtc,
      "default",
      TranscriptionProviderIds.CohereLocal,
      "model",
      text,
      text,
      TimeSpan.Zero,
      TimeSpan.Zero,
      TimeSpan.Zero).Normalize();

  private static ChatHistoryRecord CreateChat(string text, DateTimeOffset createdUtc) => new(
    Guid.NewGuid().ToString("N"),
    text,
    createdUtc,
    createdUtc,
    ChatProviderIds.GemmaLocal,
    "model",
    [new ChatMessage(ChatMessageRoles.User, text, createdUtc)]);

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<(string Message, Exception? Exception)> Errors { get; } = new();
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) => Errors.Add((message, exception));
  }

  private sealed class NoOpDictationStore : IDictationHistoryCommandStore
  {
    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> UpdateAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<int> DeleteSessionsAsync(IEnumerable<string> sessionIds, CancellationToken cancellationToken = default) => Task.FromResult(1);
  }

  private sealed class NoOpChatStore : IChatHistoryCommandStore
  {
    public Task SaveAsync(ChatHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task<int> DeleteConversationsAsync(IEnumerable<string> conversationIds, CancellationToken cancellationToken = default) => Task.FromResult(1);
  }

  private sealed class RecordingDictationStore : IDictationHistoryCommandStore
  {
    public List<DictationHistoryRecord> Updated { get; } = new();

    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task<bool> UpdateAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default)
    {
      Updated.Add(record);
      return Task.FromResult(true);
    }

    public Task<int> DeleteSessionsAsync(
      IEnumerable<string> sessionIds,
      CancellationToken cancellationToken = default) => Task.FromResult(1);
  }

  private sealed class RecordingChatStore(int deleteCount) : IChatHistoryCommandStore
  {
    public List<ChatHistoryRecord> Saved { get; } = new();

    public List<string[]> DeletedConversationBatches { get; } = new();

    public Task SaveAsync(ChatHistoryRecord record, CancellationToken cancellationToken = default)
    {
      Saved.Add(record);
      return Task.CompletedTask;
    }

    public Task<int> DeleteConversationsAsync(
      IEnumerable<string> conversationIds,
      CancellationToken cancellationToken = default)
    {
      DeletedConversationBatches.Add(conversationIds.ToArray());
      return Task.FromResult(deleteCount);
    }
  }
}
