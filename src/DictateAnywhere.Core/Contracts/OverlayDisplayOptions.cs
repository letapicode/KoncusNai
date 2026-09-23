using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record OverlayDisplayOptions(
  OverlayPlacementMode PlacementMode,
  OverlayIndicatorKind IndicatorKind,
  OverlayAnimationState AnimationState,
  TimeSpan? AutoHideDuration = null)
{
  public static OverlayDisplayOptions Default { get; } = new(
    OverlayPlacementMode.CornerPanel,
    OverlayIndicatorKind.StatusPanel,
    OverlayAnimationState.None);

  public static OverlayDisplayOptions AnchoredRecording { get; } = new(
    OverlayPlacementMode.FocusAnchor,
    OverlayIndicatorKind.RecordingIndicator,
    OverlayAnimationState.Pulse);

  public static OverlayDisplayOptions AnchoredTranscribing { get; } = new(
    OverlayPlacementMode.FocusAnchor,
    OverlayIndicatorKind.TranscribingIndicator,
    OverlayAnimationState.LetterSpin);

  public static OverlayDisplayOptions AnchoredStatus { get; } = new(
    OverlayPlacementMode.FocusAnchor,
    OverlayIndicatorKind.StatusIndicator,
    OverlayAnimationState.None);

  public static OverlayDisplayOptions AnchoredCompletion { get; } = new(
    OverlayPlacementMode.FocusAnchor,
    OverlayIndicatorKind.CompletionIndicator,
    OverlayAnimationState.None);

  public static OverlayDisplayOptions AnchoredNotice { get; } = new(
    OverlayPlacementMode.FocusAnchor,
    OverlayIndicatorKind.StatusIndicator,
    OverlayAnimationState.None,
    DictationStatusMessages.TransientOutcomeDuration);

  public static OverlayDisplayOptions StatusPanelDefault { get; } = Default;

  public bool RequestsAnchor => PlacementMode == OverlayPlacementMode.FocusAnchor;
}
