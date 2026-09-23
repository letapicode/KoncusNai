using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal enum HistoryQueryStatus
{
  Ready,
  Unavailable,
}

internal sealed record HistoryQueryResult(
  HistoryQueryStatus Status,
  string SearchText,
  IReadOnlyList<DictationHistoryRecord> Records,
  Exception? Failure)
{
  public static HistoryQueryResult Unavailable(
    string searchText,
    Exception failure) => new(
      HistoryQueryStatus.Unavailable,
      searchText,
      Array.Empty<DictationHistoryRecord>(),
      failure);
}

/// <summary>Loads the latest filtered snapshot for the standalone history window.</summary>
internal sealed class HistoryQueryCoordinator : IAsyncDisposable
{
  private const int ReadLimit = 200;

  private readonly Func<AppSettings, string, int, CancellationToken, Task<IReadOnlyList<DictationHistoryRecord>>> readAsync;
  private readonly LatestOperationSession querySession = new();

  internal HistoryQueryCoordinator(
    Func<AppSettings, int, CancellationToken, Task<IReadOnlyList<DictationHistoryRecord>>> readAsync)
    : this((settings, _, limit, token) => readAsync(settings, limit, token))
  {
    ArgumentNullException.ThrowIfNull(readAsync);
  }

  internal HistoryQueryCoordinator(
    Func<AppSettings, string, int, CancellationToken, Task<IReadOnlyList<DictationHistoryRecord>>> readAsync)
  {
    this.readAsync = readAsync ?? throw new ArgumentNullException(nameof(readAsync));
  }

  public async Task<HistoryQueryResult?> QueryLatestAsync(
    AppSettings settings,
    string? searchText,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    string normalizedSearchText = searchText ?? string.Empty;

    LatestOperationResult<HistoryQueryResult> outcome = await querySession
      .RunLatestAsync(
        token => QueryCoreAsync(settings, normalizedSearchText, token),
        cancellationToken)
      .ConfigureAwait(true);
    cancellationToken.ThrowIfCancellationRequested();
    return outcome.Completed ? outcome.Value : null;
  }

  public ValueTask DisposeAsync() => querySession.DisposeAsync();

  private async Task<HistoryQueryResult> QueryCoreAsync(
    AppSettings settings,
    string searchText,
    CancellationToken cancellationToken)
  {
    IReadOnlyList<DictationHistoryRecord> records;
    try
    {
      records = await readAsync(settings, searchText, ReadLimit, cancellationToken).ConfigureAwait(true);
      cancellationToken.ThrowIfCancellationRequested();
    }
    catch (Exception ex) when (cancellationToken.IsCancellationRequested)
    {
      throw new OperationCanceledException("History query was cancelled.", ex, cancellationToken);
    }
    catch (Exception ex) when (HistoryPersistenceFailureClassifier.IsExpected(ex))
    {
      return HistoryQueryResult.Unavailable(searchText, ex);
    }

    IReadOnlyList<DictationHistoryRecord> filteredRecords = HistorySearchFilter
      .FilterLatestDictationSessions(records, searchText)
      .ToArray();
    return new HistoryQueryResult(
      HistoryQueryStatus.Ready,
      searchText,
      filteredRecords,
      Failure: null);
  }
}
