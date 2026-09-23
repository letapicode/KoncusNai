using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed record WorkbenchHistoryViewState(
  string SearchText,
  IReadOnlyList<HistoryItemViewModel> DictationItems,
  IReadOnlyList<ChatHistoryItemViewModel> ChatItems,
  string DictationStatus,
  string ChatStatus,
  bool IsAvailable);

internal sealed record HistoryItemViewModel(string Title, IReadOnlyList<DictationHistoryRecord> Records)
{
  public DictationHistoryRecord LatestRecord => Records[0];

  public string DateLabel => LatestRecord.CreatedUtc.LocalDateTime.Date == DateTime.Today
    ? "Today"
    : LatestRecord.CreatedUtc.LocalDateTime.Date == DateTime.Today.AddDays(-1)
      ? "Yesterday"
      : LatestRecord.CreatedUtc.LocalDateTime.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);

  public string CountLabel => $"{Records.Count} dictation{(Records.Count == 1 ? string.Empty : "s")}";

  public string CombinedFinalText => string.Join(
    Environment.NewLine + Environment.NewLine,
    Records
      .OrderBy(record => record.CreatedUtc)
      .Select(record => $"[{record.CreatedUtc.LocalDateTime:t}] {record.FinalText}"));

  public static HistoryItemViewModel FromRecords(IReadOnlyList<DictationHistoryRecord> records)
  {
    if (records is null || records.Count == 0)
    {
      throw new ArgumentException("A daily dictation entry needs at least one record.", nameof(records));
    }

    IReadOnlyList<DictationHistoryRecord> normalized = records
      .Select(record => record.Normalize())
      .OrderByDescending(record => record.CreatedUtc)
      .ToArray();
    DateTime date = normalized[0].CreatedUtc.LocalDateTime.Date;
    string dateTitle = date == DateTime.Today
      ? "Today"
      : date == DateTime.Today.AddDays(-1)
        ? "Yesterday"
        : date.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
    string title = $"{dateTitle} · {normalized.Count} dictation{(normalized.Count == 1 ? string.Empty : "s")}";
    return new HistoryItemViewModel(title, normalized);
  }
}

internal sealed record ChatHistoryItemViewModel(string Title, ChatHistoryRecord Record)
{
  public static ChatHistoryItemViewModel FromRecord(ChatHistoryRecord record)
  {
    ChatHistoryRecord normalized = record.Normalize();
    return new ChatHistoryItemViewModel(ChatHistoryTitleFormatter.FormatSidebarTitle(normalized), normalized);
  }
}

internal sealed record WorkbenchHistoryMutationResult(
  HistoryCommandStatus Status,
  string? Message,
  DictationHistoryRecord? DictationRecord = null,
  ChatHistoryRecord? ChatRecord = null,
  int AffectedCount = 0)
{
  public bool ShouldRefresh => Status is HistoryCommandStatus.Succeeded or HistoryCommandStatus.NotFound;
}

/// <summary>Owns the Workbench history-query workflow and maps persistence results to presentation state.</summary>
internal sealed class WorkbenchHistoryController : IAsyncDisposable
{
  private const string UnavailableMessage = "History unavailable. See Diagnostics.";
  private readonly WorkbenchHistoryQueryCoordinator queryCoordinator;
  private readonly HistoryCommandCoordinator commandCoordinator;
  private readonly IDiagnostics diagnostics;
  private bool disposed;

