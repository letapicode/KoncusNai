using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchHistoryComposerDirective
{
  None,
  Replace,
  ClearComposerContent,
  ClearHistorySourcedContent,
}

internal sealed record WorkbenchHistoryInteractionState(
  DictationHistoryRecord? SelectedDictationRecord,
  string? SelectedDictationEntryId,
  string? SelectedChatConversationId,
  string ChatTitle)
{
  public bool HasSelectedDictation => SelectedDictationRecord is not null;
}

internal sealed record WorkbenchHistoryInteractionResult(
  HistoryCommandStatus Status,
  string? StatusMessage,
  WorkbenchHistoryInteractionState State,
  bool ShouldRefresh = false,
  WorkbenchHistoryComposerDirective ComposerDirective = WorkbenchHistoryComposerDirective.None,
  string? ComposerText = null,
  bool ShouldFocusComposer = false,
  bool ShouldRenderChat = false,
  bool ShouldRefreshPendingFiles = false,
  bool ShouldSelectChatModel = false);

/// <summary>
/// Owns Workbench history selection and the relationship between a history load and composer text.
/// Persistence remains with <see cref="WorkbenchHistoryController"/>.
/// </summary>
internal sealed class WorkbenchHistoryInteractionController
{
  private const string NewChatTitle = "New chat";

  private readonly WorkbenchHistoryController historyController;
  private readonly WorkbenchChatController chatController;
  private DictationHistoryRecord? selectedDictationRecord;
  private string? selectedDictationEntryId;
  private string? selectedChatConversationId;
  private string chatTitle = NewChatTitle;
  private LoadedDictationComposer? loadedDictationComposer;
  private string dictationSessionId = Guid.NewGuid().ToString("N");

  public WorkbenchHistoryInteractionController(
    WorkbenchHistoryController historyController,
    WorkbenchChatController chatController)
  {
    this.historyController = historyController ?? throw new ArgumentNullException(nameof(historyController));
    this.chatController = chatController ?? throw new ArgumentNullException(nameof(chatController));
  }

  public WorkbenchHistoryInteractionState State => new(
    selectedDictationRecord,
    selectedDictationEntryId,
    selectedChatConversationId,
    chatTitle);

  /// <summary>Identifies the current appendable dictation group for persistence.</summary>
  public string DictationSessionId => dictationSessionId;

  public void BeginNewDictationSession() => dictationSessionId = Guid.NewGuid().ToString("N");

  public void NotifyComposerTextChanged(string? composerText)
  {
    if (loadedDictationComposer is not null
        && !string.Equals(loadedDictationComposer.Text, composerText ?? string.Empty, StringComparison.Ordinal))
    {
      loadedDictationComposer = loadedDictationComposer with { IsPristine = false };
    }
  }

  public WorkbenchHistoryInteractionResult LoadDictationGroup(HistoryItemViewModel group)
  {
    ArgumentNullException.ThrowIfNull(group);
    selectedDictationRecord = group.LatestRecord;
    selectedDictationEntryId = selectedDictationRecord.EntryId;
    string text = group.CombinedFinalText;
    loadedDictationComposer = new LoadedDictationComposer(
      GroupSessionIds(group),
      text,
      IsPristine: true);
    BeginNewDictationSession();
    return Result(
      HistoryCommandStatus.Succeeded,
      $"Loaded {group.Records.Count} dictation{(group.Records.Count == 1 ? string.Empty : "s")}: {group.Title}",
      composerDirective: WorkbenchHistoryComposerDirective.Replace,
      composerText: text,
      shouldFocusComposer: true);
  }

  public WorkbenchHistoryInteractionResult CancelDictationRename() =>
    Result(HistoryCommandStatus.Canceled, "Rename canceled.");

  public WorkbenchHistoryInteractionResult CancelDictationDelete() =>
    Result(HistoryCommandStatus.Canceled, "Delete canceled.");

  public async Task<WorkbenchHistoryInteractionResult> SaveDictationEditAsync(
    AppSettings settings,
    string? editedText,
    CancellationToken cancellationToken = default)
  {
    WorkbenchHistoryMutationResult mutation = await historyController
      .SaveDictationEditAsync(settings, selectedDictationRecord, editedText, cancellationToken)
      .ConfigureAwait(true);
    return ApplyDictationMutation(mutation);
  }

  public async Task<WorkbenchHistoryInteractionResult> RenameDictationAsync(
    AppSettings settings,
    string? requestedTitle,
    CancellationToken cancellationToken = default)
  {
    WorkbenchHistoryMutationResult mutation = await historyController
      .RenameDictationAsync(settings, selectedDictationRecord, requestedTitle, cancellationToken)
      .ConfigureAwait(true);
    return ApplyDictationMutation(mutation);
  }

