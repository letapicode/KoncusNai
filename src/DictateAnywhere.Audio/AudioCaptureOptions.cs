namespace DictateAnywhere.Audio;

public sealed record AudioCaptureOptions(
  string? PreferredInputDeviceId,
  int TargetSampleRateHz,
  float GainMultiplier,
  bool EnableSilenceTrimHook,
  short SilenceThresholdPcm16,
  bool EnableNoiseSuppression,
  AudioChunkingOptions Chunking)
{
  public static AudioCaptureOptions Default { get; } = new(
    PreferredInputDeviceId: null,
    TargetSampleRateHz: 16_000,
    GainMultiplier: 1.0f,
    EnableSilenceTrimHook: true,
    SilenceThresholdPcm16: 350,
    EnableNoiseSuppression: false,
    Chunking: AudioChunkingOptions.Default);
}
