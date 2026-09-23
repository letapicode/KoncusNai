using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

internal enum ReaderWordTimingSource
{
  Native,
  ForcedAlignment,
  DeterministicEstimate,
}

/// <summary>Converts audio-aligned transcript words into the reader's display-token timeline.</summary>
internal sealed class ReaderWordTimingMap
{
  private ReaderWordTimingMap(IReadOnlyList<ReaderTimedWord> words, ReaderWordTimingSource source)
  {
    Words = words;
    Source = source;
  }

  public IReadOnlyList<ReaderTimedWord> Words { get; }

  public ReaderWordTimingSource Source { get; }

  public bool IsAudioGrounded => Source is ReaderWordTimingSource.Native or ReaderWordTimingSource.ForcedAlignment;

  public TimeSpan Duration => Words.Count == 0 ? TimeSpan.Zero : Words[^1].End;

  public static bool TryCreate(
    IReadOnlyList<string> readerWords,
    IReadOnlyList<SpeechWordTiming> alignedWords,
    TimeSpan audioDuration,
    ReaderWordTimingSource source,
    out ReaderWordTimingMap? map)
  {
    ArgumentNullException.ThrowIfNull(readerWords);
    ArgumentNullException.ThrowIfNull(alignedWords);
    map = null;
    if (readerWords.Count == 0 || alignedWords.Count == 0 || audioDuration <= TimeSpan.Zero)
    {
      return false;
    }

    string[] normalizedReaderWords = readerWords.Select(NormalizeForComparison).ToArray();
    string readerText = string.Concat(normalizedReaderWords);
    if (readerText.Length == 0)
    {
      return false;
    }

    StringBuilder alignedText = new();
    List<AlignedTextSpan> alignedSpans = [];
    int alignedScalarOffset = 0;
    foreach (SpeechWordTiming aligned in alignedWords)
    {
      string normalized = NormalizeForComparison(aligned.Text);
      if (normalized.Length == 0)
      {
        continue;
      }

      TimeSpan start = Clamp(aligned.Start, audioDuration);
      TimeSpan end = Clamp(aligned.End < aligned.Start ? aligned.Start : aligned.End, audioDuration);
      int scalarLength = CountRunes(normalized);
      alignedText.Append(normalized);
      alignedSpans.Add(new AlignedTextSpan(
        alignedScalarOffset,
        alignedScalarOffset + scalarLength,
        start,
        end));
      alignedScalarOffset += scalarLength;
    }

    if (alignedSpans.Count == 0 || !string.Equals(readerText, alignedText.ToString(), StringComparison.Ordinal))
    {
      return false;
    }

    ReaderTimedWord?[] result = new ReaderTimedWord?[readerWords.Count];
    int readerScalarOffset = 0;
    for (int index = 0; index < normalizedReaderWords.Length; index++)
    {
      string normalized = normalizedReaderWords[index];
      if (normalized.Length == 0)
      {
        continue;
      }

      int endOffset = readerScalarOffset + CountRunes(normalized);
      TimeSpan start = GetBoundaryTime(readerScalarOffset, preferPreviousSpan: false, alignedSpans);
      TimeSpan end = GetBoundaryTime(endOffset, preferPreviousSpan: true, alignedSpans);
      result[index] = new ReaderTimedWord(index, start, end < start ? start : end);
      readerScalarOffset = endOffset;
    }

    // Standalone punctuation has no acoustic label. Give it the real pause
    // between adjacent spoken spans so seeking and follow-along remain smooth.
    for (int index = 0; index < result.Length; index++)
    {
      if (result[index] is not null)
      {
        continue;
      }

      ReaderTimedWord? previous = index > 0 ? result.Take(index).LastOrDefault(item => item is not null) : null;
      ReaderTimedWord? next = result.Skip(index + 1).FirstOrDefault(item => item is not null);
      TimeSpan start = previous?.End ?? TimeSpan.Zero;
      TimeSpan end = next?.Start ?? audioDuration;
      result[index] = new ReaderTimedWord(index, start, end < start ? start : end);
    }

    if (result.Any(item => item is null))
    {
      return false;
    }

    map = new ReaderWordTimingMap(result.Select(item => item!).ToArray(), source);
    return true;
  }

  public int GetWordIndex(TimeSpan position)
  {
    if (Words.Count == 0)
    {
      return -1;
    }

    TimeSpan clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
    for (int index = 0; index < Words.Count - 1; index++)
    {
      if (clamped < Words[index].End)
      {
        return index;
      }
    }

    return Words.Count - 1;
  }

  public TimeSpan GetStart(int wordIndex) => Words[Math.Clamp(wordIndex, 0, Words.Count - 1)].Start;

  public double GetProgressForBoundary(int boundaryIndex, TimeSpan duration)
  {
    if (Words.Count == 0 || duration <= TimeSpan.Zero)
    {
      return 0d;
    }

    if (boundaryIndex <= 0)
    {
      return 0d;
    }

    if (boundaryIndex >= Words.Count)
    {
      return 1d;
    }

    return Math.Clamp(Words[boundaryIndex].Start.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d);
  }

  private static string NormalizeForComparison(string value)
  {
    StringBuilder normalized = new();
    foreach (Rune rune in value.Normalize(NormalizationForm.FormKC).EnumerateRunes())
    {
      if (Rune.IsLetterOrDigit(rune))
      {
        normalized.Append(Rune.ToLowerInvariant(rune));
      }
      else if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.SpacingCombiningMark
        or UnicodeCategory.EnclosingMark)
      {
        normalized.Append(rune);
      }
    }

    return normalized.ToString();
  }

  private static TimeSpan GetBoundaryTime(
    int scalarBoundary,
    bool preferPreviousSpan,
    IReadOnlyList<AlignedTextSpan> spans)
  {
    if (scalarBoundary <= 0)
    {
      return spans[0].AudioStart;
    }

    if (scalarBoundary >= spans[^1].ScalarEnd)
    {
      return spans[^1].AudioEnd;
    }

    foreach (AlignedTextSpan span in spans)
    {
      bool contains = preferPreviousSpan
        ? scalarBoundary > span.ScalarStart && scalarBoundary <= span.ScalarEnd
        : scalarBoundary >= span.ScalarStart && scalarBoundary < span.ScalarEnd;
      if (!contains)
      {
        continue;
      }

      double progress = (double)(scalarBoundary - span.ScalarStart)
        / Math.Max(1, span.ScalarEnd - span.ScalarStart);
      return Interpolate(span.AudioStart, span.AudioEnd, progress);
    }

    return preferPreviousSpan ? spans[^1].AudioEnd : spans[0].AudioStart;
  }

  private static TimeSpan Interpolate(TimeSpan start, TimeSpan end, double progress)
  {
    long ticks = start.Ticks + (long)((end.Ticks - start.Ticks) * Math.Clamp(progress, 0d, 1d));
    return TimeSpan.FromTicks(ticks);
  }

  private static TimeSpan Clamp(TimeSpan value, TimeSpan max) => value < TimeSpan.Zero
    ? TimeSpan.Zero
    : value > max ? max : value;

  private static int CountRunes(string value)
  {
    int count = 0;
    foreach (Rune _ in value.EnumerateRunes())
    {
      count++;
    }

    return count;
  }

  private sealed record AlignedTextSpan(
    int ScalarStart,
    int ScalarEnd,
    TimeSpan AudioStart,
    TimeSpan AudioEnd);
}

internal sealed record ReaderTimedWord(int Index, TimeSpan Start, TimeSpan End);
