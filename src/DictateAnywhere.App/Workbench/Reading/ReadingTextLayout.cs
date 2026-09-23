using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Shapes imported text into small, speakable reading sections while preserving paragraph rhythm.</summary>
internal static class ReadingTextLayout
{
  public const int TargetSectionCharacters = 1_100;

  private static readonly Regex Whitespace = new(@"[ \t\r\n]+", RegexOptions.Compiled);

  public static ReadingDocument Create(string title, string text, int targetSectionCharacters = TargetSectionCharacters)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      throw new ArgumentException("The document does not contain readable text.", nameof(text));
    }

    int target = Math.Clamp(targetSectionCharacters, 240, 2_500);
    string normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Untitled reading" : title.Trim();
    string[] paragraphs = UnicodeReadingTextSegmenter
      .SplitParagraphText(text.Replace("\r\n", "\n", StringComparison.Ordinal))
      .Select(NormalizeParagraph)
      .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
      .ToArray();

    List<string> sections = [];
    List<string> pendingParagraphs = [];
    int pendingLength = 0;
    foreach (string paragraph in paragraphs)
    {
      foreach (string part in SplitOversizedParagraph(paragraph, target))
      {
        int prospectiveLength = pendingLength == 0 ? part.Length : pendingLength + Environment.NewLine.Length * 2 + part.Length;
        if (pendingParagraphs.Count > 0 && prospectiveLength > target)
        {
          sections.Add(string.Join(Environment.NewLine + Environment.NewLine, pendingParagraphs));
          pendingParagraphs.Clear();
          pendingLength = 0;
        }

        pendingParagraphs.Add(part);
        pendingLength = pendingLength == 0 ? part.Length : pendingLength + Environment.NewLine.Length * 2 + part.Length;
      }
    }

    if (pendingParagraphs.Count > 0)
    {
      sections.Add(string.Join(Environment.NewLine + Environment.NewLine, pendingParagraphs));
    }

    if (sections.Count == 0)
    {
      throw new ArgumentException("The document does not contain readable text.", nameof(text));
    }

    return new ReadingDocument(
      normalizedTitle,
      sections.Select((section, index) => new ReadingSection(
        index,
        UnicodeReadingTextSegmenter.Segment(section))).ToArray());
  }

  public static ReadingDocument Create(string title, IReadOnlyList<ReadableDocumentSection> sourceSections)
  {
    ArgumentNullException.ThrowIfNull(sourceSections);
    string normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Untitled reading" : title.Trim();
    ReadingSection[] sections = sourceSections
      .Where(section => !string.IsNullOrWhiteSpace(section.FullText))
      .Select((section, index) =>
      {
        string text = section.FullText.Trim();
        ReadingTextSegmentationResult segmentation = UnicodeReadingTextSegmenter.Segment(text);
        return new ReadingSection(
          index,
          segmentation,
          section.Title.Trim(),
          UnicodeReadingTextSegmenter.Segment(section.Title).Tokens.Count);
      })
      .ToArray();
    if (sections.Length == 0)
    {
      throw new ArgumentException("The document does not contain readable sections.", nameof(sourceSections));
    }

    return new ReadingDocument(normalizedTitle, sections);
  }

  private static string NormalizeParagraph(string paragraph) => Whitespace.Replace(paragraph, " ").Trim();

  private static IEnumerable<string> SplitOversizedParagraph(string paragraph, int target)
  {
    if (paragraph.Length <= target)
    {
      yield return paragraph;
      yield break;
    }

    List<string> pendingSentences = [];
    int pendingLength = 0;
    foreach (string sentence in SplitSentences(paragraph))
    {
      if (sentence.Length > target)
      {
        if (pendingSentences.Count > 0)
        {
          yield return string.Join(" ", pendingSentences);
          pendingSentences.Clear();
          pendingLength = 0;
        }

        foreach (string wordsChunk in SplitAtWordBoundary(sentence, target))
        {
          yield return wordsChunk;
        }

        continue;
      }

      int prospectiveLength = pendingLength == 0 ? sentence.Length : pendingLength + 1 + sentence.Length;
      if (pendingSentences.Count > 0 && prospectiveLength > target)
      {
        yield return string.Join(" ", pendingSentences);
        pendingSentences.Clear();
        pendingLength = 0;
      }

      pendingSentences.Add(sentence);
      pendingLength = pendingLength == 0 ? sentence.Length : pendingLength + 1 + sentence.Length;
    }

    if (pendingSentences.Count > 0)
    {
      yield return string.Join(" ", pendingSentences);
    }
  }

  private static IEnumerable<string> SplitSentences(string paragraph)
  {
    var section = new ReadingSection(0, UnicodeReadingTextSegmenter.Segment(paragraph));
    for (int start = 0; start < section.Words.Count;)
    {
      int end = section.SentenceRanges[start].End;
      yield return section.GetSourceTextForTokenRange(start, end + 1);
      start = end + 1;
    }
  }

  private static IEnumerable<string> SplitAtWordBoundary(string text, int target)
  {
    var tokens = UnicodeReadingTextSegmenter.Segment(text).Tokens;
    int start = 0;
    for (int index = 1; index < tokens.Count; index++)
    {
      if (tokens[index].SourceEnd - tokens[start].SourceStart <= target) continue;
      // Do not split explicitly nonbreaking source separators at a section boundary.
      string separator = text[tokens[index - 1].SourceEnd..tokens[index].SourceStart];
      if (separator.Contains('\u00a0') || separator.Contains('\u202f') || separator.Contains('\u2060')) continue;
      yield return text[tokens[start].SourceStart..tokens[index - 1].SourceEnd];
      start = index;
    }
    if (start < tokens.Count) yield return text[tokens[start].SourceStart..tokens[^1].SourceEnd];
  }

  internal static string JoinWords(IReadOnlyList<string> words, int start = 0, int? endExclusive = null, IReadOnlyList<int>? paragraphStarts = null)
  {
    ArgumentNullException.ThrowIfNull(words);
    int end = Math.Clamp(endExclusive ?? words.Count, 0, words.Count);
    int first = Math.Clamp(start, 0, end);
    StringBuilder text = new();
    for (int index = first; index < end; index++)
    {
      if (index > first && paragraphStarts?.Contains(index) == true)
      {
        text.AppendLine();
        text.AppendLine();
      }
      else if (index > first && UnicodeReadingTextSegmenter.ShouldInsertSpace(words[index - 1], words[index]))
      {
        text.Append(' ');
      }

      text.Append(words[index]);
    }

    return text.ToString();
  }

  internal static bool ShouldInsertSpace(string previous, string current)
    => UnicodeReadingTextSegmenter.ShouldInsertSpace(previous, current);
}