  public async Task<WorkbenchHistoryInteractionResult> DeleteDictationGroupsAsync(
    AppSettings settings,
    IReadOnlyList<HistoryItemViewModel> groups,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(groups);
    string[] deletedSessionIds = groups.SelectMany(GroupSessionIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    bool deletesLoadedGroup = loadedDictationComposer is not null
      && loadedDictationComposer.SessionIds.Intersect(deletedSessionIds, StringComparer.OrdinalIgnoreCase).Any();
    bool deletesSelection = selectedDictationRecord is not null
      && deletedSessionIds.Contains(SessionId(selectedDictationRecord), StringComparer.OrdinalIgnoreCase);
    WorkbenchHistoryMutationResult mutation = await historyController
      .DeleteDictationGroupsAsync(settings, groups, cancellationToken)
      .ConfigureAwait(true);
    if (mutation.Status != HistoryCommandStatus.Succeeded)
    {
      return FromMutation(mutation);
    }

    // The command contract reports a successful delete only after its serialized store completes.
    LastDictationSessionCache.ClearIfMatches(deletedSessionIds);
    if (deletesLoadedGroup || deletesSelection)
    {
      selectedDictationRecord = null;
      selectedDictationEntryId = null;
    }

    bool clearComposer = deletesLoadedGroup && loadedDictationComposer!.IsPristine;
    if (deletesLoadedGroup)
    {
      loadedDictationComposer = null;
    }

    return Result(
      mutation.Status,
      mutation.Message,
      shouldRefresh: true,
      composerDirective: clearComposer
        ? WorkbenchHistoryComposerDirective.ClearHistorySourcedContent
        : WorkbenchHistoryComposerDirective.None,
      shouldFocusComposer: clearComposer);
  }

  public WorkbenchHistoryInteractionResult LoadChat(ChatHistoryItemViewModel item)
  {
    ArgumentNullException.ThrowIfNull(item);
    if (chatController.IsBusy)
    {
      return Result(HistoryCommandStatus.Canceled, "Finish the current chat action first.");
    }

    ChatHistoryRecord record = item.Record.Normalize();
    chatController.Load(record);
    selectedChatConversationId = record.ConversationId;
    chatTitle = record.Title;
    return Result(
      HistoryCommandStatus.Succeeded,
      $"Loaded: {record.Title}",
      shouldRenderChat: true,
      shouldRefreshPendingFiles: true,
      shouldSelectChatModel: true);
  }

  public WorkbenchHistoryInteractionResult BeginNewChat()
  {
    if (chatController.IsBusy)
    {
      return Result(HistoryCommandStatus.Canceled, "Finish the current chat action first.");
    }

    chatController.NewChat();
    selectedChatConversationId = null;
    chatTitle = NewChatTitle;
    loadedDictationComposer = null;
    return Result(
      HistoryCommandStatus.Succeeded,
      statusMessage: null,
      composerDirective: WorkbenchHistoryComposerDirective.ClearComposerContent,
      shouldRenderChat: true,
      shouldRefreshPendingFiles: true);
  }

  public WorkbenchHistoryInteractionResult CancelChatRename() =>
    Result(HistoryCommandStatus.Canceled, "Chat rename canceled.");

  public WorkbenchHistoryInteractionResult CancelChatDelete() =>
    Result(HistoryCommandStatus.Canceled, "Chat delete canceled.");

  public async Task<WorkbenchHistoryInteractionResult> RenameChatAsync(
    AppSettings settings,
    string? requestedTitle,
    CancellationToken cancellationToken = default)
  {
    if (chatController.IsBusy)
    {
      return Result(HistoryCommandStatus.Canceled, "Finish the current chat action first.");
    }

    string title = requestedTitle?.Trim() ?? string.Empty;
    if (title.Length == 0)
    {
      return Result(HistoryCommandStatus.Canceled, "Enter a chat title before renaming.");
    }

    if (chatController.Messages.Count == 0)
    {
      chatTitle = title;
      return Result(HistoryCommandStatus.Succeeded, "New chat title updated.");
    }

    WorkbenchHistoryMutationResult mutation = await historyController
      .RenameChatAsync(settings, chatController.CreateHistoryRecord(title), cancellationToken)
      .ConfigureAwait(true);
    if (mutation.Status == HistoryCommandStatus.Succeeded && mutation.ChatRecord is not null)
    {
      chatController.MarkSaved(mutation.ChatRecord);
      selectedChatConversationId = mutation.ChatRecord.ConversationId;
      chatTitle = mutation.ChatRecord.Title;
      return Result(mutation.Status, mutation.Message, shouldRefresh: true);
    }

    return FromMutation(mutation);
  }

  public async Task<WorkbenchHistoryInteractionResult> DeleteChatsAsync(
    AppSettings settings,
    IReadOnlyList<ChatHistoryItemViewModel> items,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(items);
    if (chatController.IsBusy)
    {
      return Result(HistoryCommandStatus.Canceled, "Finish the current chat action first.");
    }

    WorkbenchHistoryMutationResult mutation = await historyController
      .DeleteChatsAsync(settings, items, cancellationToken)
      .ConfigureAwait(true);
    if (mutation.Status != HistoryCommandStatus.Succeeded)
    {
      return FromMutation(mutation);
    }

    return BeginNewChat() with
    {
      StatusMessage = mutation.Message,
      ShouldRefresh = true,
    };
  }

  public WorkbenchHistoryInteractionResult ReconcileRefresh(WorkbenchHistoryViewState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    if (!state.IsAvailable)
    {
      return Result(HistoryCommandStatus.Unavailable, statusMessage: null);
    }

    if (selectedDictationEntryId is not null && !state.DictationItems.Any(ContainsSelectedDictation))
    {
      selectedDictationEntryId = null;
      selectedDictationRecord = null;
      loadedDictationComposer = null;
    }

    if (selectedChatConversationId is not null && !state.ChatItems.Any(item =>
          string.Equals(item.Record.ConversationId, selectedChatConversationId, StringComparison.OrdinalIgnoreCase)))
    {
      selectedChatConversationId = null;
    }

    return Result(HistoryCommandStatus.Succeeded, statusMessage: null);
  }

  public void ResetDictationSelection()
  {
    selectedDictationRecord = null;
    selectedDictationEntryId = null;
    loadedDictationComposer = null;
  }

  public void UpdateChatTitle(string? title)
  {
    if (!string.IsNullOrWhiteSpace(title))
    {
      chatTitle = title;
    }
  }

  public void RecordDictationHistoryWrite(WorkbenchDictationHistoryWriteResult result)
  {
    if (result.Status == HistoryCommandStatus.Succeeded)
    {
      selectedDictationRecord = result.Record;
      selectedDictationEntryId = result.Record.EntryId;
      loadedDictationComposer = null;
    }
  }

  private WorkbenchHistoryInteractionResult ApplyDictationMutation(WorkbenchHistoryMutationResult mutation)
  {
    if (mutation.Status == HistoryCommandStatus.Succeeded && mutation.DictationRecord is not null)
    {
      selectedDictationRecord = mutation.DictationRecord;
      selectedDictationEntryId = mutation.DictationRecord.EntryId;
      LastDictationSessionCache.UpdateIfMatches(mutation.DictationRecord);
      return Result(mutation.Status, mutation.Message, shouldRefresh: true);
    }

    if (mutation.Status == HistoryCommandStatus.NotFound)
    {
      selectedDictationRecord = null;
      selectedDictationEntryId = null;
      loadedDictationComposer = null;
    }

    return FromMutation(mutation);
  }

  private WorkbenchHistoryInteractionResult FromMutation(WorkbenchHistoryMutationResult mutation) =>
    Result(mutation.Status, mutation.Message, shouldRefresh: mutation.ShouldRefresh);

  private WorkbenchHistoryInteractionResult Result(
    HistoryCommandStatus status,
    string? statusMessage,
    bool shouldRefresh = false,
    WorkbenchHistoryComposerDirective composerDirective = WorkbenchHistoryComposerDirective.None,
    string? composerText = null,
    bool shouldFocusComposer = false,
    bool shouldRenderChat = false,
    bool shouldRefreshPendingFiles = false,
    bool shouldSelectChatModel = false) => new(
      status,
      statusMessage,
      State,
      shouldRefresh,
      composerDirective,
      composerText,
      shouldFocusComposer,
      shouldRenderChat,
      shouldRefreshPendingFiles,
      shouldSelectChatModel);

  private bool ContainsSelectedDictation(HistoryItemViewModel item) => item.Records.Any(record =>
    string.Equals(record.EntryId, selectedDictationEntryId, StringComparison.OrdinalIgnoreCase));

  private static string[] GroupSessionIds(HistoryItemViewModel group) => group.Records
    .Select(SessionId)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

  private static string SessionId(DictationHistoryRecord record) =>
    string.IsNullOrWhiteSpace(record.SessionId) ? record.EntryId : record.SessionId;

  private sealed record LoadedDictationComposer(string[] SessionIds, string Text, bool IsPristine);
}
