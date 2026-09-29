using System;
using Drawing = System.Drawing;

namespace DictateAnywhere.App.Tray;

internal static class TrayFlyoutPlacement
{
  internal static Drawing.Point Compute(Drawing.Point cursor, Drawing.Rectangle workArea, Drawing.Size size)
  {
    int x = cursor.X - size.Width + 20;
    int y = cursor.Y - size.Height - 8;
    if (y < workArea.Top)
    {
      y = cursor.Y + 8;
    }

    return Clamp(new Drawing.Point(x, y), workArea, size);
  }

  internal static Drawing.Point Clamp(Drawing.Point location, Drawing.Rectangle workArea, Drawing.Size size) =>
    new(
      Math.Clamp(location.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width)),
      Math.Clamp(location.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)));
}
