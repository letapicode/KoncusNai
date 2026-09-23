using System;

namespace DictateAnywhere.Inference;

internal static class CohereTranscriptionResponseNormalizer
{
  public static NormalizedCohereTranscriptionResponse Normalize(CohereTranscriptionResponse response)
  {
    ArgumentNullException.ThrowIfNull(response);

    string text = TrimText(response.Text);
    return new NormalizedCohereTranscriptionResponse(
      text,
      response.DurationMs,
      NormalizeOptionalString(response.Language),
      response.SampleRateHz,
      response.AudioSeconds);
  }

  public static int GetTrimmedTextLength(CohereTranscriptionResponse response)
  {
    ArgumentNullException.ThrowIfNull(response);
    return TrimText(response.Text).Length;
  }

  private static string TrimText(string? text)
  {
    return (text ?? string.Empty).Trim();
  }

  private static string? NormalizeOptionalString(string? value)
  {
    string normalized = (value ?? string.Empty).Trim();
    return normalized.Length == 0 ? null : normalized;
  }
}
