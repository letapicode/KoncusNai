using System.Threading;
using System.Threading.Tasks;
using System.IO;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Runtime;

internal static class LocalLlamaCppRuntimeReadiness
{
  public static Task<LocalChatRuntimeReadiness> CheckAsync(string modelId, CancellationToken cancellationToken = default)
  {
    return Task.Run(() =>
    {
      cancellationToken.ThrowIfCancellationRequested();
      LlamaCppChatOptions options = LlamaCppChatOptions.ForModel(modelId);
      bool installed;
      try { installed = (File.GetAttributes(options.ModelPath) & FileAttributes.Directory) == 0; }
      catch (FileNotFoundException) { installed = false; }
      catch (DirectoryNotFoundException) { installed = false; }
      LlamaCppRuntimeAvailability availability = LlamaCppRuntimeAvailability.Check(options);
      return new LocalChatRuntimeReadiness(availability.IsConfigured, availability.StatusMessage, [], [])
      { IsInstalled = installed };
    }, cancellationToken);
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
