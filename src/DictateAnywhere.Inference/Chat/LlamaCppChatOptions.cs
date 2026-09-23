using System;
using System.IO;

namespace DictateAnywhere.Inference;

/// <summary>Configuration for an offline llama.cpp server owned by Koncus Nai.</summary>
public sealed record LlamaCppChatOptions(
  string ServerExecutablePath,
  string ModelPath,
  Uri ServerBaseAddress,
  int ContextSize,
  int ThreadCount,
  int MaxNewTokens,
  TimeSpan ServerStartupTimeout,
  TimeSpan RequestTimeout,
  bool StartServer,
  bool DisableReasoning)
{
  public static string DefaultRuntimeDirectory { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DictateAnywhere",
    "llama.cpp");

  public static string DefaultModelDirectory { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DictateAnywhere",
    "models",
    "llama-cpp");

  public static string DefaultModelPath { get; } = LlamaCppModelCatalog.GetModelPath(
    LlamaCppModelCatalog.Gemma3FourBModelId);

  public static LlamaCppChatOptions Default { get; } = new(
    ServerExecutablePath: Path.Combine(DefaultRuntimeDirectory, "llama-server.exe"),
    ModelPath: DefaultModelPath,
    ServerBaseAddress: new Uri("http://127.0.0.1:8090/"),
    ContextSize: 8192,
    ThreadCount: Math.Max(1, Environment.ProcessorCount / 2),
    MaxNewTokens: 1536,
    ServerStartupTimeout: TimeSpan.FromMinutes(2),
    RequestTimeout: TimeSpan.FromMinutes(15),
    StartServer: true,
    DisableReasoning: false);

  public static LlamaCppChatOptions ForModel(string modelId)
  {
    LlamaCppModelDefinition definition = LlamaCppModelCatalog.GetRequired(modelId);
    return Default with
    {
      ModelPath = LlamaCppModelCatalog.GetModelPath(definition.ModelId),
      DisableReasoning = definition.DisableReasoning,
    };
  }
}
