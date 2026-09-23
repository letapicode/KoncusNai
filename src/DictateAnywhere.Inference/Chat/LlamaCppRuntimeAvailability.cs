using System;
using System.IO;

namespace DictateAnywhere.Inference;

public sealed record LlamaCppRuntimeAvailability(bool IsConfigured, string StatusMessage)
{
  public static LlamaCppRuntimeAvailability Check(LlamaCppChatOptions? options = null)
  {
    LlamaCppChatOptions configured = options ?? LlamaCppChatOptions.Default;
    if (!File.Exists(configured.ServerExecutablePath))
    {
      return new(false, "Install llama.cpp first. Run scripts\\setup-llama-cpp.ps1.");
    }
    if (string.Equals(
          Path.GetFullPath(configured.ServerExecutablePath),
          Path.GetFullPath(Path.Combine(LlamaCppChatOptions.DefaultRuntimeDirectory, "llama-server.exe")),
          StringComparison.OrdinalIgnoreCase)
        && !LlamaCppProvisioningService.IsVerifiedRuntime(configured))
    {
      return new(false, "Reinstall the managed llama.cpp runtime to verify its provenance.");
    }
    if (!File.Exists(configured.ModelPath))
    {
      return new(false, "Copy a compatible Q4_K_M GGUF model to the Koncus Nai llama.cpp model folder.");
    }
    if (string.Equals(
          Path.GetFullPath(Path.GetDirectoryName(configured.ModelPath)!),
          Path.GetFullPath(LlamaCppChatOptions.DefaultModelDirectory),
          StringComparison.OrdinalIgnoreCase)
        && !LlamaCppProvisioningService.HasVerifiedModelMarker(configured.ModelPath))
    {
      return new(false, "Reinstall the selected GGUF model to verify its provenance.");
    }
    return new(true, "Local llama.cpp and the GGUF model are ready. The server starts automatically when you send a message.");
  }
}
