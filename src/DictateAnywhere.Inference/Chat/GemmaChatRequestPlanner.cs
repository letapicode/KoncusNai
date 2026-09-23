using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

internal static class GemmaChatRequestPlanner
{
  private const int DefaultMaxNewTokens = 2048;
  private const int MinimumMaxNewTokens = 32;
  private const int MaximumMaxNewTokens = 8192;
  private const int MaxConversationMessages = 12;
  private const int MaxConversationCharacters = 8_000;
  private const int RewriteChunkTargetWords = 90;
  private const int RewriteChunkHardMaxWords = 120;
  private const int RewriteMaxNewTokensPerChunk = 240;

  private const string RewriteSystemPrompt =
    "You rewrite the user's supplied text. Preserve the meaning, fix grammar, punctuation, clarity, and flow, and return only the rewritten text. Do not add commentary.";

  public static GemmaChatRequestPlan Plan(
    IReadOnlyList<ChatMessage> normalizedMessages,
    int configuredMaxNewTokens)
  {
    ArgumentNullException.ThrowIfNull(normalizedMessages);

    int normalizedMaxNewTokens = NormalizeConfiguredMaxNewTokens(configuredMaxNewTokens);
    ChatMessage? lastUserMessage = normalizedMessages.LastOrDefault(message =>
      string.Equals(message.Role, ChatMessageRoles.User, StringComparison.OrdinalIgnoreCase));
    if (lastUserMessage is not null && LooksLikeInlineRewriteRequest(lastUserMessage.Content))
    {
      string rewriteText = ExtractInlineRewriteText(lastUserMessage.Content);
      ChatMessage rewriteUserMessage = lastUserMessage with
      {
        Content = string.IsNullOrWhiteSpace(rewriteText)
          ? lastUserMessage.Content
          : rewriteText,
      };

      return new GemmaChatRequestPlan(
        CreateRewriteGenerations(rewriteUserMessage.Normalize(), normalizedMaxNewTokens),
        IsRewriteStyleRequest: true);
    }

    IReadOnlyList<ChatMessage> plannedMessages = TrimConversation(normalizedMessages);
    return new GemmaChatRequestPlan(
      [
        new GemmaChatGenerationPlan(
          plannedMessages,
          CalculateGeneralTokenBudget(lastUserMessage?.Content ?? string.Empty, normalizedMaxNewTokens)),
      ],
      IsRewriteStyleRequest: false);
  }

  private static IReadOnlyList<GemmaChatGenerationPlan> CreateRewriteGenerations(
    ChatMessage rewriteUserMessage,
    int configuredMaxNewTokens)
  {
    IReadOnlyList<string> chunks = SplitRewriteTextIntoChunks(rewriteUserMessage.Content);
    return chunks
      .Select(chunk => new GemmaChatGenerationPlan(
      [
        new ChatMessage(ChatMessageRoles.System, RewriteSystemPrompt, DateTimeOffset.UtcNow),
        rewriteUserMessage with
        {
          Content = chunk,
        },
      ],
      CalculateRewriteTokenBudget(chunk, configuredMaxNewTokens)))
      .ToArray();
  }

  private static IReadOnlyList<ChatMessage> TrimConversation(IReadOnlyList<ChatMessage> normalizedMessages)
  {
    ChatMessage? systemMessage = normalizedMessages.LastOrDefault(message =>
      string.Equals(message.Role, ChatMessageRoles.System, StringComparison.OrdinalIgnoreCase));
    ChatMessage[] nonSystemMessages = normalizedMessages
      .Where(message => !string.Equals(message.Role, ChatMessageRoles.System, StringComparison.OrdinalIgnoreCase))
      .Reverse()
      .Take(MaxConversationMessages)
      .ToArray();

    List<ChatMessage> selected = new(capacity: nonSystemMessages.Length + (systemMessage is null ? 0 : 1));
    int selectedCharacters = 0;
    foreach (ChatMessage message in nonSystemMessages)
    {
      int messageCharacters = message.Content.Length;
      if (selected.Count > 0 && selectedCharacters + messageCharacters > MaxConversationCharacters)
      {
        break;
      }

      selected.Add(message);
      selectedCharacters += messageCharacters;
    }

    selected.Reverse();
    if (systemMessage is not null)
    {
      selected.Insert(0, systemMessage);
    }

    return selected.Count == 0
      ? normalizedMessages
      : selected;
  }

