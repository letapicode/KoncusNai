using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.Core.Contracts;

public sealed record ChatCompletionRequest(
  IReadOnlyList<ChatMessage> Messages,
  ChatModelSelection Selection)
{
  public ChatCompletionRequest Normalize()
  {
    ChatModelSelection normalizedSelection = (Selection ?? ChatModelSelection.Default).Normalize();
    IReadOnlyList<ChatMessage> normalizedMessages = (Messages ?? Array.Empty<ChatMessage>())
      .Select(message => message.Normalize())
      .Where(message => !string.IsNullOrWhiteSpace(message.Content))
      .ToArray();

    return new ChatCompletionRequest(normalizedMessages, normalizedSelection);
  }
}
