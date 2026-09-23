using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.App.Workbench.Reading;

internal sealed record ReadingDocument(string Title, IReadOnlyList<ReadingSection> Sections)
{
  public int TotalWordCount => Sections.Sum(section => section.Words.Count);
}

internal sealed record ReadingSection
{
  public ReadingSection(
    int index,
    ReadingTextSegmentationResult segmentation,
    string? title = null,
    int titleWordCount = 0)
  {
    ArgumentNullException.ThrowIfNull(segmentation);
    if (titleWordCount < 0 || titleWordCount > segmentation.Tokens.Count)
    {
      throw new ArgumentOutOfRangeException(nameof(titleWordCount));
    }

    Index = index;
    Text = segmentation.Text;
    Tokens = segmentation.Tokens;
    Paragraphs = segmentation.Paragraphs;
    Words = Tokens.Select(token => token.Text).ToArray();
    ParagraphStartWordIndices = Paragraphs
      .Skip(1)
      .Select(paragraph => paragraph.StartTokenIndex)
      .ToArray();
    Title = title;
    TitleWordCount = titleWordCount;
    SentenceRanges = ReadingPlaybackTiming.BuildSentenceRanges(this);
  }

  internal (int Start, int End)[] SentenceRanges { get; }

  public int Index { get; }

  public string Text { get; }

  public IReadOnlyList<string> Words { get; }

  public IReadOnlyList<int> ParagraphStartWordIndices { get; }

  public string? Title { get; }

  public int TitleWordCount { get; }

  public IReadOnlyList<ReadingToken> Tokens { get; }

  public IReadOnlyList<ReadingParagraphSpan> Paragraphs { get; }

  internal string GetSeparatorAfter(int index) => index >= 0 && index + 1 < Tokens.Count
    ? Text[Tokens[index].SourceEnd..Tokens[index + 1].SourceStart] : string.Empty;

  public string GetSourceTextForTokenRange(int start, int? endExclusive = null)
  {
    int end = Math.Clamp(endExclusive ?? Tokens.Count, 0, Tokens.Count);
    int first = Math.Clamp(start, 0, end);
    if (first == end)
    {
      return string.Empty;
    }

    int sourceStart = Tokens[first].SourceStart;
    int sourceEnd = Tokens[end - 1].SourceEnd;
    return Text[sourceStart..sourceEnd];
  }

  public IReadOnlyList<ReaderTextDirection> GetParagraphDirections(ReaderTextDirection fallbackDirection)
  {
    return Paragraphs
      .Select(paragraph => UnicodeReadingTextSegmenter.ResolveParagraphDirection(
        Text.AsSpan(paragraph.SourceStart, paragraph.SourceLength),
        fallbackDirection))
      .ToArray();
  }

  public ReaderTextDirection GetDirectionForWord(int wordIndex, ReaderTextDirection fallbackDirection)
  {
    int index = Math.Clamp(wordIndex, 0, Math.Max(0, Words.Count - 1));
    ReadingParagraphSpan? paragraph = Paragraphs.FirstOrDefault(candidate =>
      index >= candidate.StartTokenIndex
      && index < candidate.StartTokenIndex + candidate.TokenCount);
    return paragraph is null
      ? fallbackDirection
      : UnicodeReadingTextSegmenter.ResolveParagraphDirection(
        Text.AsSpan(paragraph.SourceStart, paragraph.SourceLength),
        fallbackDirection);
  }
}
