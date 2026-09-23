using System.Linq;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Composition;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class ProviderSelectionTests
{
  [Xunit.Fact]
  public async Task CreateModelManager_ListsEveryLocalSpeechProvider()
  {
    IModelManager manager = ApplicationComposition.CreateTranscriptionModelManager();

    IReadOnlyList<ModelInfo> models = await manager.GetModelsAsync();

    Xunit.Assert.DoesNotContain(models, model => model.ProviderId == "whisper-local");
    Xunit.Assert.Contains(models, model => model.ProviderId == TranscriptionProviderIds.CohereLocal);
    Xunit.Assert.Contains(models, model => model.ProviderId == TranscriptionProviderIds.CrisperWhisperLocal);
    Xunit.Assert.DoesNotContain(models, model => model.ProviderId == ChatProviderIds.GemmaLocal);
  }

  [Xunit.Fact]
  public async Task DefaultTranscriptionRegistry_AdvertisesExactlyTheManageableModels()
  {
    LocalTranscriptionProviderRegistry registry = LocalTranscriptionProviderRegistry.CreateDefault();
    IModelManager manager = registry.CreateModelManager();

    IReadOnlyList<ModelInfo> manageable = await manager.GetModelsAsync();
    var advertised = registry.GetDefinitions()
      .SelectMany(provider => provider.Models.Select(model => new
      {
        provider.ProviderId,
        model.ModelId,
        model.DisplayName,
      }))
      .OrderBy(model => model.ProviderId)
      .ThenBy(model => model.ModelId)
      .ToArray();
    var actual = manageable
      .Select(model => new
      {
        model.ProviderId,
        model.ModelId,
        model.DisplayName,
      })
      .OrderBy(model => model.ProviderId)
      .ThenBy(model => model.ModelId)
      .ToArray();

    Xunit.Assert.Equal(advertised, actual);
  }

  [Xunit.Fact]
  public async Task CreateChatModelManager_ListsCuratedLlamaCppChatModels()
  {
    IModelManager manager = ApplicationComposition.CreateChatModelManager();

    IReadOnlyList<ModelInfo> models = await manager.GetModelsAsync();

    Xunit.Assert.Equal(3, models.Count);
    Xunit.Assert.All(models, model => Xunit.Assert.Equal(ChatProviderIds.LlamaCppLocal, model.ProviderId));
    Xunit.Assert.Contains(models, model => model.ModelId == "gemma-3-4b-it-Q4_K_M.gguf");
    Xunit.Assert.Contains(models, model => model.ModelId == "Qwen3-1.7B-Q4_K_M.gguf");
    Xunit.Assert.Contains(models, model => model.ModelId == "Qwen3-4B-Q4_K_M.gguf");
  }

  [Xunit.Fact]
  public void ResolveCompatibleLanguage_ClampsUnsupportedCohereLanguage_ToProviderFallback()
  {
    AppSettings settings = AppSettings.Default with
    {
      TranscriptionProviderId = TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId = "cohere-transcribe-03-2026",
      TranscriptionLanguage = "ru",
    };

    string compatibleLanguage = TranscriptionLanguageCompatibilityPolicy.ResolveCompatibleLanguage(settings);

    Xunit.Assert.Equal("en", compatibleLanguage);
  }

  [Xunit.Fact]
  public void LocalChatProviderRegistry_DefaultIncludesCuratedLlamaCppChatModels()
  {
    LocalChatProviderRegistry registry = LocalChatProviderRegistry.CreateDefault();

    LocalChatProviderDefinition llama = Xunit.Assert.Single(
      registry.GetDefinitions().Where(definition => definition.ProviderId == ChatProviderIds.LlamaCppLocal));

    Xunit.Assert.Equal(3, llama.Models.Count);
    Xunit.Assert.Contains(llama.Models, model => model.ModelId == "gemma-3-4b-it-Q4_K_M.gguf");
    Xunit.Assert.Contains(llama.Models, model => model.ModelId == "Qwen3-1.7B-Q4_K_M.gguf");
    Xunit.Assert.Contains(llama.Models, model => model.ModelId == "Qwen3-4B-Q4_K_M.gguf");
    Xunit.Assert.Equal(ModelProviderOperationalKind.LocalOffline, llama.OperationalMetadata.Kind);
  }

  [Xunit.Fact]
  public void DefaultTranscriptionProviders_DeclareLocalOfflineOperationalMetadata()
  {
    LocalTranscriptionProviderRegistry registry = LocalTranscriptionProviderRegistry.CreateDefault();

    foreach (LocalTranscriptionProviderDefinition definition in registry.GetDefinitions())
    {
      Xunit.Assert.False(string.IsNullOrWhiteSpace(definition.Description));
      Xunit.Assert.Equal(ModelProviderOperationalKind.LocalOffline, definition.OperationalMetadata.Kind);
      Xunit.Assert.Equal("local-offline", definition.OperationalMetadata.KindId);
      Xunit.Assert.False(definition.OperationalMetadata.RequiresNetwork);
      Xunit.Assert.False(definition.OperationalMetadata.RequiresCredentials);
      Xunit.Assert.False(definition.OperationalMetadata.ProcessesUserAudioOffDevice);
    }

    LocalTranscriptionProviderDefinition crisperWhisper = Xunit.Assert.Single(
      registry.GetDefinitions().Where(definition =>
        definition.ProviderId == TranscriptionProviderIds.CrisperWhisperLocal));
    Xunit.Assert.NotNull(crisperWhisper.UsageNotice);
    Xunit.Assert.Contains("research-only", crisperWhisper.UsageNotice!, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("timestamps", crisperWhisper.UsageNotice!, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.All(crisperWhisper.Models, model =>
    {
      Xunit.Assert.DoesNotContain(model.IgnorePatterns, pattern =>
        string.Equals(pattern, "*.md", StringComparison.OrdinalIgnoreCase));
      Xunit.Assert.DoesNotContain(model.IgnorePatterns, pattern =>
        string.Equals(pattern, "README.md", StringComparison.OrdinalIgnoreCase));
    });
  }

}
