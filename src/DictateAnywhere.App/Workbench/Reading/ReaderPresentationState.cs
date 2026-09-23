using System;
using DictateAnywhere.App.Workbench.Publishing;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Immutable, UI-independent projection of authoritative Reader workflow/session state.</summary>
internal sealed record ReaderPresentationState(
  ReaderSidebarPresentation Sidebar,
  ReaderTransportPresentation Transport,
  ReaderProgressPresentation Progress,
  ReaderDocumentSurface DocumentSurface,
  bool SidebarVisible,
  bool ChromeVisible,
  bool IsDistractionFree);

internal enum ReaderDocumentSurface
{
  Draft,
  FullPage,
  FocusedWord,
  FocusedSentence,
}

internal sealed record ReaderSidebarPresentation(
  bool CanInteract,
  bool CanSelectRange,
  bool CanPrepare,
  bool CanUsePreparedRange,
  bool CanEditAppearance,
  bool IsPreparing);

internal sealed record ReaderTransportPresentation(
  bool IsVisible,
  bool CanPrevious,
  bool CanPlay,
  bool CanNext,
  bool CanEdit,
  bool IsPlaying,
  bool IsDraft,
  string Status,
  string DocumentMetadata,
  TimeSpan Position,
  TimeSpan Duration,
  double DocumentProgress,
  int SectionNumber,
  int SectionCount);

internal sealed record ReaderImportProgress(
  string Title,
  string Detail,
  double? Progress,
  string ProgressLabel);

internal sealed record ReaderProgressPresentation(
  bool IsVisible,
  string Title,
  string Detail,
  double? Progress,
  string ProgressLabel,
  double ProgressStart,
  double ProgressEnd,
  DateTimeOffset PhaseStartedAt,
  TimeSpan EstimatedDuration,
  bool IsEstimated)
{
  public static ReaderProgressPresentation Hidden { get; } = new(
    false,
    string.Empty,
    string.Empty,
    null,
    string.Empty,
    0d,
    0d,
    DateTimeOffset.MinValue,
    TimeSpan.Zero,
    false);

  public double? GetProgress(DateTimeOffset now)
  {
    if (!IsVisible)
    {
      return null;
    }
    if (!IsEstimated || EstimatedDuration <= TimeSpan.Zero || ProgressEnd <= ProgressStart)
    {
      return Progress;
    }
    double elapsedFraction = Math.Clamp(
      (now - PhaseStartedAt).TotalMilliseconds / EstimatedDuration.TotalMilliseconds,
      0d,
      0.96d);
    double eased = 1d - Math.Pow(1d - elapsedFraction, 1.35d);
    return Math.Clamp(ProgressStart + ((ProgressEnd - ProgressStart) * eased), 0d, 1d);
  }
}

internal sealed record ReaderPresentationSnapshot(
  ReaderWorkspaceMode WorkspaceMode,
  ReaderOperationKind? ActiveOperation,
  bool HasDraftChanges,
  bool HasDraftText,
  bool HasPreparedSection,
  bool HasActivatedRange,
  bool CanMovePrevious,
  bool CanMoveNext,
  bool IsPlaying,
  bool IsDistractionFree,
  bool IsSidebarCollapsed,
  string IdleStatus,
  string DocumentMetadata,
  int CurrentSectionIndex,
  int SectionCount,
  TimeSpan Position,
  TimeSpan Duration,
  double DocumentProgress,
  ReaderPreparationState Preparation,
  ReaderExportState Export,
  ReaderPublishingState Publishing,
  ReaderImportProgress? Import,
  ReaderViewMode ViewMode = ReaderViewMode.FullPage);

