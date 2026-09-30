using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection("Exclusive audio capture")]
public sealed class ModelReadinessCoordinatorTests
{
  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public async Task Preparation_ReleasesOldModelAndRestoresBothExistingFlowFacades(bool cancel)
  {
    FakeModelManager manager = new([new(TranscriptionProviderIds.CohereLocal, "cohere-transcribe-03-2026", "Cohere", true, true, ["en"])]);
    List<FakeTranscriptionModel> generations = new();
    RecordingDiagnostics diagnostics = new();
    await using ModelReadinessCoordinator coordinator = new(manager, diagnostics, (_, _) =>
    {
      FakeTranscriptionModel model = new(TranscriptionProviderIds.CohereLocal);
      generations.Add(model);
      return new TranscriptionModelRegistry([model]);
    }, (_, _) => throw new InvalidOperationException("Unexpected owned worker"));
    await coordinator.RefreshAsync(AppSettings.Default);
    ITranscriptionService hotkey = coordinator.CreateTranscriptionService(AppSettings.Default, diagnostics);
    ITranscriptionService workbench = coordinator.CreateTranscriptionService(AppSettings.Default, diagnostics);
    AudioCaptureResult audio = new([0, 0], 16_000, TimeSpan.FromMilliseconds(1));
    Task<TranscriptionResult>? queued = null;
    async Task Preparation()
    {
      Xunit.Assert.True(generations[0].IsDisposed);
      Xunit.Assert.Throws<InvalidOperationException>(() => coordinator.CreateTranscriptionService(AppSettings.Default, diagnostics));
      queued = hotkey.TranscribeAsync(audio, "cohere-transcribe-03-2026");
      Xunit.Assert.False(queued.IsCompleted);
      await Task.Yield();
      if (cancel) throw new OperationCanceledException();
    }
    if (cancel) await Xunit.Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.PrepareRuntimeAsync(Preparation, CancellationToken.None));
    else await coordinator.PrepareRuntimeAsync(Preparation, CancellationToken.None);
    await queued!;
    await hotkey.TranscribeAsync(audio, "cohere-transcribe-03-2026");
    await workbench.TranscribeAsync(audio, "cohere-transcribe-03-2026");
    Xunit.Assert.Equal(2, generations.Count);
    Xunit.Assert.Equal(3, generations[1].TranscriptionCount);
  }
  [Xunit.Fact]
  public async Task CreateTranscriptionService_UsesSameRegistryAsReadinessWarmup()
  {
    await using FakeTranscriptionModel fakeModel = new(TranscriptionProviderIds.CohereLocal);
    FakeModelManager modelManager = new([
      new ModelInfo(
        TranscriptionProviderIds.CohereLocal,
        "cohere-transcribe-03-2026",
        "Cohere Transcribe",
        IsInstalled: true,
        IsActive: true,
        SupportedLanguages: ["en"]),
    ]);
    RecordingDiagnostics diagnostics = new();
    await using ModelReadinessCoordinator coordinator = new(
      modelManager,
      diagnostics,
      (_, _) => new TranscriptionModelRegistry([fakeModel]),
      (_, _) => throw new InvalidOperationException("Fallback should not be used after readiness initialization."));

    await coordinator.RefreshAsync(AppSettings.Default);
    await WaitForReadinessAsync(coordinator, ModelReadinessState.Ready);

    ITranscriptionService service = coordinator.CreateTranscriptionService(AppSettings.Default, diagnostics);
    try
    {
      TranscriptionResult result = await service.TranscribeAsync(
        new AudioCaptureResult([0, 0], 16_000, TimeSpan.FromMilliseconds(1)),
        "cohere-transcribe-03-2026");

      Xunit.Assert.Equal("fake transcript", result.Text);
      Xunit.Assert.Equal(1, fakeModel.WarmupCount);
      Xunit.Assert.Equal(1, fakeModel.TranscriptionCount);
      Xunit.Assert.Contains(
        coordinator.CurrentSnapshot.Entries,
        entry => entry.State == ModelReadinessState.Ready && entry.ModelId == "cohere-transcribe-03-2026");
    }
    finally
    {
      if (service is IAsyncDisposable disposable)
      {
        await disposable.DisposeAsync();
      }
    }
  }

  [Xunit.Fact]
  public async Task RefreshAsync_WarmsOnlyConfiguredTranscriptionProvider()
  {
    await using FakeTranscriptionModel crisperWhisper = new(TranscriptionProviderIds.CrisperWhisperLocal);
    await using FakeTranscriptionModel cohere = new(TranscriptionProviderIds.CohereLocal);
    FakeModelManager modelManager = new([
      new ModelInfo(
        TranscriptionProviderIds.CrisperWhisperLocal,
        "crisperwhisper-2-turbo",
        "CrisperWhisper Turbo",
        IsInstalled: true,
        IsActive: true,
        SupportedLanguages: ["en"]),
      new ModelInfo(
        TranscriptionProviderIds.CohereLocal,
        "cohere-transcribe-03-2026",
        "Cohere Transcribe",
        IsInstalled: true,
        IsActive: true,
        SupportedLanguages: ["en"]),
    ]);
    RecordingDiagnostics diagnostics = new();
    AppSettings settings = CrisperWhisperLicensePolicy.AcceptCurrentVersion(
      AppSettings.Default.WithConfiguredTranscription(
        TranscriptionProviderIds.CrisperWhisperLocal,
        "crisperwhisper-2-turbo"));
    await using ModelReadinessCoordinator coordinator = new(
      modelManager,
      diagnostics,
      (_, _) => new TranscriptionModelRegistry([crisperWhisper, cohere]),
      (_, _) => throw new InvalidOperationException("Fallback should not be used after readiness initialization."));

    await coordinator.RefreshAsync(settings);
    await WaitForConditionAsync(() => crisperWhisper.WarmupCount == 1);

    Xunit.Assert.Equal(1, crisperWhisper.WarmupCount);
    Xunit.Assert.Equal(0, cohere.WarmupCount);
    Xunit.Assert.Contains(
      coordinator.CurrentSnapshot.Entries,
      entry =>
        entry.ProviderId == TranscriptionProviderIds.CohereLocal
        && entry.State == ModelReadinessState.Ready);
  }

  private static async Task WaitForReadinessAsync(
    ModelReadinessCoordinator coordinator,
    ModelReadinessState expectedState)
  {
    DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
    while (DateTimeOffset.UtcNow < deadline)
    {
      if (coordinator.CurrentSnapshot.Entries.Any(entry => entry.State == expectedState))
      {
        return;
      }

      await Task.Delay(25);
    }

    Xunit.Assert.Contains(coordinator.CurrentSnapshot.Entries, entry => entry.State == expectedState);
  }

  private static async Task WaitForConditionAsync(Func<bool> condition)
  {
    DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
    while (DateTimeOffset.UtcNow < deadline)
    {
      if (condition())
      {
        return;
      }

      await Task.Delay(25);
    }

    Xunit.Assert.True(condition());
  }

  private sealed class FakeModelManager : IModelManager
  {
    private readonly IReadOnlyList<ModelInfo> models;

    public FakeModelManager(IReadOnlyList<ModelInfo> models)
    {
      this.models = models;
    }

    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
      return Task.FromResult(models);
    }

    public Task<ModelInfo?> GetActiveModelAsync(
      string providerId,
      CancellationToken cancellationToken = default)
    {
      return Task.FromResult(models.FirstOrDefault(model =>
        model.IsActive && string.Equals(model.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));
    }

    public Task SetActiveModelAsync(
      TranscriptionModelSelection selection,
      CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }

    public Task DownloadModelAsync(
      TranscriptionModelSelection selection,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default)
    {
      progress?.Report(1.0);
      return Task.CompletedTask;
    }

    public Task DeleteModelAsync(
      TranscriptionModelSelection selection,
      CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }
  }

  private sealed class FakeTranscriptionModel : ITranscriptionModel, ITranscriptionModelWarmup, IAsyncDisposable
  {
    public FakeTranscriptionModel(string providerId)
    {
      ProviderId = providerId;
    }

    public string ProviderId { get; }

    public int WarmupCount { get; private set; }

    public int TranscriptionCount { get; private set; }
    public bool IsDisposed { get; private set; }
    public ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }

    public Task WarmUpAsync(string modelId, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      WarmupCount++;
      return Task.CompletedTask;
    }

    public Task<TranscriptionResult> TranscribeAsync(
      AudioCaptureResult audio,
      string modelId,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      TranscriptionCount++;
      return Task.FromResult(new TranscriptionResult("fake transcript", modelId, TimeSpan.FromMilliseconds(1)));
    }
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}
