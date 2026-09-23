using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal sealed class LocalDictationHistoryStore : IDictationHistoryStore, IDictationHistoryCommandStore
{
  private readonly VersionedJsonLinesFile<DictationHistoryRecord> records;

  public LocalDictationHistoryStore(string historyFilePath)
  {
    records = new VersionedJsonLinesFile<DictationHistoryRecord>(historyFilePath, static record =>
      !string.IsNullOrWhiteSpace(record.EntryId) && record.RawTranscript is not null && record.FinalText is not null);
  }

  public static string DefaultHistoryFilePath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DictateAnywhere",
    "history",
    "dictation-history.local.jsonl");

  public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) =>
    records.AppendAsync(record.Normalize(), cancellationToken);

  public async Task<DictationHistoryRecord?> ReadLatestAsync(CancellationToken cancellationToken = default)
  {
    IReadOnlyList<DictationHistoryRecord> recent = await ReadRecentAsync(1, cancellationToken).ConfigureAwait(false);
    return recent.Count == 0 ? null : recent[0];
  }

  public async Task<IReadOnlyList<DictationHistoryRecord>> ReadRecentAsync(
    int limit,
    CancellationToken cancellationToken = default)
  {
    IReadOnlyList<DictationHistoryRecord> recent = await records
      .ReadRecentAsync(limit, cancellationToken)
      .ConfigureAwait(false);
    return recent.Select(static record => record.Normalize()).ToArray();
  }

  public Task<IReadOnlyList<DictationHistoryRecord>> SearchRecentAsync(
    string? searchText, int limit, CancellationToken cancellationToken = default) =>
    records.ReadRecentAsync(limit, cancellationToken, HistorySearchFilter.DictationPredicate(searchText));

  public async Task<bool> UpdateAsync(
    DictationHistoryRecord record,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(record);
    DictationHistoryRecord normalized = record.Normalize();
    int affected = await records.RewriteAsync(
        existing => string.Equals(
          existing.Normalize().EntryId,
          normalized.EntryId,
          StringComparison.OrdinalIgnoreCase)
            ? RecordRewrite<DictationHistoryRecord>.Replace(normalized)
            : RecordRewrite<DictationHistoryRecord>.Keep(),
        cancellationToken: cancellationToken)
      .ConfigureAwait(false);
    return affected > 0;
  }

  public Task<int> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
    DeleteSessionsAsync([sessionId], cancellationToken);

  public Task<int> DeleteSessionsAsync(
    IEnumerable<string> sessionIds,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(sessionIds);
    HashSet<string> ids = sessionIds
      .Where(static id => !string.IsNullOrWhiteSpace(id))
      .Select(static id => id.Trim())
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (ids.Count == 0)
    {
      return Task.FromResult(0);
    }

    return records.RewriteAsync(
      existing => Matches(existing.Normalize(), ids)
        ? RecordRewrite<DictationHistoryRecord>.Delete()
        : RecordRewrite<DictationHistoryRecord>.Keep(),
      cancellationToken: cancellationToken);
  }

  internal Task ImportMissingAsync(
    IEnumerable<DictationHistoryRecord> importedRecords,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(importedRecords);
    Dictionary<string, DictationHistoryRecord> pending = importedRecords
      .Select(static record => record.Normalize())
      .GroupBy(static record => record.EntryId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);
    if (pending.Count == 0)
    {
      return Task.CompletedTask;
    }

    return records.RewriteAsync(
      existing =>
      {
        _ = pending.Remove(existing.Normalize().EntryId);
        return RecordRewrite<DictationHistoryRecord>.Keep();
      },
      appendRecords: pending.Values,
      cancellationToken: cancellationToken);
  }

  internal Task<bool> ContainsAllEntryIdsAsync(
    IEnumerable<string> entryIds,
    CancellationToken cancellationToken = default) =>
    records.ContainsAllAsync(
      static record => record.Normalize().EntryId,
      entryIds,
      cancellationToken);

  private static bool Matches(DictationHistoryRecord record, HashSet<string> ids)
  {
    string sessionId = string.IsNullOrWhiteSpace(record.SessionId) ? record.EntryId : record.SessionId;
    return ids.Contains(sessionId) || ids.Contains(record.EntryId);
  }
}
