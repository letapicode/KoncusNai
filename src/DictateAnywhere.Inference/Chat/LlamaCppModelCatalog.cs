using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DictateAnywhere.Inference;

/// <summary>
/// The small, curated set of GGUF chat models that Koncus Nai can download and
/// run through its managed llama.cpp server. Keeping this catalog in the
/// inference layer makes model paths, download sources, and runtime options
/// consistent across the UI, installer, readiness checks, and chat service.
/// </summary>
public static class LlamaCppModelCatalog
{
  public const string Gemma3FourBModelId = "gemma-3-4b-it-Q4_K_M.gguf";
  public const string Qwen3OnePointSevenBModelId = "Qwen3-1.7B-Q4_K_M.gguf";
  public const string Qwen3FourBModelId = "Qwen3-4B-Q4_K_M.gguf";

  private static readonly IReadOnlyList<LlamaCppModelDefinition> Definitions =
  [
    new(
      Gemma3FourBModelId,
      "Gemma 3 4B · Balanced",
      "The installed Koncus Nai default: a compact 4-bit Gemma 3 model for general local chat.",
      "https://huggingface.co/ggml-org/gemma-3-4b-it-GGUF/resolve/d0976223747697cb51e056d85c532013931fe52e/gemma-3-4b-it-Q4_K_M.gguf?download=true",
      "882e8d2db44dc554fb0ea5077cb7e4bc49e7342a1f0da57901c0802ea21a0863",
      DisableReasoning: false),
    new(
      Qwen3OnePointSevenBModelId,
      "Qwen3 1.7B · Fast",
      "A smaller 4-bit Qwen3 model for the lowest CPU latency. Best when speed matters more than deep code generation.",
      "https://huggingface.co/ggml-org/Qwen3-1.7B-GGUF/resolve/daeb8e2d528a760970442092f6bf1e55c3b659eb/Qwen3-1.7B-Q4_K_M.gguf?download=true",
      "d2387ca2dbfee2ffabce7120d3770dadca0b293052bc2f0e138fdc940d9bc7b5",
      DisableReasoning: true),
    new(
      Qwen3FourBModelId,
      "Qwen3 4B · Code",
      "A 4-bit Qwen3 model tuned for stronger instruction following and coding. It uses non-thinking mode so CPU responses stay direct.",
      "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/bc640142c66e1fdd12af0bd68f40445458f3869b/Qwen3-4B-Q4_K_M.gguf?download=true",
      "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5",
      DisableReasoning: true),
  ];

  public static IReadOnlyList<LlamaCppModelDefinition> GetAll() => Definitions;

  public static bool TryGet(string? modelId, out LlamaCppModelDefinition? definition)
  {
    definition = Definitions.FirstOrDefault(candidate =>
      string.Equals(candidate.ModelId, modelId?.Trim(), StringComparison.OrdinalIgnoreCase));
    return definition is not null;
  }

  public static LlamaCppModelDefinition GetRequired(string? modelId)
  {
    if (TryGet(modelId, out LlamaCppModelDefinition? definition) && definition is not null)
    {
      return definition;
    }

    throw new InvalidOperationException($"'{modelId}' is not a supported local llama.cpp chat model.");
  }

  public static string GetModelPath(string modelId)
  {
    LlamaCppModelDefinition definition = GetRequired(modelId);
    return Path.Combine(LlamaCppChatOptions.DefaultModelDirectory, definition.ModelId);
  }
}

public sealed record LlamaCppModelDefinition(
  string ModelId,
  string DisplayName,
  string Description,
  string DownloadUrl,
  string Sha256,
  bool DisableReasoning);