internal enum ReadingHighlightMode
{
  Off,
  Sentence,
  Word,
}

internal sealed record ReaderHighlightOption(string DisplayName, ReadingHighlightMode Mode)
{
  public static IReadOnlyList<ReaderHighlightOption> Defaults { get; } =
  [
    new("Off", ReadingHighlightMode.Off),
    new("Sentence", ReadingHighlightMode.Sentence),
    new("Word", ReadingHighlightMode.Word),
  ];

  public override string ToString() => DisplayName;
}

internal enum ReaderViewMode
{
  FullPage,
  OneSentence,
  OneWord,
}

internal sealed record ReaderViewOption(string DisplayName, ReaderViewMode Mode, string Description)
{
  public static IReadOnlyList<ReaderViewOption> Defaults { get; } =
  [
    new("Full Page", ReaderViewMode.FullPage, "Read in the complete book layout."),
    new("One Sentence", ReaderViewMode.OneSentence, "Keep only the current sentence in view."),
    new("One Word", ReaderViewMode.OneWord, "Show the current spoken word at the center."),
  ];

  public override string ToString() => DisplayName;
}

internal sealed record ReaderFollowAlongOption(
  string DisplayName,
  ReadingHighlightMode HighlightMode,
  ReaderViewMode ViewMode,
  string Description)
{
  public static IReadOnlyList<ReaderFollowAlongOption> Defaults { get; } =
  [
    new("Off", ReadingHighlightMode.Off, ReaderViewMode.FullPage, "Show the page without synchronized emphasis."),
    new("Word on Page", ReadingHighlightMode.Word, ReaderViewMode.FullPage, "Follow each spoken word in the complete page."),
    new("Sentence on Page", ReadingHighlightMode.Sentence, ReaderViewMode.FullPage, "Follow the current sentence in the complete page."),
    new("Focused Sentence", ReadingHighlightMode.Sentence, ReaderViewMode.OneSentence, "Keep the current sentence centered and easy to track."),
    new("Focused Word", ReadingHighlightMode.Word, ReaderViewMode.OneWord, "Keep the current spoken word centered."),
  ];

  public override string ToString() => DisplayName;
}

