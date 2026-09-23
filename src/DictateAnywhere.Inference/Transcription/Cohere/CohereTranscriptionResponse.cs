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
    double? audioSeconds)
  {
    Text = text;
    DurationMs = durationMs;
    Language = language;
    SampleRateHz = sampleRateHz;
    AudioSeconds = audioSeconds;
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
}
