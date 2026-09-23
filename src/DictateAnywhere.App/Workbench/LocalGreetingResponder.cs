using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.App.Workbench;

/// <summary>
/// Handles short, standalone greetings without starting a local chat model.
/// A greeting paired with a request is deliberately not matched so the model retains context.
/// </summary>
internal static class LocalGreetingResponder
{
  private static readonly IReadOnlyDictionary<string, string> RepliesByGreeting =
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["hi"] = "Hi! What can I help you with?",
      ["hello"] = "Hello! What would you like to work on?",
      ["hey"] = "Hey! How can I help?",
      ["hiya"] = "Hiya! What can I help you with?",
      ["howdy"] = "Howdy! What can I help with?",
      ["yo"] = "Hey! What can I help you with?",
      ["hi there"] = "Hi there! What would you like to do?",
      ["hello there"] = "Hello there! How can I help?",
      ["hey there"] = "Hey there! What are you working on?",
      ["how are you"] = "I'm doing well, thanks for asking. What can I help you with?",
      ["how r you"] = "I'm doing well, thanks for asking. What can I help you with?",
      ["how r u"] = "I'm doing well, thanks for asking. What can I help you with?",
      ["how are u"] = "I'm doing well, thanks for asking. What can I help you with?",
      ["hru"] = "I'm doing well, thanks for asking. What can I help you with?",
      ["good morning"] = "Good morning! What can I help you with?",
      ["good afternoon"] = "Good afternoon! What can I help you with?",
      ["good evening"] = "Good evening! What can I help you with?",
    };

  public static bool IsStandaloneGreeting(string? text) =>
    TryCreateReply(text, out _);

  public static bool TryCreateReply(string? text, out string reply)
  {
    string normalized = Normalize(text);
    if (RepliesByGreeting.TryGetValue(normalized, out string? matchedReply))
    {
      reply = matchedReply;
      return true;
    }

    reply = string.Empty;
    return false;
  }

  private static string Normalize(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return string.Empty;
    }

    string trimmed = text.Trim();
    int start = 0;
    int end = trimmed.Length - 1;
    while (start <= end && IsGreetingDecoration(trimmed[start]))
    {
      start++;
    }

    while (end >= start && IsGreetingDecoration(trimmed[end]))
    {
      end--;
    }

    if (start > end)
    {
      return string.Empty;
    }

    return string.Join(
      " ",
      trimmed[start..(end + 1)]
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(word => word.ToLowerInvariant()));
  }

  private static bool IsGreetingDecoration(char character) =>
    char.IsPunctuation(character) || char.IsSymbol(character);
}