  public WorkbenchHistoryController(
    WorkbenchHistoryQueryCoordinator queryCoordinator,
    HistoryCommandCoordinator commandCoordinator,
    IDiagnostics diagnostics)
  {
    this.queryCoordinator = queryCoordinator ?? throw new ArgumentNullException(nameof(queryCoordinator));
    this.commandCoordinator = commandCoordinator ?? throw new ArgumentNullException(nameof(commandCoordinator));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public bool IsCommandBusy => commandCoordinator.IsBusy;

  public Task<HistoryCommandResult> RecordDictationAsync(
    AppSettings settings,
    DictationHistoryRecord record,
    CancellationToken cancellationToken = default) =>
    commandCoordinator.RecordDictationAsync(settings, record, cancellationToken);

  public Task<HistoryCommandResult> UpdateDictationAsync(
    AppSettings settings,
    DictationHistoryRecord record,
    CancellationToken cancellationToken = default) =>
    commandCoordinator.UpdateDictationAsync(settings, record, cancellationToken);

  public Task<HistoryCommandResult> DeleteDictationSessionsAsync(
    AppSettings settings,
    IEnumerable<string> sessionIds,
    CancellationToken cancellationToken = default) =>
    commandCoordinator.DeleteDictationSessionsAsync(settings, sessionIds, cancellationToken);

  public Task<HistoryCommandResult> SaveChatAsync(
    AppSettings settings,
    ChatHistoryRecord record,
    CancellationToken cancellationToken = default) =>
    commandCoordinator.SaveChatAsync(settings, record, cancellationToken);

  public Task<HistoryCommandResult> DeleteChatConversationsAsync(
    AppSettings settings,
    IEnumerable<string> conversationIds,
    CancellationToken cancellationToken = default) =>
    commandCoordinator.DeleteChatConversationsAsync(settings, conversationIds, cancellationToken);

  public async Task<WorkbenchHistoryMutationResult> SaveDictationEditAsync(
    AppSettings settings,
    DictationHistoryRecord? selectedRecord,
    string? editedText,
    CancellationToken cancellationToken = default)
  {
    if (IsCommandBusy)
    {
      return LocalResult("Finish the current history action first.");
    }

    if (selectedRecord is null)
    {
      return LocalResult("Select a history item first.");
    }

    string normalizedText = editedText?.Trim() ?? string.Empty;
    if (normalizedText.Length == 0)
    {
      return LocalResult("Cannot save an empty history entry.");
    }

    DictationHistoryRecord updated = (selectedRecord with
    {
      FinalText = normalizedText,
      Title = string.Empty,
      Source = string.IsNullOrWhiteSpace(selectedRecord.Source)
        ? "workbench-history-edit"
        : selectedRecord.Source,
    }).Normalize();
    return await ExecuteMutationAsync(
      () => commandCoordinator.UpdateDictationAsync(settings, updated, cancellationToken),
      operation: "save history edits",
      successMessage: "History edits saved.",
      notFoundMessage: "Selected history item was not found.",
      unexpectedMessage: "History save failed unexpectedly. The error was recorded in Diagnostics.",
      dictationRecord: updated).ConfigureAwait(true);
  }

  public async Task<WorkbenchHistoryMutationResult> RenameDictationAsync(
    AppSettings settings,
    DictationHistoryRecord? selectedRecord,
    string? requestedTitle,
    CancellationToken cancellationToken = default)
  {
    if (IsCommandBusy)
    {
      return LocalResult("Finish the current history action first.");
    }

    if (selectedRecord is null)
    {
      return LocalResult("Select a history item first.");
    }

    string title = requestedTitle?.Trim() ?? string.Empty;
    if (title.Length == 0)
    {
      return LocalResult("Enter a name before renaming.");
    }

    DictationHistoryRecord updated = (selectedRecord with { Title = title }).Normalize();
    return await ExecuteMutationAsync(
      () => commandCoordinator.UpdateDictationAsync(settings, updated, cancellationToken),
      operation: "rename history",
      successMessage: "History renamed.",
      notFoundMessage: "Selected history item was not found.",
      unexpectedMessage: "History rename failed unexpectedly. The error was recorded in Diagnostics.",
      dictationRecord: updated).ConfigureAwait(true);
  }

  public async Task<WorkbenchHistoryMutationResult> DeleteDictationGroupsAsync(
    AppSettings settings,
    IReadOnlyList<HistoryItemViewModel> selectedItems,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selectedItems);
    if (IsCommandBusy)
    {
      return LocalResult("Finish the current history action first.");
    }

    if (selectedItems.Count == 0)
    {
      return LocalResult("Select a history item first.");
    }

    string[] sessionIds = selectedItems
      .SelectMany(item => item.Records)
      .Select(record => string.IsNullOrWhiteSpace(record.SessionId) ? record.EntryId : record.SessionId)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    WorkbenchHistoryMutationResult result = await ExecuteMutationAsync(
      () => commandCoordinator.DeleteDictationSessionsAsync(settings, sessionIds, cancellationToken),
      operation: "delete history",
      successMessage: string.Empty,
      notFoundMessage: "Selected history sessions were not found.",
      unexpectedMessage: "History deletion failed unexpectedly. The error was recorded in Diagnostics.")
      .ConfigureAwait(true);
    return result.Status == HistoryCommandStatus.Succeeded
      ? result with
      {
        Message = $"Deleted {result.AffectedCount} history entr{(result.AffectedCount == 1 ? "y" : "ies")}.",
      }
      : result;
  }

