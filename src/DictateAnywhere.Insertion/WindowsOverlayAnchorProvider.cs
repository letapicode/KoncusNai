using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Insertion;

public sealed class WindowsOverlayAnchorProvider : IOverlayAnchorProvider
{
  private readonly IWindowFocusProvider windowFocusProvider;

  public WindowsOverlayAnchorProvider(IWindowFocusProvider windowFocusProvider)
  {
    this.windowFocusProvider = windowFocusProvider ?? throw new ArgumentNullException(nameof(windowFocusProvider));
  }

  public OverlayAnchorSnapshot GetCurrentAnchor()
  {
    WindowFocusContext focusContext = windowFocusProvider.GetWindowFocusContext();

    if (focusContext.HasCaretBounds)
    {
      return new OverlayAnchorSnapshot(
        Bounds: focusContext.CaretBounds,
        Source: OverlayAnchorSource.Caret,
        UsedFallbackSource: false);
    }

    if (focusContext.IsEditable && focusContext.HasFocusedElementBounds)
    {
      return new OverlayAnchorSnapshot(
        Bounds: focusContext.FocusedElementBounds,
        Source: OverlayAnchorSource.FocusedElement,
        UsedFallbackSource: true);
    }

    if (focusContext.HasForegroundWindowBounds)
    {
      return new OverlayAnchorSnapshot(
        Bounds: focusContext.ForegroundWindowBounds,
        Source: OverlayAnchorSource.ForegroundWindow,
        UsedFallbackSource: true);
    }

    return OverlayAnchorSnapshot.Unavailable;
  }
}
