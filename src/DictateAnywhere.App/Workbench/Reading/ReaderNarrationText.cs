using System;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Composes the exact transcript shared by speech synthesis and word alignment.</summary>
internal static class ReaderNarrationText
{
  public static string Create(ReadingSection section)
  {
    ArgumentNullException.ThrowIfNull(section);
    if (section.TitleWordCount <= 0
        || string.IsNullOrWhiteSpace(section.Title)
        || section.TitleWordCount >= section.Words.Count)
    {
      return section.Text;
    }

    string title = section.Title.Trim();
    string body = section.GetSourceTextForTokenRange(section.TitleWordCount).TrimStart();
    if (string.IsNullOrWhiteSpace(body))
    {
      return title;
    }

    // A semantic heading is displayed separately, but TTS whitespace normalization can otherwise
    // merge it into the opening sentence. A terminator gives every provider a natural title pause.
    char final = title[^1];
    string pause = final is '.' or '!' or '?' or '。' or '！' or '？' or '।' or '॥'
      ? string.Empty
      : ".";
    return $"{title}{pause}\n{body}";
  }
}
