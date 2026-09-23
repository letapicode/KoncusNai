using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Models;

namespace DictateAnywhere.Models.Tests;

public sealed class CompositeModelManagerTests
{
  [Xunit.Fact]
  public async Task GetModelsAsync_AggregatesRegisteredProviderModels()
  {
    FakeProviderModelManager crisper = new(
      TranscriptionProviderIds.CrisperWhisperLocal,
      [
        new ModelInfo(TranscriptionProviderIds.CrisperWhisperLocal, "crisperwhisper-2-turbo", "CrisperWhisper Turbo", true, true, ["en"]),
      ]);
    FakeProviderModelManager cohere = new(
      TranscriptionProviderIds.CohereLocal,
      [
        new ModelInfo(TranscriptionProviderIds.CohereLocal, "cohere-transcribe-03-2026", "Cohere", false, false, ["en"]),
      ]);
    CompositeModelManager manager = new([crisper, cohere]);

    IReadOnlyList<ModelInfo> models = await manager.GetModelsAsync();

    Xunit.Assert.Equal(2, models.Count);
    Xunit.Assert.Collection(
      models,
      model => Xunit.Assert.Equal(TranscriptionProviderIds.CohereLocal, model.ProviderId),
      model => Xunit.Assert.Equal(TranscriptionProviderIds.CrisperWhisperLocal, model.ProviderId));
  }

  [Xunit.Fact]
  public async Task SetActiveModelAsync_RoutesSelectionToMatchingProvider()
  {
    FakeProviderModelManager crisper = new(
      TranscriptionProviderIds.CrisperWhisperLocal,
      [
        new ModelInfo(TranscriptionProviderIds.CrisperWhisperLocal, "crisperwhisper-2-turbo", "CrisperWhisper Turbo", true, false, ["en"]),
      ]);
    FakeProviderModelManager cohere = new(
      TranscriptionProviderIds.CohereLocal,
      [
        new ModelInfo(TranscriptionProviderIds.CohereLocal, "cohere-transcribe-03-2026", "Cohere", true, false, ["en"]),
      ]);
    CompositeModelManager manager = new([crisper, cohere]);

    await manager.SetActiveModelAsync(new TranscriptionModelSelection(TranscriptionProviderIds.CohereLocal, "cohere-transcribe-03-2026"));

    Xunit.Assert.Null(crisper.LastActivatedModelId);
    Xunit.Assert.Equal("cohere-transcribe-03-2026", cohere.LastActivatedModelId);
  }

  [Xunit.Fact]
  public async Task GetActiveModelAsync_ReturnsNull_WhenProviderIsUnregistered()
  {
    FakeProviderModelManager crisper = new(
      TranscriptionProviderIds.CrisperWhisperLocal,
      [
        new ModelInfo(TranscriptionProviderIds.CrisperWhisperLocal, "crisperwhisper-2-turbo", "CrisperWhisper Turbo", true, true, ["en"]),
      ]);
    CompositeModelManager manager = new([crisper]);

    ModelInfo? active = await manager.GetActiveModelAsync(TranscriptionProviderIds.CohereLocal);

    Xunit.Assert.Null(active);
  }

  [Xunit.Fact]
  public async Task DownloadModelAsync_ThrowsForUnknownProvider()
  {
    FakeProviderModelManager crisper = new(
      TranscriptionProviderIds.CrisperWhisperLocal,
      [
        new ModelInfo(TranscriptionProviderIds.CrisperWhisperLocal, "crisperwhisper-2-turbo", "CrisperWhisper Turbo", true, true, ["en"]),
      ]);
    CompositeModelManager manager = new([crisper]);

    ModelManagementException exception = await Xunit.Assert.ThrowsAsync<ModelManagementException>(() =>
      manager.DownloadModelAsync(new TranscriptionModelSelection(TranscriptionProviderIds.CohereLocal, "cohere-transcribe-03-2026")));

    Xunit.Assert.Contains("cohere-local", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  private sealed class FakeProviderModelManager : IProviderModelManager
  {
    private readonly IReadOnlyList<ModelInfo> models;

    public FakeProviderModelManager(string providerId, IReadOnlyList<ModelInfo> models)
    {
      ProviderId = providerId;
      this.models = models;
    }

    public string ProviderId { get; }

    public string? LastActivatedModelId { get; private set; }

    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
      return Task.FromResult(models);
    }

    public Task<ModelInfo?> GetActiveModelAsync(CancellationToken cancellationToken = default)
    {
      return Task.FromResult(models.FirstOrDefault(model => model.IsActive));
    }

    public Task SetActiveModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
      LastActivatedModelId = modelId;
      return Task.CompletedTask;
    }

    public Task DownloadModelAsync(string modelId, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
      LastActivatedModelId = modelId;
      progress?.Report(1.0);
      return Task.CompletedTask;
    }

    public Task DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
      LastActivatedModelId = modelId;
      return Task.CompletedTask;
    }
  }
}
