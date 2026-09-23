using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Rebuilds an edited reading while preserving or detecting semantic section titles.</summary>
internal static class ReaderEditableDocumentBuilder
{
  public static ReadingDocument Create(string title, string text, ReadingDocument? baseline = null)
  {
    ArgumentNullException.ThrowIfNull(text);
    string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
    string[] paragraphs = Regex
      .Split(normalized, @"\n\s*\n+")
      .Select(value => value.Trim())
      .Where(value => value.Length > 0)
      .ToArray();
    HashSet<string> knownTitles = baseline?.Sections
      .Where(section => !string.IsNullOrWhiteSpace(section.Title))
      .Select(section => section.Title!.Trim())
      .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
    List<ReadableDocumentElement> elements = new(paragraphs.Length);
    int headings = 0;
    for (int index = 0; index < paragraphs.Length; index++)
    {
      string paragraph = paragraphs[index];
      bool isHeading = knownTitles.Contains(paragraph)
        || LooksLikeEditedHeading(paragraph, index + 1 < paragraphs.Length ? paragraphs[index + 1] : null);
      headings += isHeading ? 1 : 0;
      elements.Add(new ReadableDocumentElement(paragraph, isHeading, StartsParagraph: true));
    }

    if (headings > 0)
    {
      ReadableDocumentContent semantic = ReadableDocumentContent.FromElements(text, elements);
      if (semantic.Sections.Count > 0)
      {
        return ReadingTextLayout.Create(title, semantic.Sections);
      }
    }

    return ReadingTextLayout.Create(title, text);
  }

  private static bool LooksLikeEditedHeading(string paragraph, string? followingParagraph)
  {
    if (followingParagraph is null || paragraph.Length is < 2 or > 160)
    {
      return false;
    }

    int wordCount = paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    char final = paragraph[^1];
    bool endsSentence = final is '.' or '!' or '?' or '\u3002' or '\uFF01' or '\uFF1F' or '\u0964' or '\u0965';
    return wordCount <= 18
      && !endsSentence
      && paragraph.Any(char.IsLetterOrDigit)
      && followingParagraph.Length >= Math.Min(80, paragraph.Length * 2);
  }
}
