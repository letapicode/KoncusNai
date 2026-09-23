using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record TranscriptionModelSelection(
  string ProviderId,
  string ModelId)
{
  public TranscriptionModelSelection Normalize()
  {
    string normalizedProviderId = string.IsNullOrWhiteSpace(ProviderId)
      ? AppSettings.Default.TranscriptionProviderId
      : ProviderId.Trim().ToLowerInvariant();
    string normalizedModelId = string.IsNullOrWhiteSpace(ModelId)
      ? AppSettings.Default.TranscriptionModelId
      : ModelId.Trim();

    return new TranscriptionModelSelection(normalizedProviderId, normalizedModelId);
  }
}
