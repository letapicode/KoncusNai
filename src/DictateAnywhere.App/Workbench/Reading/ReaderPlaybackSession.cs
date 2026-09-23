using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Owns the selected reading range and the prepared state of its current section.</summary>
internal sealed class ReaderPlaybackSession
{
  private static readonly TimeSpan ReplayThreshold = TimeSpan.FromMilliseconds(120);
  private int sectionCount;

  public ReaderPlaybackSession(int sectionCount)
  {
    ResetDocument(sectionCount);
  }

  public int CurrentSectionIndex { get; private set; }

  public int RangeStartIndex { get; private set; }

  public int RangeEndIndex { get; private set; }

  public int SelectedSectionCount => RangeEndIndex - RangeStartIndex + 1;

  public bool HasActivatedRange { get; private set; }

  public bool HasPreparedSection => CurrentWordTimingMap is not null;

  public bool HasPlaybackEnded { get; private set; }

  public int HighlightedWordIndex { get; private set; } = -1;

  public TimeSpan SectionDuration { get; private set; }

  public ReaderWordTimingMap? CurrentWordTimingMap { get; private set; }

  public bool CanMovePrevious => HasActivatedRange && CurrentSectionIndex > RangeStartIndex;

  public bool CanMoveNext => HasActivatedRange && CurrentSectionIndex < RangeEndIndex;

  public void ResetDocument(int newSectionCount)
  {
    if (newSectionCount <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(newSectionCount), "A reading document must contain at least one section.");
    }

    sectionCount = newSectionCount;
    RangeStartIndex = 0;
    RangeEndIndex = 0;
    CurrentSectionIndex = 0;
    HasActivatedRange = false;
    ResetPreparedSection();
  }

  public void SelectRange(int startIndex, int endIndex)
  {
    ValidateRange(startIndex, endIndex);
    RangeStartIndex = startIndex;
    RangeEndIndex = endIndex;
    CurrentSectionIndex = startIndex;
    HasActivatedRange = false;
    ResetPreparedSection();
  }

  public void ActivateSelection()
  {
    CurrentSectionIndex = RangeStartIndex;
    HasActivatedRange = true;
    ResetPreparedSection();
  }

  public void BeginSectionPreparation()
  {
    ResetPreparedSection();
  }

  public void SetSectionDuration(TimeSpan duration)
  {
    if (duration < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(duration));
    }

    SectionDuration = duration;
    HasPlaybackEnded = false;
  }

  public void CompleteSectionPreparation(ReaderWordTimingMap timingMap)
  {
    ArgumentNullException.ThrowIfNull(timingMap);
    CurrentWordTimingMap = timingMap;
  }

  public bool NeedsReplayReset(TimeSpan position) => HasPlaybackEnded
    || SectionDuration > TimeSpan.Zero && position >= SectionDuration - ReplayThreshold;

  public void RestartFromBeginning()
  {
    HighlightedWordIndex = -1;
    HasPlaybackEnded = false;
  }

  public void MarkPaused()
  {
    HasPlaybackEnded = false;
  }

  public bool TryMovePrevious()
  {
    if (!CanMovePrevious)
    {
      return false;
    }

    MoveToSection(CurrentSectionIndex - 1);
    return true;
  }

  public bool TryMoveNext()
  {
    if (!CanMoveNext)
    {
      return false;
    }

    MoveToSection(CurrentSectionIndex + 1);
    return true;
  }

  public ReaderPlaybackCompletion CompleteCurrentSection(int wordCount)
  {
    if (!HasActivatedRange)
    {
      throw new InvalidOperationException("A reading range must be activated before playback can complete.");
    }

    if (wordCount < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(wordCount));
    }

    if (TryMoveNext())
    {
      return ReaderPlaybackCompletion.AdvancedToNextSection;
    }

    HighlightedWordIndex = wordCount - 1;
    HasPlaybackEnded = true;
    return ReaderPlaybackCompletion.CompletedRange;
  }

  public bool UpdateHighlight(TimeSpan position)
  {
    if (CurrentWordTimingMap is null)
    {
      return false;
    }

    int nextWordIndex = CurrentWordTimingMap.GetWordIndex(position);
    if (nextWordIndex == HighlightedWordIndex)
    {
      return false;
    }

    HighlightedWordIndex = nextWordIndex;
    return true;
  }

  public TimeSpan Seek(TimeSpan position)
  {
    TimeSpan clamped = position < TimeSpan.Zero
      ? TimeSpan.Zero
      : position > SectionDuration ? SectionDuration : position;
    if (clamped < SectionDuration - ReplayThreshold)
    {
      HasPlaybackEnded = false;
    }

    HighlightedWordIndex = CurrentWordTimingMap?.GetWordIndex(clamped) ?? -1;
    return clamped;
  }

  public void InvalidatePreparation()
  {
    HasActivatedRange = false;
    ResetPreparedSection();
  }

  public IReadOnlyList<ReadingSection> GetSelectedSections(ReadingDocument document)
  {
    ArgumentNullException.ThrowIfNull(document);
    if (document.Sections.Count != sectionCount)
    {
      throw new InvalidOperationException("The reading document changed without resetting the playback session.");
    }

    return document.Sections
      .Skip(RangeStartIndex)
      .Take(SelectedSectionCount)
      .ToArray();
  }

  private void MoveToSection(int index)
  {
    CurrentSectionIndex = index;
    ResetPreparedSection();
  }

  private void ResetPreparedSection()
  {
    CurrentWordTimingMap = null;
    HighlightedWordIndex = -1;
    SectionDuration = TimeSpan.Zero;
    HasPlaybackEnded = false;
  }

  private void ValidateRange(int startIndex, int endIndex)
  {
    if (startIndex < 0 || startIndex >= sectionCount)
    {
      throw new ArgumentOutOfRangeException(nameof(startIndex));
    }

    if (endIndex < startIndex || endIndex >= sectionCount)
    {
      throw new ArgumentOutOfRangeException(nameof(endIndex));
    }
  }
}

internal enum ReaderPlaybackCompletion
{
  AdvancedToNextSection,
  CompletedRange,
}
