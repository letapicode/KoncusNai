using System;
using System.Globalization;

namespace DictateAnywhere.Overlay;

internal static class RecordingElapsedFormatter
{
  public static string Format(TimeSpan elapsed)
  {
    long totalSeconds = Math.Max(0L, (long)Math.Floor(elapsed.TotalSeconds));
    long totalMinutes = totalSeconds / 60L;
    long seconds = totalSeconds % 60L;
    return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", totalMinutes, seconds);
  }
}
