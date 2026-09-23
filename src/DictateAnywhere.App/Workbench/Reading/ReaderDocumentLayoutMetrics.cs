using System;
using System.Windows;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Owns the geometry shared by the editable and read-only Reader document surfaces.</summary>
internal static class ReaderDocumentLayoutMetrics
{
  internal const double StandardPageHorizontalInset = 45d;
  internal const double StandardTextHorizontalInset = 21d;

  internal static Thickness StandardPagePadding =>
    new(StandardPageHorizontalInset, 40d, StandardPageHorizontalInset, 42d);

  internal static Thickness StandardTextInsets =>
    new(StandardTextHorizontalInset, 12d, StandardTextHorizontalInset, 16d);

  internal static Thickness DistractionFreePagePadding => new(90d, 72d, 90d, 72d);

  internal static Thickness DistractionFreeTextInsets => new(24d);

  internal static Thickness CreateStandardTextInsets(double visibleVerticalScrollbarWidth)
  {
    if (!double.IsFinite(visibleVerticalScrollbarWidth) || visibleVerticalScrollbarWidth < 0d)
    {
      throw new ArgumentOutOfRangeException(
        nameof(visibleVerticalScrollbarWidth),
        visibleVerticalScrollbarWidth,
        "Scrollbar width must be finite and nonnegative.");
    }

    double reservedWidth = Math.Min(StandardTextHorizontalInset, visibleVerticalScrollbarWidth);
    return new Thickness(
      StandardTextHorizontalInset,
      12d,
      StandardTextHorizontalInset - reservedWidth,
      16d);
  }
}
