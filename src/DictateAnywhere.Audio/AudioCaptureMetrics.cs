using System;

namespace DictateAnywhere.Audio;

public sealed record AudioCaptureMetrics(
  TimeSpan StartLatency,
  TimeSpan StopLatency,
  TimeSpan CaptureWallClockDuration,
  int BytesCaptured);
