using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>Bounds local chat context while preserving instructions and complete recent turns.</summary>
internal static class LlamaCppPromptBudget
{
  internal const int MaximumCharacters = 8_000;
  internal const int MaximumTurns = 12;

  public static ChatCompletionRequest Apply(ChatCompletionRequest request)
  {
    ChatCompletionRequest normalized = request.Normalize();
    ChatMessage[] instructions = normalized.Messages
      .Where(message => message.Role == ChatMessageRoles.System).ToArray();
    ChatMessage[] turns = normalized.Messages
      .Where(message => message.Role != ChatMessageRoles.System).TakeLast(MaximumTurns).ToArray();
    int instructionCharacters = instructions.Sum(message => message.Content.Length);
    if (instructionCharacters >= MaximumCharacters)
    {
      throw new InvalidOperationException(
        "The file and app context is too large for the selected local model. Remove a file or start a new chat.");
    }

    List<ChatMessage> retained = [];
    int remaining = MaximumCharacters - instructionCharacters;
    for (int index = turns.Length - 1; index >= 0; index--)
    {
      ChatMessage turn = turns[index];
      if (turn.Content.Length > remaining)
      {
        if (index == turns.Length - 1)
        {
          throw new InvalidOperationException(
            "The latest message is too large for the selected local model. Shorten it or remove an attached file.");
        }

        break;
      }

      retained.Insert(0, turn);
      remaining -= turn.Content.Length;
    }

    // Cutting by count or size can retain an answer after dropping its question.
    // Gemma rejects that leading assistant turn; other models lose its meaning.
    while (retained.Count > 0 && retained[0].Role == ChatMessageRoles.Assistant)
    {
      retained.RemoveAt(0);
    }
    return new ChatCompletionRequest([.. instructions, .. retained], normalized.Selection);
  }
}
