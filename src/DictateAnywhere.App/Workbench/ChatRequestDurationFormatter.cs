using System;
using System.Globalization;

namespace DictateAnywhere.App.Workbench;

internal static class ChatRequestDurationFormatter
{
  public static string Format(TimeSpan elapsed)
  {
    TimeSpan normalized = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    int totalHours = (int)Math.Floor(normalized.TotalHours);
    int minutes = normalized.Minutes;
    int seconds = normalized.Seconds;

    if (totalHours > 0)
    {
      return string.Format(CultureInfo.InvariantCulture, "{0}h {1:D2}m {2:D2}s", totalHours, minutes, seconds);
    }

    if (minutes > 0)
    {
      return string.Format(CultureInfo.InvariantCulture, "{0}m {1:D2}s", minutes, seconds);
    }

    return string.Format(CultureInfo.InvariantCulture, "{0}s", seconds);
  }
}