  private static bool LooksLikeInlineRewriteRequest(string prompt)
  {
    if (string.IsNullOrWhiteSpace(prompt))
    {
      return false;
    }

    string normalized = prompt.Trim().ToLowerInvariant();
    bool asksForRewrite =
      normalized.Contains("rewrite", StringComparison.Ordinal)
      || normalized.Contains("re-write", StringComparison.Ordinal)
      || normalized.Contains("reword", StringComparison.Ordinal)
      || normalized.Contains("polish", StringComparison.Ordinal)
      || normalized.Contains("clean up", StringComparison.Ordinal)
      || normalized.Contains("fix", StringComparison.Ordinal);
    bool asksForLanguageCleanup =
      normalized.Contains("grammar", StringComparison.Ordinal)
      || normalized.Contains("flow", StringComparison.Ordinal)
      || normalized.Contains("punctuation", StringComparison.Ordinal)
      || normalized.Contains("clarity", StringComparison.Ordinal)
      || normalized.Contains("readability", StringComparison.Ordinal)
      || normalized.Contains("wording", StringComparison.Ordinal)
      || normalized.Contains("sentence", StringComparison.Ordinal)
      || normalized.Contains("paragraph", StringComparison.Ordinal)
      || normalized.Contains("prose", StringComparison.Ordinal);

    return asksForRewrite && asksForLanguageCleanup;
  }

  internal static string ExtractInlineRewriteText(string prompt)
  {
    if (string.IsNullOrWhiteSpace(prompt))
    {
      return string.Empty;
    }

    string trimmed = prompt.Trim();
    string[] newlineSeparators = ["\r\n\r\n", "\n\n", "\r\n", "\n"];
    foreach (string separator in newlineSeparators)
    {
      int separatorIndex = trimmed.IndexOf(separator, StringComparison.Ordinal);
      if (separatorIndex <= 0)
      {
        continue;
      }

      string prefix = trimmed[..separatorIndex];
      string suffix = trimmed[(separatorIndex + separator.Length)..].Trim();
      if (LooksLikeInlineRewriteRequest(prefix) && CountWords(suffix) >= 3)
      {
        return suffix;
      }
    }

    string questionSuffix = ExtractAfterDelimiter(trimmed, '?');
    if (!string.IsNullOrWhiteSpace(questionSuffix))
    {
      return questionSuffix;
    }

    string colonSuffix = ExtractAfterDelimiter(trimmed, ':');
    return string.IsNullOrWhiteSpace(colonSuffix)
      ? trimmed
      : colonSuffix;
  }

  private static string ExtractAfterDelimiter(string prompt, char delimiter)
  {
    int delimiterIndex = prompt.IndexOf(delimiter, StringComparison.Ordinal);
    if (delimiterIndex <= 0 || delimiterIndex >= prompt.Length - 1)
    {
      return string.Empty;
    }

    string prefix = prompt[..delimiterIndex];
    string suffix = prompt[(delimiterIndex + 1)..].Trim(' ', '\t', '\r', '\n', ':', '"', '\'');
    return LooksLikeInlineRewriteRequest(prefix) && CountWords(suffix) >= 3
      ? suffix
      : string.Empty;
  }

  private static int CalculateRewriteTokenBudget(string text, int configuredMaxNewTokens)
  {
    int sourceWords = CountWords(text);
    int targetBudget = Math.Max(96, (sourceWords * 2) + 24);
    int cappedBudget = Math.Min(configuredMaxNewTokens, RewriteMaxNewTokensPerChunk);
    return Math.Clamp(targetBudget, MinimumMaxNewTokens, cappedBudget);
  }

  private static int CalculateGeneralTokenBudget(string lastUserMessage, int configuredMaxNewTokens)
  {
    _ = lastUserMessage;
    return configuredMaxNewTokens;
  }