  public async Task<WorkbenchHistoryMutationResult> RenameChatAsync(
    AppSettings settings,
    ChatHistoryRecord record,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(record);
    if (IsCommandBusy)
    {
      return LocalResult("Finish the current history action first.");
    }

    ChatHistoryRecord normalized = record.Normalize();
    return await ExecuteMutationAsync(
      () => commandCoordinator.SaveChatAsync(settings, normalized, cancellationToken),
      operation: "rename chat history",
      successMessage: "Chat renamed.",
      notFoundMessage: "Selected chat was not found.",
      unexpectedMessage: "Chat rename failed unexpectedly. The error was recorded in Diagnostics.",
      chatRecord: normalized).ConfigureAwait(true);
  }

  public async Task<WorkbenchHistoryMutationResult> SaveChatConversationAsync(
    AppSettings settings,
    ChatHistoryRecord record,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(record);
    if (IsCommandBusy)
    {
      return LocalResult("Finish the current history action first.");
    }

    ChatHistoryRecord normalized = record.Normalize();
    WorkbenchHistoryMutationResult result = await ExecuteMutationAsync(
      () => commandCoordinator.SaveChatAsync(settings, normalized, cancellationToken),
      operation: "save chat history",
      successMessage: "Chat saved.",
      notFoundMessage: "Chat was not found.",
      unexpectedMessage: "Chat not saved; the error was recorded in Diagnostics.",
      chatRecord: normalized).ConfigureAwait(true);
    return result.Status == HistoryCommandStatus.Canceled
      ? result with { Message = "Chat save canceled." }
      : result;
  }

  public async Task<WorkbenchHistoryMutationResult> DeleteChatsAsync(
    AppSettings settings,
    IReadOnlyList<ChatHistoryItemViewModel> selectedItems,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selectedItems);
    if (IsCommandBusy)
    {
      return LocalResult("Finish the current history action first.");
    }

    if (selectedItems.Count == 0)
    {
      return LocalResult("Select a chat first.");
    }

