using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>Adapts outgoing messages to the selected model's template without changing history.</summary>
internal static class LlamaCppPromptFormatter
{
  public static IReadOnlyList<ChatMessage> Format(ChatCompletionRequest request, string responsePolicy)
  {
    string instructions = string.Join("\n\n", new[] { responsePolicy }.Concat(
      request.Messages.Where(message => message.Role == ChatMessageRoles.System).Select(message => message.Content)));
    List<ChatMessage> turns = request.Messages.Where(message => message.Role != ChatMessageRoles.System).ToList();
    bool gemma3 = string.Equals(request.Selection.ModelId, LlamaCppModelCatalog.Gemma3FourBModelId, StringComparison.OrdinalIgnoreCase);
    if (!gemma3)
    {
      // Templates generally accept one initial system turn, not several separate ones.
      return [new ChatMessage(ChatMessageRoles.System, instructions, DateTimeOffset.UtcNow), .. turns];
    }

    // Gemma 3 uses alternating user/model turns. Its instructions belong in the
    // initial user turn (https://ai.google.dev/gemma/docs/core/prompt-structure).
    // Canceled or failed requests can leave consecutive user messages in history.
    List<ChatMessage> alternating = [];
    foreach (ChatMessage message in turns)
    {
      if (alternating.Count > 0 && alternating[^1].Role == message.Role)
      {
        alternating[^1] = alternating[^1] with { Content = alternating[^1].Content + "\n\n" + message.Content };
      }
      else
      {
        alternating.Add(message);
      }
    }
    if (alternating.Count == 0 || alternating[0].Role != ChatMessageRoles.User || alternating[^1].Role != ChatMessageRoles.User)
    {
      throw new InvalidOperationException("Gemma 3 needs a conversation starting and ending with a user message. Start a new chat and retry your question.");
    }
    alternating[0] = alternating[0] with { Content = instructions + "\n\n" + alternating[0].Content };
    return alternating;
  }
}
