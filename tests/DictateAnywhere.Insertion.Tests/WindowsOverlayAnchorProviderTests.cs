using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.Insertion.Tests;

public sealed class WindowsOverlayAnchorProviderTests
{
  [Xunit.Fact]
  public void GetCurrentAnchor_PrefersCaretBounds()
  {
    WindowsOverlayAnchorProvider provider = new(new StubWindowFocusProvider
    {
      FocusContext = new WindowFocusContext
      {
        CaretBounds = new ScreenBounds(200, 320, 1, 18),
        FocusedElementBounds = new ScreenBounds(180, 300, 240, 32),
        ForegroundWindowBounds = new ScreenBounds(100, 100, 800, 600),
        Editability = WindowEditability.Editable,
      },
    });

    OverlayAnchorSnapshot anchor = provider.GetCurrentAnchor();

    Xunit.Assert.Equal(OverlayAnchorSource.Caret, anchor.Source);
    Xunit.Assert.False(anchor.UsedFallbackSource);
    Xunit.Assert.Equal(new ScreenBounds(200, 320, 1, 18), anchor.Bounds);
  }

  [Xunit.Fact]
  public void GetCurrentAnchor_UsesEditableFocusedElement_WhenCaretUnavailable()
  {
    WindowsOverlayAnchorProvider provider = new(new StubWindowFocusProvider
    {
      FocusContext = new WindowFocusContext
      {
        FocusedElementBounds = new ScreenBounds(180, 300, 240, 32),
        ForegroundWindowBounds = new ScreenBounds(100, 100, 800, 600),
        Editability = WindowEditability.Editable,
      },
    });

    OverlayAnchorSnapshot anchor = provider.GetCurrentAnchor();

    Xunit.Assert.Equal(OverlayAnchorSource.FocusedElement, anchor.Source);
    Xunit.Assert.True(anchor.UsedFallbackSource);
    Xunit.Assert.Equal(new ScreenBounds(180, 300, 240, 32), anchor.Bounds);
  }

  [Xunit.Fact]
  public void GetCurrentAnchor_UsesForegroundWindow_WhenEditableElementUnavailable()
  {
    WindowsOverlayAnchorProvider provider = new(new StubWindowFocusProvider
    {
      FocusContext = new WindowFocusContext
      {
        FocusedElementBounds = new ScreenBounds(180, 300, 240, 32),
        ForegroundWindowBounds = new ScreenBounds(100, 100, 800, 600),
        Editability = WindowEditability.Unknown,
      },
    });

    OverlayAnchorSnapshot anchor = provider.GetCurrentAnchor();

    Xunit.Assert.Equal(OverlayAnchorSource.ForegroundWindow, anchor.Source);
    Xunit.Assert.True(anchor.UsedFallbackSource);
    Xunit.Assert.Equal(new ScreenBounds(100, 100, 800, 600), anchor.Bounds);
  }

  [Xunit.Fact]
  public void GetCurrentAnchor_ReturnsUnavailable_WhenNoBoundsExist()
  {
    WindowsOverlayAnchorProvider provider = new(new StubWindowFocusProvider
    {
      FocusContext = WindowFocusContext.Empty,
    });

    OverlayAnchorSnapshot anchor = provider.GetCurrentAnchor();

    Xunit.Assert.Equal(OverlayAnchorSnapshot.Unavailable, anchor);
  }

  private sealed class StubWindowFocusProvider : IWindowFocusProvider
  {
    public WindowFocusContext FocusContext { get; init; } = WindowFocusContext.Empty;

    public nint GetForegroundWindowHandle()
    {
      return FocusContext.ForegroundWindowHandle;
    }

    public WindowFocusContext GetWindowFocusContext()
    {
      return FocusContext;
    }
  }
}
