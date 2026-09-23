using System;
using System.IO;
using System.Text.RegularExpressions;

namespace DictateAnywhere.App.Workbench;

internal static partial class ReadableDocumentTitleResolver
{
  private const int MaximumTitleWords = 12;

  internal static string Resolve(string filePathOrFallbackTitle, string text)
  {
    string fallback = Path.GetFileNameWithoutExtension(filePathOrFallbackTitle)?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(fallback))
    {
      fallback = "Untitled reading";
    }

    if (string.IsNullOrWhiteSpace(text))
    {
      return fallback;
    }

    foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(30))
    {
      string line = Whitespace().Replace(rawLine, " ").Trim(' ', '\t', '-', '–', '—');
      if (!IsMeaningful(line))
      {
        continue;
      }

      string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (words.Length <= MaximumTitleWords && line.Length <= 120)
      {
        return line.TrimEnd('.', ',', ':', ';');
      }

      return $"{string.Join(' ', words.Take(8)).TrimEnd('.', ',', ':', ';')}…";
    }

    return fallback;
  }

  private static bool IsMeaningful(string line)
  {
    if (line.Length < 2 || line.Count(char.IsLetter) < 2 || PageNumber().IsMatch(line))
    {
      return false;
    }

    return !Noise().IsMatch(line);
  }

  [GeneratedRegex(@"^(?:\d{1,4}|[ivxlcdm]{1,8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex PageNumber();

  [GeneratedRegex(@"^(?:by\s+|copyright\b|©|all rights reserved\b|table of contents\b|contents\b|https?://|www\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex Noise();

  [GeneratedRegex(@"\s+")]
  private static partial Regex Whitespace();
}
