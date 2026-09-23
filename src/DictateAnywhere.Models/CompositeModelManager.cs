using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Models;

public sealed class CompositeModelManager : IModelManager
{
  private readonly IReadOnlyDictionary<string, IProviderModelManager> managersByProviderId;

  public CompositeModelManager(IEnumerable<IProviderModelManager> managers)
  {
    ArgumentNullException.ThrowIfNull(managers);

    Dictionary<string, IProviderModelManager> indexed = new(StringComparer.OrdinalIgnoreCase);
    foreach (IProviderModelManager manager in managers)
    {
      ArgumentNullException.ThrowIfNull(manager);
      if (!indexed.TryAdd(manager.ProviderId, manager))
      {
        throw new ModelManagementException($"Duplicate model manager registration for provider '{manager.ProviderId}'.");
      }
    }

    if (indexed.Count == 0)
    {
      throw new ModelManagementException("At least one provider model manager must be registered.");
    }

    managersByProviderId = indexed;
  }

  public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
  {
    List<ModelInfo> results = new();
    foreach (IProviderModelManager manager in managersByProviderId.Values.OrderBy(manager => manager.ProviderId, StringComparer.OrdinalIgnoreCase))
    {
      cancellationToken.ThrowIfCancellationRequested();
      IReadOnlyList<ModelInfo> models = await manager.GetModelsAsync(cancellationToken).ConfigureAwait(false);
      results.AddRange(models);
    }

    return results;
  }

  public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default)
  {
    if (!TryResolveManager(providerId, out IProviderModelManager? manager) || manager is null)
    {
      return Task.FromResult<ModelInfo?>(null);
    }

    return manager.GetActiveModelAsync(cancellationToken);
  }

  public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);
    IProviderModelManager manager = ResolveManager(selection);
    return manager.SetActiveModelAsync(selection.ModelId, cancellationToken);
  }

  public Task DownloadModelAsync(
    TranscriptionModelSelection selection,
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);
    IProviderModelManager manager = ResolveManager(selection);
    return manager.DownloadModelAsync(selection.ModelId, progress, cancellationToken);
  }

  public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);
    IProviderModelManager manager = ResolveManager(selection);
    return manager.DeleteModelAsync(selection.ModelId, cancellationToken);
  }

  private IProviderModelManager ResolveManager(TranscriptionModelSelection selection)
  {
    TranscriptionModelSelection normalized = selection.Normalize();
    if (TryResolveManager(normalized.ProviderId, out IProviderModelManager? manager) && manager is not null)
    {
      return manager;
    }

    throw new ModelManagementException(
      $"No local model manager is registered for transcription provider '{normalized.ProviderId}'.");
  }

  private bool TryResolveManager(string providerId, out IProviderModelManager? manager)
  {
    string normalizedProviderId = string.IsNullOrWhiteSpace(providerId)
      ? string.Empty
      : providerId.Trim();
    return managersByProviderId.TryGetValue(normalizedProviderId, out manager);
  }
}
