using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

public sealed class TranscriptionModelRegistry
{
  private readonly IReadOnlyDictionary<string, ITranscriptionModel> modelsByProviderId;

  public TranscriptionModelRegistry(IEnumerable<ITranscriptionModel> models)
  {
    ArgumentNullException.ThrowIfNull(models);

    Dictionary<string, ITranscriptionModel> indexed = new(StringComparer.OrdinalIgnoreCase);
    foreach (ITranscriptionModel model in models)
    {
      ArgumentNullException.ThrowIfNull(model);
      if (string.IsNullOrWhiteSpace(model.ProviderId))
      {
        throw new InvalidOperationException("Transcription model provider id must not be empty.");
      }

      if (!indexed.TryAdd(model.ProviderId.Trim(), model))
      {
        throw new InvalidOperationException(
          $"Duplicate transcription model registration for provider '{model.ProviderId}'.");
      }
    }

    if (indexed.Count == 0)
    {
      throw new InvalidOperationException("At least one transcription model registration is required.");
    }

    modelsByProviderId = indexed;
  }

  public bool TryResolve(string providerId, out ITranscriptionModel? model)
  {
    string normalizedProviderId = string.IsNullOrWhiteSpace(providerId)
      ? string.Empty
      : providerId.Trim();
    return modelsByProviderId.TryGetValue(normalizedProviderId, out model);
  }

  public IReadOnlyCollection<ITranscriptionModel> GetModels()
  {
    return [.. modelsByProviderId.Values];
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The returned registry transfers ownership of disposable model instances to TranscriptionService.")]
  public static TranscriptionModelRegistry CreateDefault(
    CohereTranscriptionOptions cohereOptions,
    CrisperWhisperTranscriptionOptions crisperWhisperOptions,
    IDiagnostics? diagnostics = null)
  {
    ArgumentNullException.ThrowIfNull(cohereOptions);
    ArgumentNullException.ThrowIfNull(crisperWhisperOptions);

    return new TranscriptionModelRegistry(
    [
      new CohereTranscriptionService(cohereOptions, diagnostics),
      new CrisperWhisperTranscriptionService(crisperWhisperOptions),
    ]);
  }
}
