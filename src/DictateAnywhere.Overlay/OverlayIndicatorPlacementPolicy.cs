using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Overlay;

/// <summary>Calculates a stable bottom-center indicator position inside a monitor work area.</summary>
public static class OverlayIndicatorPlacementPolicy
{
  public const int DefaultBottomMargin = 18;

  public static ScreenBounds Place(
    ScreenBounds workingArea,
    int indicatorWidth,
    int indicatorHeight,
    int bottomMargin = DefaultBottomMargin)
  {
    if (workingArea.IsEmpty)
    {
      throw new ArgumentException("A non-empty monitor work area is required.", nameof(workingArea));
    }

    int width = Math.Clamp(indicatorWidth, 1, workingArea.Width);
    int height = Math.Clamp(indicatorHeight, 1, workingArea.Height);
    int margin = Math.Clamp(bottomMargin, 0, Math.Max(0, workingArea.Height - height));
    int left = workingArea.Left + ((workingArea.Width - width) / 2);
    int top = workingArea.Bottom - height - margin;
    return new ScreenBounds(left, top, width, height);
  }
}
