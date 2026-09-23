using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchHistoryQueryStatus
{
  Ready,
  Unavailable,
}

internal sealed record WorkbenchHistoryQueryResult(
  WorkbenchHistoryQueryStatus Status,
  string SearchText,
  IReadOnlyList<IReadOnlyList<DictationHistoryRecord>> DictationGroups,
  IReadOnlyList<ChatHistoryRecord> Chats,
  Exception? Failure)
{
  public static WorkbenchHistoryQueryResult Unavailable(
    string searchText,
    Exception failure) => new(
      WorkbenchHistoryQueryStatus.Unavailable,
      searchText,
      Array.Empty<IReadOnlyList<DictationHistoryRecord>>(),
      Array.Empty<ChatHistoryRecord>(),
      failure);
}

/// <summary>Loads the latest filtered Workbench history snapshot without owning WPF presentation.</summary>
internal sealed class WorkbenchHistoryQueryCoordinator : IAsyncDisposable
{
  private const int ReadLimit = 100;

  private readonly Func<AppSettings, string, int, CancellationToken, Task<IReadOnlyList<DictationHistoryRecord>>> readDictationsAsync;
  private readonly Func<AppSettings, string, int, CancellationToken, Task<IReadOnlyList<ChatHistoryRecord>>> readChatsAsync;
  private readonly LatestOperationSession querySession = new();

  internal WorkbenchHistoryQueryCoordinator(
    Func<AppSettings, int, CancellationToken, Task<IReadOnlyList<DictationHistoryRecord>>> readDictationsAsync,
    Func<AppSettings, int, CancellationToken, Task<IReadOnlyList<ChatHistoryRecord>>> readChatsAsync)
    : this((settings, _, limit, token) => readDictationsAsync(settings, limit, token),
      (settings, _, limit, token) => readChatsAsync(settings, limit, token))
  {
    ArgumentNullException.ThrowIfNull(readDictationsAsync);
    ArgumentNullException.ThrowIfNull(readChatsAsync);
  }

  internal WorkbenchHistoryQueryCoordinator(
    Func<AppSettings, string, int, CancellationToken, Task<IReadOnlyList<DictationHistoryRecord>>> readDictationsAsync,
    Func<AppSettings, string, int, CancellationToken, Task<IReadOnlyList<ChatHistoryRecord>>> readChatsAsync)
  {
    this.readDictationsAsync = readDictationsAsync ?? throw new ArgumentNullException(nameof(readDictationsAsync));
    this.readChatsAsync = readChatsAsync ?? throw new ArgumentNullException(nameof(readChatsAsync));
  }

  public async Task<WorkbenchHistoryQueryResult?> QueryLatestAsync(
    AppSettings settings,
    string? searchText,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    string normalizedSearchText = searchText ?? string.Empty;

    LatestOperationResult<WorkbenchHistoryQueryResult> outcome = await querySession
      .RunLatestAsync(
        token => QueryCoreAsync(settings, normalizedSearchText, token),
        cancellationToken)
      .ConfigureAwait(true);
    cancellationToken.ThrowIfCancellationRequested();
    return outcome.Completed ? outcome.Value : null;
  }

  public ValueTask DisposeAsync() => querySession.DisposeAsync();

  private async Task<WorkbenchHistoryQueryResult> QueryCoreAsync(
    AppSettings settings,
    string searchText,
    CancellationToken cancellationToken)
  {
    IReadOnlyList<DictationHistoryRecord> dictations;
    IReadOnlyList<ChatHistoryRecord> chatRecords;
    try
    {
      Task<IReadOnlyList<DictationHistoryRecord>> dictationTask = readDictationsAsync(
        settings,
        searchText,
        ReadLimit,
        cancellationToken);
      Task<IReadOnlyList<ChatHistoryRecord>> chatTask = readChatsAsync(
        settings,
        searchText,
        ReadLimit,
        cancellationToken);
      await Task.WhenAll(dictationTask, chatTask).ConfigureAwait(true);
      cancellationToken.ThrowIfCancellationRequested();
      dictations = await dictationTask.ConfigureAwait(true);
      chatRecords = await chatTask.ConfigureAwait(true);
    }
    catch (Exception ex) when (cancellationToken.IsCancellationRequested)
    {
      throw new OperationCanceledException("Workbench history query was cancelled.", ex, cancellationToken);
    }
    catch (Exception ex) when (HistoryPersistenceFailureClassifier.IsExpected(ex))
    {
      return WorkbenchHistoryQueryResult.Unavailable(searchText, ex);
    }

    IReadOnlyList<IReadOnlyList<DictationHistoryRecord>> dictationGroups = HistorySearchFilter
      .GroupDictationByDay(dictations, searchText)
      .Select(group => (IReadOnlyList<DictationHistoryRecord>)group.ToArray())
      .ToArray();
    IReadOnlyList<ChatHistoryRecord> chats = HistorySearchFilter
      .FilterChats(chatRecords, searchText)
      .ToArray();
    return new WorkbenchHistoryQueryResult(
      WorkbenchHistoryQueryStatus.Ready,
      searchText,
      dictationGroups,
      chats,
      Failure: null);
  }
}
