using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

internal enum ReaderPreparationKind
{
  CurrentSection,
  SelectedRange,
}

internal enum ReaderPreparationPhase
{
  Idle,
  WaitingForOperation,
  PreparingNarration,
  PreparingTiming,
  OpeningAudio,
  Ready,
  Canceled,
  Failed,
}

internal enum ReaderPreparationOutcome
{
  Succeeded,
  Busy,
  Canceled,
  Failed,
  Stale,
}

internal enum ReaderPreparationStatus
{
  None,
  PreparingSectionNarration,
  PreparingSectionTiming,
  OpeningSectionAudio,
  PreparingRangeNarration,
  PreparingRangeTiming,
  OpeningPreparedRange,
  SectionReady,
  RangeReady,
  Canceled,
  SectionFailed,
  RangeFailed,
}

[Flags]
internal enum ReaderPreparationDirective
{
  None = 0,
  StopVoicePreview = 1,
  ResetPlaybackView = 2,
  PlayWhenReady = 4,
  NotifyCompletion = 8,
}

internal sealed record ReaderPreparationRequest
{
  public ReaderPreparationRequest(
    ReaderPreparationKind kind,
    IReadOnlyList<ReadingSection> selectedSections,
    int currentSectionOffset,
    ReaderNarrationProfile narrationProfile,
    bool playWhenReady)
  {
    ArgumentNullException.ThrowIfNull(selectedSections);
    if (selectedSections.Count == 0)
    {
      throw new ArgumentException("A preparation request requires at least one section.", nameof(selectedSections));
    }

    if (currentSectionOffset < 0 || currentSectionOffset >= selectedSections.Count)
    {
      throw new ArgumentOutOfRangeException(nameof(currentSectionOffset));
    }

    Kind = kind;
    SelectedSections = selectedSections.ToArray();
    CurrentSectionOffset = currentSectionOffset;
    NarrationProfile = narrationProfile ?? throw new ArgumentNullException(nameof(narrationProfile));
    PlayWhenReady = playWhenReady;
  }

  public ReaderPreparationKind Kind { get; }

  public IReadOnlyList<ReadingSection> SelectedSections { get; }

  public int CurrentSectionOffset { get; }

  public ReaderNarrationProfile NarrationProfile { get; }

  public bool PlayWhenReady { get; }

  public ReadingSection CurrentSection => SelectedSections[CurrentSectionOffset];
}

internal sealed record ReaderPreparationState(
  ReaderPreparationPhase Phase,
  ReaderPreparationStatus Status,
  ReaderPreparationKind? Kind,
  int CompletedSections,
  int TotalSections,
  double ProgressStart,
  double ProgressEnd,
  bool IsCached,
  DateTimeOffset PhaseStartedAt,
  TimeSpan EstimatedPhaseDuration,
  ReaderPreparationDirective Directives)
{
  public static ReaderPreparationState Idle(DateTimeOffset now) => new(
    ReaderPreparationPhase.Idle,
    ReaderPreparationStatus.None,
    null,
    0,
    0,
    0d,
    0d,
    false,
    now,
    TimeSpan.Zero,
    ReaderPreparationDirective.None);

  public double GetNormalizedProgress(DateTimeOffset now)
  {
    if (ProgressEnd <= ProgressStart || EstimatedPhaseDuration <= TimeSpan.Zero)
    {
      return Math.Clamp(ProgressStart, 0d, 1d);
    }

    double elapsedFraction = Math.Clamp(
      (now - PhaseStartedAt).TotalMilliseconds / EstimatedPhaseDuration.TotalMilliseconds,
      0d,
      0.96d);
    double eased = 1d - Math.Pow(1d - elapsedFraction, 1.35d);
    return Math.Clamp(ProgressStart + ((ProgressEnd - ProgressStart) * eased), 0d, 1d);
  }
}

internal sealed record ReaderPreparationResult(
  ReaderPreparationOutcome Outcome,
  ReaderPreparationStatus Status,
  ReaderPreparationDirective Directives,
  TextToSpeechResult? Speech = null,
  ReaderWordTimingMap? Timing = null,
  int PreparedSectionCount = 0);
