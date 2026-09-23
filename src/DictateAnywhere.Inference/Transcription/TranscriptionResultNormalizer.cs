using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

internal static class TranscriptionResultNormalizer
{
  public static TranscriptionResult Normalize(
    TranscriptionResult result,
    string providerId,
    string requestedModelId)
  {
    ArgumentNullException.ThrowIfNull(result);

    string normalizedText = (result.Text ?? string.Empty).Trim();
    string normalizedModelId = string.IsNullOrWhiteSpace(result.ModelId)
      ? requestedModelId
      : result.ModelId.Trim();

    return result with
    {
      Text = normalizedText,
      ModelId = normalizedModelId,
    };
  }
}