internal enum ReaderHighlightVisualStyle
{
  ReaderPage,
  FocusType,
  Underline,
  AccentFill,
  BoldFocus,
  FocusRing,
  Spotlight,
}

internal sealed record ReaderHighlightVisualOption(string DisplayName, ReaderHighlightVisualStyle Style, string Description)
{
  public static IReadOnlyList<ReaderHighlightVisualOption> Defaults { get; } =
  [
    new("Reader Page", ReaderHighlightVisualStyle.ReaderPage, "A complete page with a quiet, synchronized reading guide."),
    new("Focus Type", ReaderHighlightVisualStyle.FocusType, "Uses accent color without changing the line layout."),
    new("Soft Underline", ReaderHighlightVisualStyle.Underline, "A quiet reading guide with no filled box."),
    new("Focus Fill", ReaderHighlightVisualStyle.AccentFill, "A rounded high-contrast surface behind the whole word."),
    new("Kinetic Bold", ReaderHighlightVisualStyle.BoldFocus, "Boldens and colors the spoken word without moving the surrounding text."),
    new("Focus Ring · Accessible", ReaderHighlightVisualStyle.FocusRing, "A color-independent outline designed for clear recognition."),
    new("Spotlight · Accessible", ReaderHighlightVisualStyle.Spotlight, "Dims surrounding words and underlines the active word."),
  ];

  public override string ToString() => DisplayName;
}

internal sealed record ReaderHighlightColorOption(string DisplayName, string HexColor)
{
  public static IReadOnlyList<ReaderHighlightColorOption> Defaults { get; } =
  [
    new("Warm Gold", "#D4A94F"),
    new("Clear Sky", "#4D9FFF"),
    new("Violet", "#A88BF4"),
    new("Emerald", "#3FC18A"),
    new("Coral", "#F0785A"),
    new("Rose", "#D94F91"),
    new("Aqua", "#32B9C7"),
    new("Silver", "#AEB9CC"),
  ];

  public override string ToString() => DisplayName;
}

/// <summary>Maps local WAV time to a readable word index when a provider has no native word timestamps.</summary>
internal static class ReadingPlaybackTiming
{
  public static int GetWordIndex(TimeSpan position, TimeSpan duration, IReadOnlyList<string> words)
  {
    ArgumentNullException.ThrowIfNull(words);
    if (words.Count == 0 || duration <= TimeSpan.Zero)
    {
      return -1;
    }

    double target = Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0d, 0.999999d)
      * words.Sum(GetWordWeight);
    double consumed = 0d;
    for (int index = 0; index < words.Count; index++)
    {
      consumed += GetWordWeight(words[index]);
      if (target < consumed)
      {
        return index;
      }
    }

