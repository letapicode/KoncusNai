using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record ChatMessage(
  string Role,
  string Content,
  DateTimeOffset CreatedUtc)
{
  public ChatMessage Normalize()
  {
    string normalizedRole = string.IsNullOrWhiteSpace(Role)
      ? ChatMessageRoles.User
      : Role.Trim().ToLowerInvariant();
    if (normalizedRole is not ChatMessageRoles.System
        and not ChatMessageRoles.User
        and not ChatMessageRoles.Assistant)
    {
      normalizedRole = ChatMessageRoles.User;
    }

    return new ChatMessage(
      normalizedRole,
      Content?.Trim() ?? string.Empty,
      CreatedUtc == default ? DateTimeOffset.UtcNow : CreatedUtc);
  }
}
