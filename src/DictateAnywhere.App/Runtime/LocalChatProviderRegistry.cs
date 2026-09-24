using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Runtime;

internal sealed class LocalChatProviderRegistry
{
  private readonly IReadOnlyDictionary<string, LocalChatProviderRegistration> registrationsByProviderId;

  public LocalChatProviderRegistry(IEnumerable<LocalChatProviderRegistration> registrations)
  {
    ArgumentNullException.ThrowIfNull(registrations);

    Dictionary<string, LocalChatProviderRegistration> indexed = new(StringComparer.OrdinalIgnoreCase);
    foreach (LocalChatProviderRegistration registration in registrations)
    {
      ArgumentNullException.ThrowIfNull(registration);
      if (!indexed.TryAdd(registration.ProviderId, registration))
      {
        throw new InvalidOperationException($"Duplicate local chat provider registration for '{registration.ProviderId}'.");
      }
    }

    if (indexed.Count == 0)
    {
      throw new InvalidOperationException("At least one local chat provider registration is required.");
    }

    registrationsByProviderId = indexed;
  }

  public bool TryResolve(string providerId, out LocalChatProviderRegistration? registration)
  {
    string normalizedProviderId = string.IsNullOrWhiteSpace(providerId)
      ? string.Empty
      : providerId.Trim();
    return registrationsByProviderId.TryGetValue(normalizedProviderId, out registration);
  }

  public IReadOnlyList<LocalChatProviderDefinition> GetDefinitions()
  {
    return registrationsByProviderId.Values
      .Select(registration => registration.Definition)
      .OrderBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
      .ToArray();
  }

  public static LocalChatProviderRegistry CreateDefault()
  {
    return new LocalChatProviderRegistry(
    [
      new LocalChatProviderRegistration(
        new LocalChatProviderDefinition(
          ChatProviderIds.OllamaLocal,
          "Ollama local chat",
          "Gemma 4 E4B Q4_K_M through Ollama. Ollama manages the quantized model and keeps it local on this device.",
          [new LocalChatModelDefinition("gemma4:e4b", "Gemma 4 E4B (Ollama Q4_K_M)", "Quantized 8B Gemma 4 E4B model served by your local Ollama installation.")],
          ModelProviderOperationalMetadata.LocalOffline),
        selection => new OllamaChatService(OllamaChatOptions.ForModel(selection.ModelId), OllamaListenerTrust.IsCurrentTrusted)),
      new LocalChatProviderRegistration(
        new LocalChatProviderDefinition(
          ChatProviderIds.LlamaCppLocal,
          "llama.cpp local chat",
          "Fully local GGUF inference through llama.cpp. Keeps a quantized model warm between messages for lower CPU latency.",
          LlamaCppModelCatalog.GetAll()
            .Select(model => new LocalChatModelDefinition(model.ModelId, model.DisplayName, model.Description))
            .ToArray(),
          ModelProviderOperationalMetadata.LocalOffline),
        selection => new LlamaCppChatService(LlamaCppChatOptions.ForModel(selection.ModelId))),
    ]);
  }
}
