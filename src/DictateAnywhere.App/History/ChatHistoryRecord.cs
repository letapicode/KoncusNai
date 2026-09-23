using System;
using System.Collections.Generic;
using System.Linq;

using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal sealed record ChatHistoryRecord(
  string ConversationId,
  string Title,
  DateTimeOffset CreatedUtc,
  DateTimeOffset UpdatedUtc,
  string ProviderId,
  string ModelId,
  IReadOnlyList<ChatMessage> Messages)
{
  public ChatHistoryRecord Normalize()
  {
    IReadOnlyList<ChatMessage> normalizedMessages = (Messages ?? Array.Empty<ChatMessage>())
      .Select(message => message.Normalize())
      .Where(message => !string.IsNullOrWhiteSpace(message.Content))
      .OrderBy(message => message.CreatedUtc)
      .ToArray();
    DateTimeOffset createdUtc = CreatedUtc == default ? DateTimeOffset.UtcNow : CreatedUtc;
    DateTimeOffset updatedUtc = UpdatedUtc == default ? createdUtc : UpdatedUtc;
    if (normalizedMessages.Count > 0)
    {
      createdUtc = CreatedUtc == default
        ? normalizedMessages[0].CreatedUtc
        : CreatedUtc;
      updatedUtc = UpdatedUtc == default
        ? normalizedMessages[^1].CreatedUtc
        : UpdatedUtc;
    }

    ChatModelSelection selection = new(ProviderId, ModelId);
    selection = selection.Normalize();

    return new ChatHistoryRecord(
      string.IsNullOrWhiteSpace(ConversationId) ? Guid.NewGuid().ToString("N") : ConversationId.Trim(),
      string.IsNullOrWhiteSpace(Title) ? BuildDefaultTitle(normalizedMessages) : Title.Trim(),
      createdUtc,
      updatedUtc,
      selection.ProviderId,
      selection.ModelId,
      normalizedMessages);
  }

  private static string BuildDefaultTitle(IReadOnlyList<ChatMessage> messages)
  {
    string source = messages
      .FirstOrDefault(message => string.Equals(message.Role, ChatMessageRoles.User, StringComparison.Ordinal))?
      .Content ?? "New chat";
    string collapsed = string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    if (collapsed.Length <= 48)
    {
      return collapsed;
    }

    return collapsed[..48].TrimEnd() + "...";
  }
}