    return words.Count - 1;
  }

  public static TimeSpan GetPositionForWordIndex(TimeSpan duration, IReadOnlyList<string> words, int wordIndex)
  {
    ArgumentNullException.ThrowIfNull(words);
    if (words.Count == 0 || duration <= TimeSpan.Zero)
    {
      return TimeSpan.Zero;
    }

    double progress = GetProgressForWordBoundary(words, Math.Clamp(wordIndex, 0, words.Count));
    long boundaryTicks = Math.Min(duration.Ticks, (long)Math.Ceiling(duration.Ticks * progress) + TimeSpan.TicksPerMillisecond);
    return TimeSpan.FromTicks(boundaryTicks);
  }

  public static double GetProgressForWordBoundary(IReadOnlyList<string> words, int boundaryIndex)
  {
    ArgumentNullException.ThrowIfNull(words);
    if (words.Count == 0)
    {
      return 0d;
    }

    double total = words.Sum(GetWordWeight);
    double consumed = words.Take(Math.Clamp(boundaryIndex, 0, words.Count)).Sum(GetWordWeight);
    return total <= 0d ? 0d : Math.Clamp(consumed / total, 0d, 1d);
  }

  public static int GetWordIndex(TimeSpan position, TimeSpan duration, int wordCount)
  {
    if (wordCount <= 0 || duration <= TimeSpan.Zero)
    {
      return -1;
    }

    double ratio = Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0d, 0.999999d);
    return Math.Clamp((int)Math.Floor(ratio * wordCount), 0, wordCount - 1);
  }

  public static (int Start, int End) GetSentenceRange(IReadOnlyList<string> words, int wordIndex)
  {
    ArgumentNullException.ThrowIfNull(words);
    if (words.Count == 0 || wordIndex < 0)
    {
      return (-1, -1);
    }

    int requested = Math.Clamp(wordIndex, 0, words.Count - 1);
    int start = 0;
    for (int index = 0; index < words.Count; index++)
    {
      if (!ContainsSentenceTerminator(words[index]))
      {
        continue;
      }

      int end = index;
      while (end + 1 < words.Count && IsClosingSentenceToken(words[end + 1]))
      {
        end++;
      }

      if (requested <= end)
      {
        return (start, end);
      }

      start = end + 1;
      index = end;
    }

    return (Math.Min(start, words.Count - 1), words.Count - 1);
  }

  private static double GetWordWeight(string word)
  {
    int letterOrDigitCount = word.Count(char.IsLetterOrDigit);
    double weight = 1d + Math.Max(0, letterOrDigitCount - 1) * 0.18d;
    if (word.IndexOfAny([',', ':', ';']) >= 0)
    {
      weight += 0.35d;
    }

    if (word.IndexOfAny(['.', '!', '?', '…', '。', '！', '？', '।', '॥', '۔', '؟']) >= 0)
    {
      weight += 1.1d;
    }

    return weight;
  }

  private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
  {
    "Mr.", "Mrs.", "Ms.", "Dr.", "Prof.", "Sr.", "Jr.", "St.", "e.g.", "i.e.",
    "Mme.", "Mlle.", "M.", "Srta.", "Dra.", "Sra.", "Sig.", "Sig.ra.", "p.ex.",
  };

  private static bool ContainsSentenceTerminator(string word)
  {
    string terminal = word.TrimEnd('"', '\'', '”', '’', '»', '›', ')', ']', '}', '」', '』', '】');
    if (terminal.Length == 0) return false;
    char last = terminal[^1];
    if (last is '!' or '?' or '…' or '。' or '！' or '？' or '।' or '॥' or '۔' or '؟') return true;
    if (last != '.' || Abbreviations.Contains(terminal)) return false;
    if (terminal.EndsWith("...", StringComparison.Ordinal)) return true;
    string stem = terminal[..^1];
    // Initials and dotted initialisms are not sentence endings on their own.
    if (stem.EnumerateRunes().Count() == 1 && stem.EnumerateRunes().All(Rune.IsLetter)) return false;
    string[] initials = stem.Split('.');
    return !(initials.Length > 1 && initials.All(part => part.EnumerateRunes().Count() == 1
      && part.EnumerateRunes().All(Rune.IsLetter)));
  }

  public static (int Start, int End) GetSentenceRange(ReadingSection section, int wordIndex)
  {
    ArgumentNullException.ThrowIfNull(section);
    if (wordIndex < 0 || section.Words.Count == 0) return (-1, -1);
    int index = Math.Min(wordIndex, section.Words.Count - 1);
    return section.SentenceRanges[index];
  }

  internal static (int Start, int End)[] BuildSentenceRanges(ReadingSection section)
  {
    var result = new (int Start, int End)[section.Words.Count];
    int[] boundaries = section.ParagraphStartWordIndices.Append(0).Append(section.TitleWordCount)
      .Append(section.Words.Count).Distinct().OrderBy(value => value).ToArray();
    for (int group = 0; group + 1 < boundaries.Length; group++)
    {
      int start = boundaries[group];
      int limit = boundaries[group + 1];
      for (int index = start; index < limit; index++)
      {
        if (!ContainsSentenceTerminator(section.Words[index]) && index < limit - 1) continue;
        int end = index;
        while (end + 1 < limit && IsClosingSentenceToken(section.Words[end + 1])) end++;
        for (int token = start; token <= end; token++) result[token] = (start, end);
        start = end + 1;
        index = end;
      }
    }
    return result;
  }

  private static bool IsClosingSentenceToken(string word) => word.Length > 0
    && word.All(character => character is '\"' or '\'' or '”' or '’' or '»' or '›' or ')' or ']' or '}' or '」' or '』' or '】');

  public static double GetDocumentProgress(ReadingDocument document, int sectionIndex, TimeSpan position, TimeSpan duration)
  {
    ArgumentNullException.ThrowIfNull(document);
    if (document.Sections.Count == 0)
    {
      return 0;
    }

    int completeWords = document.Sections.Take(Math.Clamp(sectionIndex, 0, document.Sections.Count)).Sum(section => section.Words.Count);
    double currentProgress = sectionIndex >= 0 && sectionIndex < document.Sections.Count && duration > TimeSpan.Zero
      ? Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d) * document.Sections[sectionIndex].Words.Count
      : 0d;
    return Math.Clamp((completeWords + currentProgress) / Math.Max(1, document.TotalWordCount), 0d, 1d);
  }
}

