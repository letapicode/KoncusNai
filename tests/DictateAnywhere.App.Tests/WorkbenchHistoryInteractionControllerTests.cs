using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(LastDictationSessionCacheCollection.Name)]
[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Fixture transfers both coordinators to WorkbenchHistoryController for disposal.")]
public sealed class WorkbenchHistoryInteractionControllerTests : IDisposable
{
  public void Dispose() => LastDictationSessionCache.Clear();

  [Xunit.Fact]
  public async Task LoadDeleteLoadedGroup_ClearsOnlyPristineHistoryComposerAndCache()
  {
    DictationHistoryRecord loaded = CreateDictation("loaded", DateTimeOffset.UtcNow, "loaded-session");
    LastDictationSessionCache.Store(loaded);
    await using Fixture fixture = new(deleteCount: 1);
    HistoryItemViewModel group = HistoryItemViewModel.FromRecords([loaded]);

    WorkbenchHistoryInteractionResult loadedResult = fixture.Interaction.LoadDictationGroup(group);
    WorkbenchHistoryInteractionResult deleted = await fixture.Interaction.DeleteDictationGroupsAsync(
      AppSettings.Default,
      [group]);

    Xunit.Assert.Equal(WorkbenchHistoryComposerDirective.Replace, loadedResult.ComposerDirective);
    Xunit.Assert.Equal(WorkbenchHistoryComposerDirective.ClearHistorySourcedContent, deleted.ComposerDirective);
    Xunit.Assert.True(deleted.ShouldRefresh);
    Xunit.Assert.Null(deleted.State.SelectedDictationRecord);
    Xunit.Assert.False(LastDictationSessionCache.TryGet(TimeSpan.Zero, out _));
  }

  [Xunit.Fact]
  public async Task DeleteLoadedGroup_PreservesComposerAfterManualChange()
  {
    DictationHistoryRecord loaded = CreateDictation("loaded", DateTimeOffset.UtcNow, "loaded-session");
    await using Fixture fixture = new(deleteCount: 1);
    HistoryItemViewModel group = HistoryItemViewModel.FromRecords([loaded]);

    fixture.Interaction.LoadDictationGroup(group);
    fixture.Interaction.NotifyComposerTextChanged("user changed this");
    WorkbenchHistoryInteractionResult deleted = await fixture.Interaction.DeleteDictationGroupsAsync(
      AppSettings.Default,
      [group]);

    Xunit.Assert.Equal(WorkbenchHistoryComposerDirective.None, deleted.ComposerDirective);
    Xunit.Assert.Null(deleted.State.SelectedDictationRecord);
  }

  [Xunit.Fact]
  public async Task DeleteDifferentGroup_PreservesLoadedComposerAndSelection()
  {
    DictationHistoryRecord loaded = CreateDictation("loaded", DateTimeOffset.UtcNow, "loaded-session");
    DictationHistoryRecord other = CreateDictation("other", DateTimeOffset.UtcNow.AddDays(-1), "other-session");
    LastDictationSessionCache.Store(loaded);
    await using Fixture fixture = new(deleteCount: 1);
    HistoryItemViewModel loadedGroup = HistoryItemViewModel.FromRecords([loaded]);
    HistoryItemViewModel otherGroup = HistoryItemViewModel.FromRecords([other]);

    fixture.Interaction.LoadDictationGroup(loadedGroup);
    WorkbenchHistoryInteractionResult deleted = await fixture.Interaction.DeleteDictationGroupsAsync(
      AppSettings.Default,
      [otherGroup]);

    Xunit.Assert.Equal(WorkbenchHistoryComposerDirective.None, deleted.ComposerDirective);
    Xunit.Assert.Equal(loaded.EntryId, deleted.State.SelectedDictationRecord?.EntryId);
    Xunit.Assert.True(LastDictationSessionCache.TryGet(TimeSpan.Zero, out DictationHistoryRecord? cached));
    Xunit.Assert.Equal(loaded.EntryId, cached?.EntryId);
  }

  [Xunit.Fact]
  public async Task FailedOrCanceledDelete_PreservesLoadedComposerSelectionAndCache()
  {
    DictationHistoryRecord loaded = CreateDictation("loaded", DateTimeOffset.UtcNow, "loaded-session");
    LastDictationSessionCache.Store(loaded);
    await using Fixture unavailable = new(deleteException: new IOException("unavailable"));
    HistoryItemViewModel group = HistoryItemViewModel.FromRecords([loaded]);

    unavailable.Interaction.LoadDictationGroup(group);
    WorkbenchHistoryInteractionResult failed = await unavailable.Interaction.DeleteDictationGroupsAsync(
      AppSettings.Default,
      [group]);
    WorkbenchHistoryInteractionResult canceled = unavailable.Interaction.CancelDictationDelete();

    Xunit.Assert.Equal(HistoryCommandStatus.Unavailable, failed.Status);
    Xunit.Assert.Equal(WorkbenchHistoryComposerDirective.None, failed.ComposerDirective);
    Xunit.Assert.Equal(loaded.EntryId, failed.State.SelectedDictationRecord?.EntryId);
    Xunit.Assert.True(LastDictationSessionCache.TryGet(TimeSpan.Zero, out _));
    Xunit.Assert.Equal(HistoryCommandStatus.Canceled, canceled.Status);
  }

  [Xunit.Fact]
  public async Task EditAndRename_KeepCurrentRecordAndOnlyUpdateMatchingCache()
  {
    DictationHistoryRecord selected = CreateDictation("original", DateTimeOffset.UtcNow, "selected-session");
    DictationHistoryRecord cachedOther = CreateDictation("other", DateTimeOffset.UtcNow, "other-session");
    LastDictationSessionCache.Store(cachedOther);
    await using Fixture fixture = new();
    fixture.Interaction.LoadDictationGroup(HistoryItemViewModel.FromRecords([selected]));

    WorkbenchHistoryInteractionResult edit = await fixture.Interaction.SaveDictationEditAsync(AppSettings.Default, "edited");
    WorkbenchHistoryInteractionResult rename = await fixture.Interaction.RenameDictationAsync(AppSettings.Default, "Renamed");

    Xunit.Assert.Equal("edited", edit.State.SelectedDictationRecord?.FinalText);
    Xunit.Assert.Equal("Renamed", rename.State.SelectedDictationRecord?.Title);
    Xunit.Assert.True(LastDictationSessionCache.TryGet(TimeSpan.Zero, out DictationHistoryRecord? cached));
    Xunit.Assert.Equal(cachedOther.EntryId, cached?.EntryId);
    Xunit.Assert.Equal(HistoryCommandStatus.Canceled, fixture.Interaction.CancelDictationRename().Status);
  }

  [Xunit.Fact]
  public async Task ChatLoadAndRename_CoversUnsavedAndPersistedConversations()
  {
    ChatHistoryRecord saved = CreateChat("Saved", DateTimeOffset.UtcNow);
    await using Fixture fixture = new();

    WorkbenchHistoryInteractionResult loaded = fixture.Interaction.LoadChat(ChatHistoryItemViewModel.FromRecord(saved));
    WorkbenchHistoryInteractionResult persistedRename = await fixture.Interaction.RenameChatAsync(AppSettings.Default, "Renamed");
    WorkbenchHistoryInteractionResult newChat = fixture.Interaction.BeginNewChat();
    WorkbenchHistoryInteractionResult unsavedRename = await fixture.Interaction.RenameChatAsync(AppSettings.Default, "Draft title");

    Xunit.Assert.True(loaded.ShouldRenderChat);
    Xunit.Assert.Equal(saved.ConversationId, persistedRename.State.SelectedChatConversationId);
    Xunit.Assert.True(persistedRename.ShouldRefresh);
    Xunit.Assert.Equal("New chat", newChat.State.ChatTitle);
    Xunit.Assert.Equal("Draft title", unsavedRename.State.ChatTitle);
    Xunit.Assert.False(unsavedRename.ShouldRefresh);
    Xunit.Assert.Equal(HistoryCommandStatus.Canceled, fixture.Interaction.CancelChatRename().Status);
  }

  [Xunit.Fact]
  public async Task ActiveChatOperation_BlocksLoadNewAndDeleteMutations()
  {
    ChatHistoryRecord saved = CreateChat("Saved", DateTimeOffset.UtcNow);
    await using Fixture fixture = new();
    string originalConversationId = fixture.Chat.ConversationId;
    using WorkbenchChatOperation operation = Xunit.Assert.IsType<WorkbenchChatOperation>(
      fixture.Chat.TryBeginOperation(WorkbenchChatOperationKind.Completion));

    WorkbenchHistoryInteractionResult loaded = fixture.Interaction.LoadChat(ChatHistoryItemViewModel.FromRecord(saved));
    WorkbenchHistoryInteractionResult newChat = fixture.Interaction.BeginNewChat();
    WorkbenchHistoryInteractionResult deleted = await fixture.Interaction.DeleteChatsAsync(
      AppSettings.Default,
      [ChatHistoryItemViewModel.FromRecord(saved)]);

    Xunit.Assert.Equal(HistoryCommandStatus.Canceled, loaded.Status);
    Xunit.Assert.Equal(HistoryCommandStatus.Canceled, newChat.Status);
    Xunit.Assert.Equal(HistoryCommandStatus.Canceled, deleted.Status);
    Xunit.Assert.Equal(originalConversationId, fixture.Chat.ConversationId);
  }

  [Xunit.Fact]
  public async Task RefreshReconciliation_PreservesExistingSelectionAndClearsMissingSelection()
  {
    DictationHistoryRecord dictation = CreateDictation("selected", DateTimeOffset.UtcNow, "selected-session");
    ChatHistoryRecord chat = CreateChat("Saved", DateTimeOffset.UtcNow);
    await using Fixture fixture = new();
    fixture.Interaction.LoadDictationGroup(HistoryItemViewModel.FromRecords([dictation]));
    fixture.Interaction.LoadChat(ChatHistoryItemViewModel.FromRecord(chat));
    WorkbenchHistoryViewState existing = new(
      string.Empty,
      [HistoryItemViewModel.FromRecords([dictation])],
      [ChatHistoryItemViewModel.FromRecord(chat)],
      string.Empty,
      string.Empty,
      IsAvailable: true);

    WorkbenchHistoryInteractionResult preserved = fixture.Interaction.ReconcileRefresh(existing);
    WorkbenchHistoryInteractionResult removed = fixture.Interaction.ReconcileRefresh(new WorkbenchHistoryViewState(
      string.Empty, [], [], string.Empty, string.Empty, IsAvailable: true));

    Xunit.Assert.Equal(dictation.EntryId, preserved.State.SelectedDictationEntryId);
    Xunit.Assert.Equal(chat.ConversationId, preserved.State.SelectedChatConversationId);
    Xunit.Assert.Null(removed.State.SelectedDictationEntryId);
    Xunit.Assert.Null(removed.State.SelectedChatConversationId);
  }

  [Xunit.Fact]
  public void DailyGroupLabels_IdentifyDictationCountsAndPluralize()
  {
    HistoryItemViewModel today = HistoryItemViewModel.FromRecords([
      CreateDictation("one", new DateTimeOffset(DateTime.Today.AddHours(9)), "one"),
      CreateDictation("two", new DateTimeOffset(DateTime.Today.AddHours(10)), "two"),
    ]);
    HistoryItemViewModel yesterday = HistoryItemViewModel.FromRecords([
      CreateDictation("one", new DateTimeOffset(DateTime.Today.AddDays(-1).AddHours(9)), "yesterday"),
    ]);
    DateTime olderDate = DateTime.Today.AddDays(-7).AddHours(9);
    HistoryItemViewModel older = HistoryItemViewModel.FromRecords([
      CreateDictation("one", new DateTimeOffset(olderDate), "older"),
    ]);

    Xunit.Assert.Equal("Today · 2 dictations", today.Title);
    Xunit.Assert.Equal("Yesterday · 1 dictation", yesterday.Title);
    Xunit.Assert.Equal($"{olderDate:MMMM d, yyyy} · 1 dictation", older.Title);
    Xunit.Assert.Contains("dictations", today.Title, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain(" - ", today.Title, StringComparison.Ordinal);
  }

  private static DictationHistoryRecord CreateDictation(string text, DateTimeOffset created, string sessionId) =>
    new DictationHistoryRecord(created, "default", TranscriptionProviderIds.CohereLocal, "model", text, text,
      TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, SessionId: sessionId).Normalize();

  private static ChatHistoryRecord CreateChat(string title, DateTimeOffset created) =>
    new ChatHistoryRecord(Guid.NewGuid().ToString("N"), title, created, created, ChatProviderIds.GemmaLocal, "model",
      [new ChatMessage(ChatMessageRoles.User, "hello", created)]).Normalize();

  private sealed class Fixture : IAsyncDisposable
  {
    private readonly MutableDictationStore dictationStore;
    private readonly MutableChatStore chatStore = new();
    public WorkbenchHistoryController History { get; }
    public WorkbenchChatController Chat { get; }
    public WorkbenchHistoryInteractionController Interaction { get; }

    public Fixture(int deleteCount = 1, Exception? deleteException = null)
    {
      dictationStore = new MutableDictationStore(deleteCount, deleteException);
      History = new WorkbenchHistoryController(
        new WorkbenchHistoryQueryCoordinator(
          (_, _, _) => Task.FromResult<IReadOnlyList<DictationHistoryRecord>>([CreateDictation("selected", DateTimeOffset.UtcNow, "selected-session")]),
          (_, _, _) => Task.FromResult<IReadOnlyList<ChatHistoryRecord>>([CreateChat("Saved", DateTimeOffset.UtcNow)])),
        new HistoryCommandCoordinator(_ => dictationStore, _ => chatStore),
        new Diagnostics());
      Chat = new WorkbenchChatController(_ => new ChatService());
      Interaction = new WorkbenchHistoryInteractionController(History, Chat);
    }

    public async ValueTask DisposeAsync()
    {
      await History.DisposeAsync();
      await Chat.DisposeAsync();
    }
  }

  private sealed class MutableDictationStore(int deleteCount, Exception? deleteException) : IDictationHistoryCommandStore
  {
    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> UpdateAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<int> DeleteSessionsAsync(IEnumerable<string> sessionIds, CancellationToken cancellationToken = default) =>
      deleteException is null ? Task.FromResult(deleteCount) : Task.FromException<int>(deleteException);
  }

  private sealed class MutableChatStore : IChatHistoryCommandStore
  {
    public Task SaveAsync(ChatHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<int> DeleteConversationsAsync(IEnumerable<string> conversationIds, CancellationToken cancellationToken = default) => Task.FromResult(1);
  }

  private sealed class Diagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }

  private sealed class ChatService : IChatCompletionService
  {
    public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
      Task.FromResult(new ChatCompletionResult(string.Empty, "test", "test", TimeSpan.Zero));
  }
}
