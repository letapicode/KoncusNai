using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Overlay.Tests;

public sealed class OverlayIndicatorPlacementPolicyTests
{
  [Xunit.Fact]
  public void Place_CentersIndicatorAboveWorkAreaBottom()
  {
    ScreenBounds result = OverlayIndicatorPlacementPolicy.Place(
      new ScreenBounds(0, 0, 1920, 1040),
      indicatorWidth: 84,
      indicatorHeight: 28);

    Xunit.Assert.Equal(new ScreenBounds(918, 994, 84, 28), result);
  }

  [Xunit.Fact]
  public void Place_RespectsOffsetSecondaryMonitor()
  {
    ScreenBounds result = OverlayIndicatorPlacementPolicy.Place(
      new ScreenBounds(-1600, 40, 1600, 860),
      indicatorWidth: 52,
      indicatorHeight: 24);

    Xunit.Assert.Equal(new ScreenBounds(-826, 858, 52, 24), result);
  }

  [Xunit.Fact]
  public void Place_ClampsOversizedIndicatorAndMargin()
  {
    ScreenBounds result = OverlayIndicatorPlacementPolicy.Place(
      new ScreenBounds(100, 200, 40, 30),
      indicatorWidth: 80,
      indicatorHeight: 50,
      bottomMargin: 100);

    Xunit.Assert.Equal(new ScreenBounds(100, 200, 40, 30), result);
  }

  [Xunit.Fact]
  public void Place_RejectsEmptyWorkArea()
  {
    Xunit.Assert.Throws<ArgumentException>(() => OverlayIndicatorPlacementPolicy.Place(
      new ScreenBounds(0, 0, 0, 0),
      indicatorWidth: 84,
      indicatorHeight: 28));
  }
}
