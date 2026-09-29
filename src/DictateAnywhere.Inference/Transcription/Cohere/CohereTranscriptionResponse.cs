using System.Text.Json.Serialization;

namespace DictateAnywhere.Inference;

internal sealed class CohereTranscriptionResponse
{
  [JsonConstructor]
  public CohereTranscriptionResponse(
    string text,
    double durationMs,
    string? language,
    int? sampleRateHz,
    double? audioSeconds,
    string? backend = null,
    string? dtype = null,
    string? fallbackReason = null,
    int? threads = null,
    string? nativeVersion = null,
    bool isTruncated = false)
  {
    Text = text;
    DurationMs = durationMs;
    Language = language;
    SampleRateHz = sampleRateHz;
    AudioSeconds = audioSeconds;
    Backend = backend;
    Dtype = dtype;
    FallbackReason = fallbackReason;
    Threads = threads;
    NativeVersion = nativeVersion;
    IsTruncated = isTruncated;
  }

  public CohereTranscriptionResponse(string text, double durationMs)
    : this(text, durationMs, null, null, null)
  {
  }

  public string Text { get; }

  public double DurationMs { get; }

  public string? Language { get; }

  public int? SampleRateHz { get; }

  public double? AudioSeconds { get; }

  public string? Backend { get; }
  public string? Dtype { get; }
  public string? FallbackReason { get; }
  public int? Threads { get; }
  public string? NativeVersion { get; }
  public bool IsTruncated { get; }
}
