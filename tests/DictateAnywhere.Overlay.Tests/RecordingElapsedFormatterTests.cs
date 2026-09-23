using System;

namespace DictateAnywhere.Overlay.Tests;

public sealed class RecordingElapsedFormatterTests
{
  [Xunit.Theory]
  [Xunit.InlineData(-0.1, "00:00")]
  [Xunit.InlineData(0.0, "00:00")]
  [Xunit.InlineData(0.9, "00:00")]
  [Xunit.InlineData(1.0, "00:01")]
  [Xunit.InlineData(59.9, "00:59")]
  [Xunit.InlineData(60.0, "01:00")]
  [Xunit.InlineData(3599.9, "59:59")]
  [Xunit.InlineData(3600.0, "60:00")]
  public void Format_UsesWholeMinutesAndSeconds(double totalSeconds, string expected)
  {
    string actual = RecordingElapsedFormatter.Format(TimeSpan.FromSeconds(totalSeconds));

    Xunit.Assert.Equal(expected, actual);
  }
}
