using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Overlay;

public sealed record OverlayPresentation(
  OverlayVisualState VisualState,
  string Message,
  bool ShowTimer,
  TimeSpan? Elapsed,
  OverlayPlacementMode PlacementMode,
  OverlayIndicatorKind IndicatorKind,
  OverlayAnimationState AnimationState,
  OverlayAnchorSnapshot? Anchor)
{
  public bool IsAnchoredIndicator =>
    PlacementMode == OverlayPlacementMode.FocusAnchor && Anchor is not null && Anchor.HasBounds;
}
