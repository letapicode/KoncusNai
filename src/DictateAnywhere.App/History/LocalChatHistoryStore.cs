using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal sealed class LocalChatHistoryStore : IChatHistoryCommandStore
{
  private readonly VersionedJsonLinesFile<ChatHistoryRecord> records;

  public LocalChatHistoryStore(string historyFilePath)
  {
    records = new VersionedJsonLinesFile<ChatHistoryRecord>(historyFilePath, static record =>
      !string.IsNullOrWhiteSpace(record.ConversationId) && record.Messages is not null
      && record.Messages.All(static message => message is not null && message.Content is not null));
  }

  public static string DefaultHistoryFilePath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DictateAnywhere",
    "history",
    "chat-history.local.jsonl");

  public Task SaveAsync(ChatHistoryRecord record, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(record);
    ChatHistoryRecord normalized = record.Normalize();
    return SaveNormalizedAsync(normalized, cancellationToken);
  }

  public async Task<IReadOnlyList<ChatHistoryRecord>> ReadRecentAsync(
    int limit,
    CancellationToken cancellationToken = default)
  {
    IReadOnlyList<ChatHistoryRecord> recent = await records
      .ReadRecentAsync(limit, cancellationToken)
      .ConfigureAwait(false);
    return recent.Select(static record => record.Normalize()).ToArray();
  }

  public Task<IReadOnlyList<ChatHistoryRecord>> SearchRecentAsync(
    string? searchText, int limit, CancellationToken cancellationToken = default) =>
    records.ReadRecentAsync(limit, cancellationToken, HistorySearchFilter.ChatPredicate(searchText));

  public async Task<bool> RenameAsync(
    string conversationId,
    string title,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(title))
    {
      return false;
    }

    string id = conversationId.Trim();
    string normalizedTitle = title.Trim();
    int affected = await records.RewriteAsync(
        existing => string.Equals(existing.Normalize().ConversationId, id, StringComparison.OrdinalIgnoreCase)
          ? RecordRewrite<ChatHistoryRecord>.Replace(existing.Normalize() with
          {
            Title = normalizedTitle,
            UpdatedUtc = DateTimeOffset.UtcNow,
          })
          : RecordRewrite<ChatHistoryRecord>.Keep(),
        cancellationToken: cancellationToken)
      .ConfigureAwait(false);
    return affected > 0;
  }

  public Task<int> DeleteConversationAsync(
    string conversationId,
    CancellationToken cancellationToken = default) =>
    DeleteConversationsAsync([conversationId], cancellationToken);

  public Task<int> DeleteConversationsAsync(
    IEnumerable<string> conversationIds,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(conversationIds);
    HashSet<string> ids = conversationIds
      .Where(static id => !string.IsNullOrWhiteSpace(id))
      .Select(static id => id.Trim())
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (ids.Count == 0)
    {
      return Task.FromResult(0);
    }

    return records.RewriteAsync(
      existing => ids.Contains(existing.Normalize().ConversationId)
        ? RecordRewrite<ChatHistoryRecord>.Delete()
        : RecordRewrite<ChatHistoryRecord>.Keep(),
      cancellationToken: cancellationToken);
  }

  private async Task SaveNormalizedAsync(ChatHistoryRecord normalized, CancellationToken cancellationToken)
  {
    await records.RewriteAsync(
        existing => string.Equals(
          existing.Normalize().ConversationId,
          normalized.ConversationId,
          StringComparison.OrdinalIgnoreCase)
            ? RecordRewrite<ChatHistoryRecord>.Delete()
            : RecordRewrite<ChatHistoryRecord>.Keep(),
        appendRecords: [normalized],
        cancellationToken: cancellationToken)
      .ConfigureAwait(false);
  }

  internal Task ImportMissingAsync(
    IEnumerable<ChatHistoryRecord> importedRecords,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(importedRecords);
    Dictionary<string, ChatHistoryRecord> pending = importedRecords
      .Select(static record => record.Normalize())
      .GroupBy(static record => record.ConversationId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);
    if (pending.Count == 0)
    {
      return Task.CompletedTask;
    }

    return records.RewriteAsync(
      existing =>
      {
        _ = pending.Remove(existing.Normalize().ConversationId);
        return RecordRewrite<ChatHistoryRecord>.Keep();
      },
      appendRecords: pending.Values,
      cancellationToken: cancellationToken);
  }

  internal Task<bool> ContainsAllConversationIdsAsync(
    IEnumerable<string> conversationIds,
    CancellationToken cancellationToken = default) =>
    records.ContainsAllAsync(
      static record => record.Normalize().ConversationId,
      conversationIds,
      cancellationToken);
}
