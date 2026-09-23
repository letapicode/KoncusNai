using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal static class ChatHistoryTitleFormatter
{
  public const string PlaceholderTitle = "New chat";

  private const int MaxTitleWords = 6;
  // Keeps each New chat title useful in the library without letting a single prompt dominate the rail.
  private const int MaxTitleCharacters = 48;

  public static string ResolveTitle(
    string? requestedTitle,
    IReadOnlyList<ChatMessage> messages)
  {
    string normalizedRequestedTitle = CollapseWhitespace(requestedTitle);
    if (!string.IsNullOrWhiteSpace(normalizedRequestedTitle)
        && !string.Equals(normalizedRequestedTitle, PlaceholderTitle, StringComparison.OrdinalIgnoreCase))
    {
      return TrimToTitleShape(normalizedRequestedTitle);
    }

    string source = (messages ?? Array.Empty<ChatMessage>())
      .Select(message => message.Normalize())
      .FirstOrDefault(message => string.Equals(message.Role, ChatMessageRoles.User, StringComparison.OrdinalIgnoreCase))?
      .Content ?? PlaceholderTitle;

    return SentenceCaseGeneratedTitle(TrimToTitleShape(StripRewriteInstruction(source)));
  }

  public static string FormatSidebarTitle(ChatHistoryRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);

    ChatHistoryRecord normalized = record.Normalize();
    return string.IsNullOrWhiteSpace(normalized.Title)
      ? PlaceholderTitle
      : SentenceCaseGeneratedTitle(normalized.Title);
  }

  private static string StripRewriteInstruction(string value)
  {
    string collapsed = CollapseWhitespace(value);
    if (string.IsNullOrWhiteSpace(collapsed))
    {
      return PlaceholderTitle;
    }

    string lower = collapsed.ToLowerInvariant();
    bool looksLikeRewriteInstruction =
      (lower.Contains("rewrite", StringComparison.Ordinal)
       || lower.Contains("fix", StringComparison.Ordinal)
       || lower.Contains("polish", StringComparison.Ordinal))
      && (lower.Contains("grammar", StringComparison.Ordinal)
          || lower.Contains("flow", StringComparison.Ordinal)
          || lower.Contains("clarity", StringComparison.Ordinal));
    if (!looksLikeRewriteInstruction)
    {
      return collapsed;
    }

    int colonIndex = collapsed.IndexOf(':', StringComparison.Ordinal);
    if (colonIndex > 0 && colonIndex < collapsed.Length - 1)
    {
      return collapsed[(colonIndex + 1)..].Trim();
    }

    int questionIndex = collapsed.IndexOf('?', StringComparison.Ordinal);
    if (questionIndex > 0 && questionIndex < collapsed.Length - 1)
    {
      return collapsed[(questionIndex + 1)..].Trim();
    }

    return collapsed;
  }

  private static string TrimToTitleShape(string value)
  {
    string collapsed = CollapseWhitespace(value).Trim(' ', '\t', '\r', '\n', ':', '.', ',', ';', '-', '"', '\'');
    if (string.IsNullOrWhiteSpace(collapsed))
    {
      return PlaceholderTitle;
    }

    List<string> words = collapsed
      .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
      .Select(CleanWord)
      .Where(word => !string.IsNullOrWhiteSpace(word))
      .Take(MaxTitleWords)
      .ToList();
    if (words.Count == 0)
    {
      return PlaceholderTitle;
    }

    string title = string.Join(' ', words);
    return title.Length <= MaxTitleCharacters
      ? title
      : title[..MaxTitleCharacters].TrimEnd();
  }

  private static string CleanWord(string word)
  {
    StringBuilder builder = new();
    foreach (char value in word)
    {
      if (char.IsLetterOrDigit(value) || value is '\'' or '-')
      {
        builder.Append(value);
      }
    }

    return builder.ToString().Trim('\'', '-');
  }

  private static string SentenceCaseGeneratedTitle(string title)
  {
    if (string.IsNullOrWhiteSpace(title) || !char.IsLower(title[0]))
    {
      return title;
    }

    int firstWordEnd = title.IndexOf(' ');
    ReadOnlySpan<char> firstWord = firstWordEnd < 0
      ? title.AsSpan()
      : title.AsSpan(0, firstWordEnd);
    foreach (char character in firstWord)
    {
      if (char.IsLetter(character) && char.IsUpper(character))
      {
        return title;
      }
    }

    return char.ToUpperInvariant(title[0]) + title[1..];
  }

  private static string CollapseWhitespace(string? value)
  {
    return string.IsNullOrWhiteSpace(value)
      ? string.Empty
      : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
  }
}
