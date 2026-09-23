using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Tests;

public sealed class RuntimeServiceFactoryTests
{
  [Xunit.Fact]
  public void CreateTranscriptionService_UsesCohereProviderByDefault()
  {
    ITranscriptionService service = RuntimeServiceFactory.CreateTranscriptionService(AppSettings.Default);

    Xunit.Assert.IsType<CohereTranscriptionService>(service);
  }

  [Xunit.Fact]
  public async Task CreateTextTransformationService_UsesBuiltInSpokenCommandTransformer()
  {
    ITextTransformationService service = RuntimeServiceFactory.CreateTextTransformationService(AppSettings.Default);

    TextTransformationResult result = await service.TransformAsync(
      new TextTransformationRequest("hello world", TextTransformationOptions.Default));

    Xunit.Assert.Equal("hello world", result.Text);
  }

  [Xunit.Fact]
  public void CreateTranscriptionService_UsesLocalCohereProvider_WhenConfigured()
  {
    AppSettings settings = AppSettings.Default with
    {
      TranscriptionProviderId = TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId = "cohere-transcribe-03-2026",
    };

    ITranscriptionService service = RuntimeServiceFactory.CreateTranscriptionService(settings);

    Xunit.Assert.IsType<CohereTranscriptionService>(service);
  }

  [Xunit.Fact]
  public void ResolveTranscription_UsesProviderNeutralSelection_WhenConfiguredFieldsArePresent()
  {
    AppSettings settings = AppSettings.Default with
    {
      TranscriptionProviderId = TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId = "cohere-transcribe-03-2026",
    };

    TranscriptionModelSelection selection = RuntimeServiceSelection.ResolveTranscription(settings);

    Xunit.Assert.Equal(TranscriptionProviderIds.CohereLocal, selection.ProviderId);
    Xunit.Assert.Equal("cohere-transcribe-03-2026", selection.ModelId);
  }

  [Xunit.Fact]
  public void CreateTranscriptionService_UsesInjectedProviderRegistry_ForNonDefaultProvider()
  {
    AppSettings settings = AppSettings.Default with
    {
      TranscriptionProviderId = TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId = "cohere-transcribe-03-2026",
    };
    FakeTranscriptionService fakeService = new();
    LocalTranscriptionProviderRegistry registry = new(
    [
      new LocalTranscriptionProviderRegistration(
        new LocalTranscriptionProviderDefinition(
          TranscriptionProviderIds.CohereLocal,
          "Cohere",
          new LocalTranscriptionProviderCapabilities(
            SupportedLanguages: ["en"],
            StreamingMode: TranscriptionStreamingMode.BatchOnly,
            HardwareRequirement: LocalExecutionHardwareRequirement.GpuPreferred,
            SupportsBenchmarking: false),
          Array.Empty<LocalTranscriptionModelDefinition>(),
          Array.Empty<LocalModelResourceRequirement>(),
          ModelProviderOperationalMetadata.LocalOffline),
        (_, _) => fakeService,
        () => throw new InvalidOperationException("Model management is not used by this test.")),
    ]);

    ITranscriptionService service = RuntimeServiceFactory.CreateTranscriptionService(settings, registry);

    Xunit.Assert.Same(fakeService, service);
  }

  private sealed class FakeTranscriptionService : ITranscriptionService
  {
    public Task<TranscriptionResult> TranscribeAsync(
      AudioCaptureResult audio,
      string modelId,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return Task.FromResult(new TranscriptionResult("text", modelId, TimeSpan.Zero));
    }
  }

}
