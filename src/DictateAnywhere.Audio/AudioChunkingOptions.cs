using System;

namespace DictateAnywhere.Audio;

public sealed record AudioChunkingOptions(
  bool Enabled,
  TimeSpan PreferredChunkDuration,
  TimeSpan MaxChunkDuration,
  TimeSpan SilenceBoundaryDuration,
  short SilenceThresholdPcm16)
{
  public static AudioChunkingOptions Default { get; } = new(
    Enabled: true,
    PreferredChunkDuration: TimeSpan.FromSeconds(55),
    MaxChunkDuration: TimeSpan.FromSeconds(75),
    SilenceBoundaryDuration: TimeSpan.FromMilliseconds(800),
    SilenceThresholdPcm16: 350);
}
