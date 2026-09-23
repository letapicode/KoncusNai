namespace DictateAnywhere.Core.Contracts;

public sealed record OverlayAnchorSnapshot(
  ScreenBounds? Bounds,
  OverlayAnchorSource Source,
  bool UsedFallbackSource)
{
  public static OverlayAnchorSnapshot Unavailable { get; } = new(
    Bounds: null,
    Source: OverlayAnchorSource.None,
    UsedFallbackSource: false);

  public bool HasBounds => Bounds.HasValue && !Bounds.Value.IsEmpty;
}
