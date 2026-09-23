using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Runtime;

/// <summary>Model-manager adapter for Koncus Nai's managed llama.cpp GGUF catalog.</summary>
internal sealed class LlamaCppChatModelManager : IModelManager
{
  public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    IReadOnlyList<ModelInfo> models = LlamaCppModelCatalog.GetAll()
      .Select(model => new ModelInfo(
        ChatProviderIds.LlamaCppLocal,
        model.ModelId,
        model.DisplayName,
        LlamaCppRuntimeAvailability.Check(LlamaCppChatOptions.ForModel(model.ModelId)).IsConfigured,
        string.Equals(model.ModelId, LlamaCppModelCatalog.Gemma3FourBModelId, StringComparison.OrdinalIgnoreCase),
        ["en"]))
      .ToArray();
    return Task.FromResult(models);
  }

  public async Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default)
  {
    if (!string.Equals(providerId, ChatProviderIds.LlamaCppLocal, StringComparison.OrdinalIgnoreCase)) return null;
    IReadOnlyList<ModelInfo> models = await GetModelsAsync(cancellationToken).ConfigureAwait(false);
    return models.FirstOrDefault(model => model.IsInstalled) ?? models[0];
  }

  public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;

  public async Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
  {
    if (!string.Equals(selection.ProviderId, ChatProviderIds.LlamaCppLocal, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException($"'{selection.ProviderId}' is not managed by llama.cpp.");
    }

    _ = await new LlamaCppProvisioningService()
      .ProvisionAsync(selection.ModelId, progress, cancellationToken)
      .ConfigureAwait(false);
  }

  public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
    => throw new InvalidOperationException("The local llama.cpp model is managed by Koncus Nai and cannot be removed from this screen.");
}
