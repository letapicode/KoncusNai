using DictateAnywhere.Overlay;

namespace DictateAnywhere.Overlay.Tests;

public sealed class OverlayWindowStylesTests
{
  [Xunit.Fact]
  public void BuildFocusSafeAlwaysOnTopStyle_IncludesNoActivateTopMostToolWindowAndInputPassThroughFlags()
  {
    int style = OverlayWindowStyles.BuildFocusSafeAlwaysOnTopStyle(0);

    Xunit.Assert.NotEqual(0, style & OverlayWindowStyles.WsExNoActivate);
    Xunit.Assert.NotEqual(0, style & OverlayWindowStyles.WsExTopMost);
    Xunit.Assert.NotEqual(0, style & OverlayWindowStyles.WsExToolWindow);
    Xunit.Assert.NotEqual(0, style & OverlayWindowStyles.WsExTransparent);
  }
}
