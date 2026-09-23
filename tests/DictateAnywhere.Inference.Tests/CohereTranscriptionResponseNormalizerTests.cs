using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class CohereTranscriptionResponseNormalizerTests
{
  [Xunit.Fact]
  public void Normalize_TrimsText_AndNormalizesOptionalMetadata()
  {
    CohereTranscriptionResponse response = new(
      "  hello world  ",
      12.5,
      " en ",
      16_000,
      1.25);

    NormalizedCohereTranscriptionResponse normalized =
      CohereTranscriptionResponseNormalizer.Normalize(response);

    Xunit.Assert.Equal("hello world", normalized.Text);
    Xunit.Assert.Equal(12.5, normalized.WorkerInferenceDurationMs);
    Xunit.Assert.Equal("en", normalized.Language);
    Xunit.Assert.Equal(16_000, normalized.SampleRateHz);
    Xunit.Assert.Equal(1.25, normalized.AudioSeconds);
  }

  [Xunit.Fact]
  public void Normalize_AllowsEmptyText()
  {
    CohereTranscriptionResponse response = new("  ", 0);

    NormalizedCohereTranscriptionResponse normalized =
      CohereTranscriptionResponseNormalizer.Normalize(response);

    Xunit.Assert.Equal(string.Empty, normalized.Text);
    Xunit.Assert.Equal(0, normalized.WorkerInferenceDurationMs);
  }
}
