using System;
using System.Windows.Media;

namespace DictateAnywhere.App.Workbench.Reading;

internal static class ReaderHighlightContrast
{
  // Keep the selected hue when possible; move toward the theme's readable ink only as needed.
  internal static Color Resolve(Color accent, Color background, Color ink)
  {
    for (int step = 0; step <= 20; step++)
    {
      double amount = step / 20d;
      Color candidate = Color.FromRgb(
        (byte)Math.Round(accent.R + (ink.R - accent.R) * amount),
        (byte)Math.Round(accent.G + (ink.G - accent.G) * amount),
        (byte)Math.Round(accent.B + (ink.B - accent.B) * amount));
      if (Ratio(candidate, background) >= 3) return candidate;
    }
    return ink;
  }

  internal static double Ratio(Color first, Color second)
  {
    double a = Luminance(first);
    double b = Luminance(second);
    return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
  }

  private static double Luminance(Color color) =>
    0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

  private static double Linear(byte channel)
  {
    double value = channel / 255d;
    return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
  }
}