internal sealed record ReaderThemeOption(
  string DisplayName,
  string ShellColor,
  string SidebarColor,
  string FooterColor,
  string PageColor,
  string FrameColor,
  string InkColor,
  string WordHighlightColor,
  string SentenceHighlightColor)
{
  public static IReadOnlyList<ReaderThemeOption> Defaults { get; } =
  [
    new("Warm Paper", "#080D18", "#0D1525", "#111A2C", "#F9F2E8", "#1B2840", "#182237", "#F6CA67", "#F6E7BE"),
    new("Midnight", "#070B14", "#0C1423", "#111B2D", "#111A29", "#2B3D5B", "#F3F5F8", "#E9BC59", "#624A20"),
    new("Abyss Black", "#020304", "#040506", "#05070A", "#030405", "#14171D", "#F4F1EA", "#F2C15B", "#59441E"),
    new("Quiet Sage", "#101714", "#17251F", "#1C2B25", "#F5F3EA", "#496558", "#1E3027", "#E5C869", "#E9E4BA"),
  ];

  public override string ToString() => DisplayName;
}

internal sealed record ReaderPreparationOption(string DisplayName, bool PrepareEntireRange, string Description)
{
  public static IReadOnlyList<ReaderPreparationOption> Defaults { get; } =
  [
    new("Start Sooner", false, "Prepare this section with word timing. The next one is quietly prepared while you listen."),
    new("Prepare Entire Selection", true, "Create audio and word timing for every selected section before you begin."),
  ];

  public override string ToString() => DisplayName;
}

internal enum ReaderVideoFormat
{
  Landscape,
  YouTubeShort,
}

internal sealed record ReaderVideoFormatOption(string DisplayName, ReaderVideoFormat Format, string Description)
{
  public static IReadOnlyList<ReaderVideoFormatOption> Defaults { get; } =
  [
    new("Reading Video · 16:9", ReaderVideoFormat.Landscape, "A spacious, page-based 2560 × 1440 reading video."),
    new("YouTube Short · 9:16", ReaderVideoFormat.YouTubeShort, "A vertical 1080 × 1920 video with large, safe-area captions."),
  ];

  public override string ToString() => DisplayName;
}

internal enum ReaderVideoCaptionStyle
{
  ReaderPage,
  KineticBold,
  FocusPill,
}

internal sealed record ReaderVideoCaptionStyleOption(string DisplayName, ReaderVideoCaptionStyle Style, string Description)
{
  public static IReadOnlyList<ReaderVideoCaptionStyleOption> Defaults { get; } =
  [
    new("Reader Page", ReaderVideoCaptionStyle.ReaderPage, "Full-page synchronized reading."),
    new("Kinetic Bold", ReaderVideoCaptionStyle.KineticBold, "Large phrase captions with the spoken word bold and accented."),
    new("Focus Pill", ReaderVideoCaptionStyle.FocusPill, "Large phrase captions with a modern rounded focus capsule."),
  ];

  public override string ToString() => DisplayName;
}
