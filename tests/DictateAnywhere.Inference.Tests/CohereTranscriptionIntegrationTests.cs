using System;
using System.IO;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

[Xunit.Trait("Category", "ModelIntegration")]
public sealed class CohereTranscriptionIntegrationTests
{
  private const string EnableIntegrationTestsEnvironmentVariable = "DICTATEANYWHERE_RUN_COHERE_INTEGRATION_TESTS";
  private const string ModelId = "cohere-transcribe-03-2026";

  [CohereIntegrationFact]
  public async Task WarmUpAsync_ValidatesInstalledModelRuntime_WhenExplicitlyEnabled()
  {
    string modelPath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models",
      TranscriptionProviderIds.CohereLocal,
      ModelId);
    if (!Directory.Exists(modelPath))
    {
      throw new InvalidOperationException($"Cohere integration tests are enabled, but model path '{modelPath}' does not exist.");
    }

    await using CohereTranscriptionService service = new(CohereTranscriptionOptions.Default);
    await service.WarmUpAsync(ModelId);
  }

  internal static bool IsEnabledEnvironment()
  {
    return string.Equals(
      Environment.GetEnvironmentVariable(EnableIntegrationTestsEnvironmentVariable),
      "1",
      StringComparison.Ordinal);
  }
}

internal sealed class CohereIntegrationFactAttribute : Xunit.FactAttribute
{
  public CohereIntegrationFactAttribute()
  {
    if (!CohereTranscriptionIntegrationTests.IsEnabledEnvironment())
    {
      Skip = "Set DICTATEANYWHERE_RUN_COHERE_INTEGRATION_TESTS=1 after provisioning the local Cohere model to run this integration test.";
    }
  }
}