    WorkbenchHistoryMutationResult result = await ExecuteMutationAsync(
      () => commandCoordinator.DeleteChatConversationsAsync(
        settings,
        selectedItems.Select(item => item.Record.ConversationId),
        cancellationToken),
      operation: "delete chat history",
      successMessage: string.Empty,
      notFoundMessage: "Selected chats were not found.",
      unexpectedMessage: "Chat deletion failed unexpectedly. The error was recorded in Diagnostics.")
      .ConfigureAwait(true);
    return result.Status == HistoryCommandStatus.Succeeded
      ? result with
      {
        Message = $"Deleted {result.AffectedCount} chat{(result.AffectedCount == 1 ? string.Empty : "s")}.",
      }
      : result;
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This workflow boundary converts unexpected persistence failures into an unavailable state and records diagnostics.")]
  public async Task<WorkbenchHistoryViewState?> RefreshAsync(
    AppSettings settings,
    string? searchText,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(settings);

    WorkbenchHistoryQueryResult? result;
    try
    {
      result = await queryCoordinator
        .QueryLatestAsync(settings, searchText, cancellationToken)
        .ConfigureAwait(true);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected Workbench history query failure.", ex);
      return Unavailable(searchText);
    }

    if (result is null)
    {
      return null;
    }

    if (result.Status == WorkbenchHistoryQueryStatus.Unavailable)
    {
      if (result.Failure is not null)
      {
        diagnostics.Error("Workbench history query failed.", result.Failure);
      }

      return Unavailable(result.SearchText);
    }

    IReadOnlyList<HistoryItemViewModel> dictations = result.DictationGroups
      .Select(HistoryItemViewModel.FromRecords)
      .ToArray();
    IReadOnlyList<ChatHistoryItemViewModel> chats = result.Chats
      .Select(ChatHistoryItemViewModel.FromRecord)
      .ToArray();
    return new WorkbenchHistoryViewState(
      result.SearchText,
      dictations,
      chats,
      FormatStatus(dictations.Count, result.SearchText, "session(s)"),
      FormatStatus(chats.Count, result.SearchText, "chat(s)"),
      IsAvailable: true);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    ValueTask queryDisposal = queryCoordinator.DisposeAsync();
    ValueTask commandDisposal = commandCoordinator.DisposeAsync();
    await queryDisposal.ConfigureAwait(true);
    await commandDisposal.ConfigureAwait(true);
  }

  private static WorkbenchHistoryViewState Unavailable(string? searchText) => new(
    searchText ?? string.Empty,
    Array.Empty<HistoryItemViewModel>(),
    Array.Empty<ChatHistoryItemViewModel>(),
    UnavailableMessage,
    UnavailableMessage,
    IsAvailable: false);

  private static string FormatStatus(int itemCount, string searchText, string noun) =>
    HistorySearchFilter.HasSearchText(searchText)
      ? $"{itemCount} matching {noun}."
      : string.Empty;

  private static WorkbenchHistoryMutationResult LocalResult(string message) => new(
    HistoryCommandStatus.Canceled,
    message);

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The Workbench command boundary records unexpected failures and returns actionable presentation state.")]
  private async Task<WorkbenchHistoryMutationResult> ExecuteMutationAsync(
    Func<Task<HistoryCommandResult>> command,
    string operation,
    string successMessage,
    string notFoundMessage,
    string unexpectedMessage,
    DictationHistoryRecord? dictationRecord = null,
    ChatHistoryRecord? chatRecord = null)
  {
    try
    {
      HistoryCommandResult result = await command().ConfigureAwait(true);
      return result.Status switch
      {
        HistoryCommandStatus.Succeeded => new WorkbenchHistoryMutationResult(
          result.Status,
          successMessage,
          dictationRecord,
          chatRecord,
          result.AffectedCount),
        HistoryCommandStatus.NotFound => new WorkbenchHistoryMutationResult(
          result.Status,
          notFoundMessage,
          AffectedCount: result.AffectedCount),
        HistoryCommandStatus.Canceled => new WorkbenchHistoryMutationResult(result.Status, Message: null),
        HistoryCommandStatus.Unavailable => UnavailableMutation(result, operation),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unsupported history command status."),
      };
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      diagnostics.Error($"Unexpected Workbench {operation} failure.", ex);
      return new WorkbenchHistoryMutationResult(HistoryCommandStatus.Unavailable, unexpectedMessage);
    }
  }

  private WorkbenchHistoryMutationResult UnavailableMutation(HistoryCommandResult result, string operation)
  {
    diagnostics.Error($"Could not {operation}.", result.Failure);
    return new WorkbenchHistoryMutationResult(
      HistoryCommandStatus.Unavailable,
      $"Could not {operation}; history is unavailable.");
  }
}