  private static int NormalizeConfiguredMaxNewTokens(int configuredMaxNewTokens)
  {
    int value = configuredMaxNewTokens <= 0 ? DefaultMaxNewTokens : configuredMaxNewTokens;
    return Math.Clamp(value, MinimumMaxNewTokens, MaximumMaxNewTokens);
  }

  private static int CountWords(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return 0;
    }

    return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
  }

  private static IReadOnlyList<string> SplitRewriteTextIntoChunks(string text)
  {
    string normalized = string.IsNullOrWhiteSpace(text)
      ? string.Empty
      : text.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
      return [normalized];
    }

    List<string> chunks = [];
    StringBuilder current = new();
    int currentWords = 0;

    foreach (string unit in SplitIntoSentenceLikeUnits(normalized))
    {
      int unitWords = CountWords(unit);
      if (unitWords == 0)
      {
        continue;
      }

      if (unitWords > RewriteChunkHardMaxWords)
      {
        FlushCurrentChunk(chunks, current, ref currentWords);
        chunks.AddRange(SplitByWordCount(unit, RewriteChunkTargetWords));
        continue;
      }

      if (currentWords > 0 && currentWords + unitWords > RewriteChunkTargetWords)
      {
        FlushCurrentChunk(chunks, current, ref currentWords);
      }

      if (current.Length > 0)
      {
        current.Append(' ');
      }

      current.Append(unit.Trim());
      currentWords += unitWords;
    }

    FlushCurrentChunk(chunks, current, ref currentWords);
    return chunks.Count == 0 ? [normalized] : chunks;
  }

  private static IReadOnlyList<string> SplitIntoSentenceLikeUnits(string text)
  {
    List<string> units = [];
    StringBuilder current = new();
    for (int i = 0; i < text.Length; i++)
    {
      char value = text[i];
      current.Append(value);
      if (!IsSentenceTerminator(value))
      {
        continue;
      }

      bool atEnd = i == text.Length - 1;
      bool nextIsWhitespace = !atEnd && char.IsWhiteSpace(text[i + 1]);
      if (atEnd || nextIsWhitespace)
      {
        AddCurrentUnit(units, current);
      }
    }

    AddCurrentUnit(units, current);
    return units;
  }

  private static bool IsSentenceTerminator(char value)
  {
    return value is '.' or '!' or '?' or ';';
  }

  private static IReadOnlyList<string> SplitByWordCount(string text, int maxWords)
  {
    string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (words.Length <= maxWords)
    {
      return [text.Trim()];
    }

    List<string> chunks = [];
    for (int index = 0; index < words.Length; index += maxWords)
    {
      chunks.Add(string.Join(' ', words.Skip(index).Take(maxWords)));
    }

    return chunks;
  }

  private static void AddCurrentUnit(List<string> units, StringBuilder current)
  {
    if (current.Length == 0)
    {
      return;
    }

    string unit = current.ToString().Trim();
    if (!string.IsNullOrWhiteSpace(unit))
    {
      units.Add(unit);
    }

    current.Clear();
  }

  private static void FlushCurrentChunk(
    List<string> chunks,
    StringBuilder current,
    ref int currentWords)
  {
    if (current.Length == 0)
    {
      return;
    }

    chunks.Add(current.ToString().Trim());
    current.Clear();
    currentWords = 0;
  }
}

internal sealed record GemmaChatRequestPlan(
  IReadOnlyList<GemmaChatGenerationPlan> Generations,
  bool IsRewriteStyleRequest)
{
  public IReadOnlyList<ChatMessage> Messages => Generations.Count == 0
    ? Array.Empty<ChatMessage>()
    : Generations[0].Messages;

  public int MaxNewTokens => Generations.Count == 0
    ? 0
    : Generations.Max(generation => generation.MaxNewTokens);

  public int GenerationCount => Generations.Count;
}

internal sealed record GemmaChatGenerationPlan(
  IReadOnlyList<ChatMessage> Messages,
  int MaxNewTokens);
