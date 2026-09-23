using System;
using System.Collections.Generic;

namespace DictateAnywhere.Core.Contracts;

/// <summary>One local speech request with a supported language and optional voice choice.</summary>
public sealed record TextToSpeechRequest(
  string Text,
  string Language = "en",
  string? VoiceId = null,
  string? Description = null,
  int Seed = 42,
  string? OutputPath = null,
  string? ProviderId = null)
{
  public TextToSpeechRequest Normalize()
  {
    return new TextToSpeechRequest(
      Text?.Trim() ?? string.Empty,
      Language?.Trim().ToLowerInvariant() ?? string.Empty,
      string.IsNullOrWhiteSpace(VoiceId) ? null : VoiceId.Trim(),
      string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
      Seed,
      string.IsNullOrWhiteSpace(OutputPath) ? null : OutputPath.Trim(),
      string.IsNullOrWhiteSpace(ProviderId) ? null : ProviderId.Trim().ToLowerInvariant());
  }
}

public sealed record TextToSpeechRuntimeMetadata(
  string SelectedDevice,
  string Backend,
  string DataType,
  string? GpuName,
  bool FallbackOccurred,
  string? FallbackReason,
  TimeSpan GenerationTime,
  TimeSpan AudioDuration,
  double RealTimeFactor,
  long? PeakMemoryBytes = null,
  int? SampleRate = null);

public sealed record TextToSpeechResult(
  string AudioPath,
  TimeSpan Duration,
  int SegmentCount,
  string ProviderId,
  string ModelId,
  IReadOnlyList<SpeechWordTiming>? WordTimings = null,
  TextToSpeechRuntimeMetadata? RuntimeMetadata = null);
