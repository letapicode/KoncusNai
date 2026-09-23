using System.Drawing;
using System.Windows.Forms;

namespace DictateAnywhere.Overlay;

internal static class OverlayStatusIndicatorLayout
{
  internal const int TextLeft = 29;
  internal const int RightPadding = 10;

  private const int MinimumWidth = 96;
  private const int MaximumTextWidth = 420;
  private const int MaximumTextHeight = 36;
  private const int VerticalChrome = 12;
  private const int BorderExtent = 1;

  internal const TextFormatFlags DrawTextFlags =
    TextFormatFlags.Left |
    TextFormatFlags.VerticalCenter |
    TextFormatFlags.WordBreak |
    TextFormatFlags.EndEllipsis |
    TextFormatFlags.NoPadding;

  private const TextFormatFlags MeasureSingleLineFlags =
    TextFormatFlags.SingleLine |
    TextFormatFlags.NoPadding;

  private const TextFormatFlags MeasureWrappedFlags =
    TextFormatFlags.WordBreak |
    TextFormatFlags.EndEllipsis |
    TextFormatFlags.NoPadding;

  internal static Size Measure(string message, Font font)
  {
    Size singleLine = TextRenderer.MeasureText(
      message,
      font,
      Size.Empty,
      MeasureSingleLineFlags);

    Size measured = singleLine.Width <= MaximumTextWidth
      ? singleLine
      : TextRenderer.MeasureText(
        message,
        font,
        new Size(MaximumTextWidth, MaximumTextHeight),
        MeasureWrappedFlags);

    return CalculateIndicatorSize(measured);
  }

  internal static Size CalculateIndicatorSize(Size measuredText)
  {
    int horizontalChrome = TextLeft + RightPadding + BorderExtent;
    return new Size(
      Math.Clamp(measuredText.Width + horizontalChrome, MinimumWidth, MaximumTextWidth + horizontalChrome),
      Math.Clamp(measuredText.Height + VerticalChrome, 30, MaximumTextHeight + VerticalChrome));
  }

  internal static Rectangle GetTextBounds(Rectangle clientBounds)
  {
    return new Rectangle(
      TextLeft,
      5,
      Math.Max(1, clientBounds.Width - TextLeft - RightPadding),
      Math.Max(1, clientBounds.Height - 10));
  }
}
