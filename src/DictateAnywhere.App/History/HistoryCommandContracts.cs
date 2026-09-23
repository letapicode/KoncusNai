using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal enum HistoryCommandStatus
{
  Succeeded,
  NotFound,
  Unavailable,
  Canceled,
}

internal sealed record HistoryCommandResult(
  HistoryCommandStatus Status,
  int AffectedCount,
  Exception? Failure)
{
  public static HistoryCommandResult Succeeded(int affectedCount = 1) => new(
    HistoryCommandStatus.Succeeded,
    affectedCount,
    Failure: null);

  public static HistoryCommandResult NotFound() => new(
    HistoryCommandStatus.NotFound,
    AffectedCount: 0,
    Failure: null);

  public static HistoryCommandResult Unavailable(Exception failure) => new(
    HistoryCommandStatus.Unavailable,
    AffectedCount: 0,
    failure ?? throw new ArgumentNullException(nameof(failure)));

  public static HistoryCommandResult Canceled() => new(
    HistoryCommandStatus.Canceled,
    AffectedCount: 0,
    Failure: null);
}

internal interface IDictationHistoryCommandStore
{
  Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default);

  Task<bool> UpdateAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default);

  Task<int> DeleteSessionsAsync(
    IEnumerable<string> sessionIds,
    CancellationToken cancellationToken = default);
}

internal interface IChatHistoryCommandStore
{
  Task SaveAsync(ChatHistoryRecord record, CancellationToken cancellationToken = default);

  Task<int> DeleteConversationsAsync(
    IEnumerable<string> conversationIds,
    CancellationToken cancellationToken = default);
}
