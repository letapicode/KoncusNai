using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Runtime;

internal static class LocalLlamaCppRuntimeReadiness
{
  public static Task<LocalChatRuntimeReadiness> CheckAsync(string modelId, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    LlamaCppRuntimeAvailability availability = LlamaCppRuntimeAvailability.Check(LlamaCppChatOptions.ForModel(modelId));
    return Task.FromResult(new LocalChatRuntimeReadiness(
      availability.IsConfigured,
      availability.StatusMessage,
      [],
      []));
  }

  public static async Task<LocalChatRuntimeReadiness> ProvisionAsync(
    string modelId,
    System.IProgress<double>? progress,
    CancellationToken cancellationToken = default)
  {
    LlamaCppRuntimeAvailability availability = await new LlamaCppProvisioningService()
      .ProvisionAsync(modelId, progress, cancellationToken)
      .ConfigureAwait(false);
    return new LocalChatRuntimeReadiness(availability.IsConfigured, availability.StatusMessage, [], []);
  }

  public static Task<LocalChatRuntimeReadiness> CheckAsync(CancellationToken cancellationToken = default)
    => CheckAsync(LlamaCppModelCatalog.Gemma3FourBModelId, cancellationToken);

  public static Task<LocalChatRuntimeReadiness> ProvisionAsync(
    System.IProgress<double>? progress,
    CancellationToken cancellationToken = default)
    => ProvisionAsync(LlamaCppModelCatalog.Gemma3FourBModelId, progress, cancellationToken);
}
