using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record ChatModelSelection(
  string ProviderId,
  string ModelId)
{
  public static ChatModelSelection Default { get; } = new(
    ChatProviderIds.LlamaCppLocal,
    "gemma-3-4b-it-Q4_K_M.gguf");

  public ChatModelSelection Normalize()
  {
    string normalizedProviderId = string.IsNullOrWhiteSpace(ProviderId)
      ? Default.ProviderId
      : ProviderId.Trim().ToLowerInvariant();
    string normalizedModelId = string.IsNullOrWhiteSpace(ModelId)
      ? Default.ModelId
      : ModelId.Trim();

    return new ChatModelSelection(normalizedProviderId, normalizedModelId);
  }
}
