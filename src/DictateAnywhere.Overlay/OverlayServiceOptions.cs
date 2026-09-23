using System;

namespace DictateAnywhere.Overlay;

public sealed record OverlayServiceOptions(
  bool Enabled,
  bool CaretIndicatorEnabled,
  bool FallbackToCornerOverlay,
  bool AutoHideInsertedState,
  TimeSpan InsertedStateDuration,
  bool AutoHideErrorState,
  TimeSpan ErrorStateDuration)
{
  public static OverlayServiceOptions Default { get; } = new(
    Enabled: true,
    CaretIndicatorEnabled: true,
    FallbackToCornerOverlay: true,
    AutoHideInsertedState: true,
    InsertedStateDuration: TimeSpan.FromMilliseconds(750),
    AutoHideErrorState: true,
    ErrorStateDuration: TimeSpan.FromSeconds(4));
}
