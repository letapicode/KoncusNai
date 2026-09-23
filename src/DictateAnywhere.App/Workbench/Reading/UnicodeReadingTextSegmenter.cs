using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>
/// Segments logical reader text without depending on WPF layout. Every token retains its exact
/// UTF-16 source span, and token boundaries are restricted to Unicode text-element boundaries.
/// </summary>
internal static class UnicodeReadingTextSegmenter
{
  private static readonly Regex ParagraphBreaks = new(@"\r?\n\s*\r?\n+", RegexOptions.Compiled);

  public static ReadingTextSegmentationResult Segment(
    string text,
    ReaderTextDirection fallbackDirection = ReaderTextDirection.LeftToRight)
  {
    ArgumentNullException.ThrowIfNull(text);
    IReadOnlyList<ReadingToken> tokens = SegmentTokens(text);
    IReadOnlyList<ReadingParagraphSpan> paragraphs = SegmentParagraphs(text, tokens, fallbackDirection);
    return new ReadingTextSegmentationResult(text, tokens, paragraphs);
  }

  public static IReadOnlyList<string> SplitParagraphText(string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    return ParagraphBreaks.Split(text);
  }

  public static ReaderTextDirection ResolveParagraphDirection(
    ReadOnlySpan<char> text,
    ReaderTextDirection fallbackDirection = ReaderTextDirection.LeftToRight)
  {
    foreach (Rune rune in text.EnumerateRunes())
    {
      UnicodeCategory category = Rune.GetUnicodeCategory(rune);
      if (category is UnicodeCategory.UppercaseLetter
          or UnicodeCategory.LowercaseLetter
          or UnicodeCategory.TitlecaseLetter
          or UnicodeCategory.ModifierLetter
          or UnicodeCategory.OtherLetter)
      {
        return IsRightToLeftScript(rune)
          ? ReaderTextDirection.RightToLeft
          : ReaderTextDirection.LeftToRight;
      }
    }

    return fallbackDirection;
  }

  public static bool ShouldInsertSpace(string previous, string current)
  {
    ArgumentException.ThrowIfNullOrEmpty(previous);
    ArgumentException.ThrowIfNullOrEmpty(current);
    Rune previousRune = previous.EnumerateRunes().Last();
    Rune currentRune = current.EnumerateRunes().First();
    return !IsCjk(previousRune)
      && !IsCjk(currentRune)
      && !IsStandalonePunctuation(currentRune);
  }

  private static IReadOnlyList<ReadingToken> SegmentTokens(string text)
  {
    int[] textElementStarts = StringInfo.ParseCombiningCharacters(text);
    List<ReadingToken> tokens = [];
    int pendingStart = -1;
    int pendingEnd = -1;
    for (int elementIndex = 0; elementIndex < textElementStarts.Length; elementIndex++)
    {
      int start = textElementStarts[elementIndex];
      int end = elementIndex + 1 < textElementStarts.Length
        ? textElementStarts[elementIndex + 1]
        : text.Length;
      ReadOnlySpan<char> element = text.AsSpan(start, end - start);
      Rune rune = Rune.TryGetRuneAt(text, start, out Rune decoded)
        ? decoded
        : Rune.ReplacementChar;

      if (IsWhitespace(element))
      {
        FlushPending();
        continue;
      }

      if (IsCjk(rune) || IsStandalonePunctuation(rune))
      {
        FlushPending();
        tokens.Add(new ReadingToken(text[start..end], start, end - start));
        continue;
      }

      pendingStart = pendingStart < 0 ? start : pendingStart;
      pendingEnd = end;
    }

    FlushPending();
    return tokens;

    void FlushPending()
    {
      if (pendingStart < 0)
      {
        return;
      }

      tokens.Add(new ReadingToken(
        text[pendingStart..pendingEnd],
        pendingStart,
        pendingEnd - pendingStart));
      pendingStart = -1;
      pendingEnd = -1;
    }
  }

  private static IReadOnlyList<ReadingParagraphSpan> SegmentParagraphs(
    string text,
    IReadOnlyList<ReadingToken> tokens,
    ReaderTextDirection fallbackDirection)
  {
    List<ReadingParagraphSpan> paragraphs = [];
    int tokenIndex = 0;
    int paragraphStart = 0;
    foreach (Match match in ParagraphBreaks.Matches(text))
    {
      AddParagraph(paragraphStart, match.Index);
      paragraphStart = match.Index + match.Length;
    }

    AddParagraph(paragraphStart, text.Length);
    return paragraphs;

    void AddParagraph(int sourceStart, int sourceEnd)
    {
      while (tokenIndex < tokens.Count && tokens[tokenIndex].SourceEnd <= sourceStart)
      {
        tokenIndex++;
      }

      int startTokenIndex = tokenIndex;
      while (tokenIndex < tokens.Count && tokens[tokenIndex].SourceStart < sourceEnd)
      {
        tokenIndex++;
      }

      int tokenCount = tokenIndex - startTokenIndex;
      if (tokenCount == 0)
      {
        return;
      }

      ReadingToken first = tokens[startTokenIndex];
      ReadingToken last = tokens[tokenIndex - 1];
      int contentStart = first.SourceStart;
      int contentEnd = last.SourceEnd;
      paragraphs.Add(new ReadingParagraphSpan(
        contentStart,
        contentEnd - contentStart,
        startTokenIndex,
        tokenCount,
        ResolveParagraphDirection(text.AsSpan(contentStart, contentEnd - contentStart), fallbackDirection)));
    }
  }

  private static bool IsWhitespace(ReadOnlySpan<char> text)
  {
    foreach (Rune rune in text.EnumerateRunes())
    {
      if (!Rune.IsWhiteSpace(rune))
      {
        return false;
      }
    }

    return true;
  }

  private static bool IsStandalonePunctuation(Rune rune) => rune.Value is
    0x3002 or 0xFF01 or 0xFF1F or 0x0964 or 0x0965;

  private static bool IsCjk(Rune rune) => rune.Value is
    >= 0x3400 and <= 0x9FFF
    or >= 0xF900 and <= 0xFAFF
    or >= 0x20000 and <= 0x2FA1F
    or >= 0x30000 and <= 0x323AF
    or >= 0x3040 and <= 0x30FF
    or >= 0x31F0 and <= 0x31FF
    or >= 0x1B000 and <= 0x1B16F
    or >= 0x1100 and <= 0x11FF
    or >= 0x3130 and <= 0x318F
    or >= 0xA960 and <= 0xA97F
    or >= 0xAC00 and <= 0xD7AF;

  private static bool IsRightToLeftScript(Rune rune) => rune.Value is
    >= 0x0590 and <= 0x05FF
    or >= 0x0600 and <= 0x06FF
    or >= 0x0750 and <= 0x077F
    or >= 0x0870 and <= 0x089F
    or >= 0x08A0 and <= 0x08FF
    or >= 0xFB1D and <= 0xFDFF
    or >= 0xFE70 and <= 0xFEFF
    or >= 0x1EE00 and <= 0x1EEFF;
}

internal sealed record ReadingToken(string Text, int SourceStart, int SourceLength)
{
  public int SourceEnd => SourceStart + SourceLength;
}

internal sealed record ReadingParagraphSpan(
  int SourceStart,
  int SourceLength,
  int StartTokenIndex,
  int TokenCount,
  ReaderTextDirection Direction)
{
  public int SourceEnd => SourceStart + SourceLength;
}

internal sealed record ReadingTextSegmentationResult(
  string Text,
  IReadOnlyList<ReadingToken> Tokens,
  IReadOnlyList<ReadingParagraphSpan> Paragraphs);