internal static class ReaderPresentationReducer
{
  public static ReaderPresentationState Reduce(ReaderPresentationSnapshot snapshot, DateTimeOffset now)
  {
    ArgumentNullException.ThrowIfNull(snapshot);
    bool draft = snapshot.WorkspaceMode == ReaderWorkspaceMode.Draft;
    bool busy = snapshot.ActiveOperation.HasValue;
    bool preparing = snapshot.ActiveOperation is ReaderOperationKind.SectionPreparation
      or ReaderOperationKind.RangePreparation
      or ReaderOperationKind.DocumentImport;
    bool canUsePreparedReading = !draft || !snapshot.HasDraftChanges;
    ReaderProgressPresentation progress = ResolveProgress(snapshot, now);
    string status = progress.IsVisible && !string.IsNullOrWhiteSpace(progress.Detail)
      ? progress.Detail
      : snapshot.IdleStatus;

    return new ReaderPresentationState(
      new ReaderSidebarPresentation(
        CanInteract: !busy,
        CanSelectRange: !busy && !draft,
        CanPrepare: !busy && (!draft || snapshot.HasDraftText),
        CanUsePreparedRange: !busy && canUsePreparedReading && snapshot.HasActivatedRange,
        CanEditAppearance: !busy,
        IsPreparing: preparing),
      new ReaderTransportPresentation(
        IsVisible: !draft || !snapshot.HasDraftChanges && snapshot.HasPreparedSection,
        CanPrevious: !busy && canUsePreparedReading && snapshot.CanMovePrevious,
        CanPlay: !busy && canUsePreparedReading && snapshot.HasPreparedSection,
        CanNext: !busy && canUsePreparedReading && snapshot.CanMoveNext,
        CanEdit: !busy && !draft,
        IsPlaying: snapshot.IsPlaying,
        IsDraft: draft,
        Status: status,
        DocumentMetadata: snapshot.DocumentMetadata,
        Position: snapshot.Position,
        Duration: snapshot.Duration,
        DocumentProgress: Math.Clamp(snapshot.DocumentProgress, 0d, 1d),
        SectionNumber: snapshot.SectionCount == 0 ? 0 : snapshot.CurrentSectionIndex + 1,
        SectionCount: snapshot.SectionCount),
      progress,
      draft ? ReaderDocumentSurface.Draft : snapshot.ViewMode switch
      {
        ReaderViewMode.FullPage => ReaderDocumentSurface.FullPage,
        ReaderViewMode.OneWord => ReaderDocumentSurface.FocusedWord,
        ReaderViewMode.OneSentence => ReaderDocumentSurface.FocusedSentence,
        _ => throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.ViewMode, "Unknown Reader view mode."),
      },
      SidebarVisible: !snapshot.IsDistractionFree && !snapshot.IsSidebarCollapsed,
      ChromeVisible: !snapshot.IsDistractionFree,
      snapshot.IsDistractionFree);
  }

  private static ReaderProgressPresentation ResolveProgress(ReaderPresentationSnapshot snapshot, DateTimeOffset now)
  {
    return snapshot.ActiveOperation switch
    {
      ReaderOperationKind.DocumentImport when snapshot.Import is not null => Fixed(
        snapshot.Import.Title,
        snapshot.Import.Detail,
        snapshot.Import.Progress,
        snapshot.Import.ProgressLabel),
      ReaderOperationKind.SectionPreparation or ReaderOperationKind.RangePreparation => FromPreparation(snapshot.Preparation, now),
      ReaderOperationKind.AudioExport or ReaderOperationKind.VideoExport => FromExport(snapshot.Export),
      ReaderOperationKind.YouTubePublish => FromPublishing(snapshot.Publishing),
      _ => ReaderProgressPresentation.Hidden,
    };
  }

  private static ReaderProgressPresentation FromPreparation(ReaderPreparationState state, DateTimeOffset now)
  {
    (string title, string detail) = state.Status switch
    {
      ReaderPreparationStatus.PreparingSectionNarration => ("Preparing your audiobook", "Creating natural narration for this section."),
      ReaderPreparationStatus.PreparingSectionTiming => ("Matching every word", "Matching the finished narration to the displayed text."),
      ReaderPreparationStatus.OpeningSectionAudio => ("Opening your audiobook", "Opening the prepared narration for playback."),
      ReaderPreparationStatus.PreparingRangeNarration => ("Preparing your audiobook", "Creating narration for the selected reading range."),
      ReaderPreparationStatus.PreparingRangeTiming => ("Preparing your audiobook", "Creating word timing for the selected reading range."),
      ReaderPreparationStatus.OpeningPreparedRange => ("Opening your audiobook", "Opening the first prepared section for playback."),
      _ => ("Preparing your audiobook", "Starting local preparation."),
    };
    string label = state.TotalSections > 1
      ? $"Section {Math.Min(state.CompletedSections + 1, state.TotalSections):N0} of {state.TotalSections:N0}"
      : state.IsCached ? "Using prepared work" : state.Phase.ToString();
    double progress = state.GetNormalizedProgress(now);
    return new ReaderProgressPresentation(
      true,
      title,
      detail,
      progress,
      label,
      state.ProgressStart,
      state.ProgressEnd,
      state.PhaseStartedAt,
      state.EstimatedPhaseDuration,
      !state.IsCached && state.EstimatedPhaseDuration > TimeSpan.Zero);
  }

  private static ReaderProgressPresentation FromExport(ReaderExportState state)
  {
    string title = state.Kind == ReaderExportKind.Audio ? "Exporting your audiobook" : "Creating your reading video";
    string detail = state.Status switch
    {
      ReaderExportStatus.PreparingAudio => $"Preparing section {Math.Min(state.CompletedSections + 1, state.TotalSections):N0} of {state.TotalSections:N0}.",
      ReaderExportStatus.PreparingVideo => $"Preparing synchronized section {Math.Min(state.CompletedSections + 1, state.TotalSections):N0} of {state.TotalSections:N0}.",
      ReaderExportStatus.WritingAudio => "Writing the lossless WAV file.",
      ReaderExportStatus.AcquiringVideoRuntime => "Preparing the local video engine.",
      ReaderExportStatus.RenderingVideo => "Rendering synchronized highlights.",
      ReaderExportStatus.EncodingVideo => "Encoding the final MP4.",
      _ => "Preparing the export.",
    };
    string label = state.TotalSections > 0 && state.CompletedSections < state.TotalSections
      ? $"Section {state.CompletedSections + 1:N0} of {state.TotalSections:N0}"
      : state.Phase.ToString();
    return Fixed(title, detail, state.Progress, label);
  }

  private static ReaderProgressPresentation FromPublishing(ReaderPublishingState state)
  {
    string title = state.Status == ReaderPublishingStatus.Uploading ? "Uploading to YouTube" : "Creating your publishing series";
    string detail = state.Status switch
    {
      ReaderPublishingStatus.Uploading => $"Uploading episode {state.EpisodeNumber:N0} of {state.EpisodeCount:N0}.",
      ReaderPublishingStatus.Rendering => $"Creating episode {Math.Max(1, state.EpisodeNumber):N0} of {state.EpisodeCount:N0}.",
      _ => "Preparing the series.",
    };
    string label = state.EpisodeCount > 0
      ? $"Episode {Math.Max(1, state.EpisodeNumber):N0} of {state.EpisodeCount:N0}"
      : "Starting";
    return Fixed(title, detail, state.Progress, label);
  }

  private static ReaderProgressPresentation Fixed(string title, string detail, double? value, string label) => new(
    true,
    title,
    detail,
    value,
    label,
    value ?? 0d,
    value ?? 0d,
    DateTimeOffset.MinValue,
    TimeSpan.Zero,
    false);
}
