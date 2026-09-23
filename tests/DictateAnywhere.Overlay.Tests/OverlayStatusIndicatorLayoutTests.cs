using System.Drawing;

namespace DictateAnywhere.Overlay.Tests;

public sealed class OverlayStatusIndicatorLayoutTests
{
  [Xunit.Fact]
  public void CalculateIndicatorSize_LeavesMeasuredTextWidthInsidePaintBounds()
  {
    Size measuredText = new(147, 15);

    Size indicatorSize = OverlayStatusIndicatorLayout.CalculateIndicatorSize(measuredText);
    Rectangle paintBounds = new(0, 0, indicatorSize.Width - 1, indicatorSize.Height - 1);
    Rectangle textBounds = OverlayStatusIndicatorLayout.GetTextBounds(paintBounds);

    Xunit.Assert.Equal(measuredText.Width, textBounds.Width);
    Xunit.Assert.Equal(OverlayStatusIndicatorLayout.RightPadding, paintBounds.Right - textBounds.Right);
  }
}
