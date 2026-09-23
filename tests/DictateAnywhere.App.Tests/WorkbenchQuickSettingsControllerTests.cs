using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchQuickSettingsControllerTests
{
  [Xunit.Fact]
  public async Task QueryModelsAsync_KeepsConfiguredMissingModelAndSortsInstalledProviders()
  {
    AppSettings settings = AppSettings.Default.WithConfiguredTranscription(
      TranscriptionProviderIds.CohereLocal,
      "configured-missing");
    FakeModelManager models = new([
      Model(TranscriptionProviderIds.CrisperWhisperLocal, "crisper", installed: true),
      Model(TranscriptionProviderIds.CohereLocal, "configured-missing", installed: false),
      Model("unused", "unused", installed: false),
    ]);
    await using WorkbenchQuickSettingsController controller = new(models, new NoOpDiagnostics());

    WorkbenchTranscriptionModelState state = await controller.QueryModelsAsync(settings);

    Xunit.Assert.Equal(2, state.Options.Count);
    Xunit.Assert.Equal(TranscriptionProviderIds.CohereLocal, state.Options[0].Selection.ProviderId);
    Xunit.Assert.Equal(TranscriptionProviderIds.CrisperWhisperLocal, state.Options[1].Selection.ProviderId);
    Xunit.Assert.Equal("configured-missing", state.Selected?.Selection.ModelId);
    Xunit.Assert.True(state.HasInstalledModel);
    Xunit.Assert.Equal("Selected speech model is not installed.", state.Status);
  }

  private static ModelInfo Model(string providerId, string modelId, bool installed) => new(
    providerId,
    modelId,
    modelId,
    installed,
    IsActive: false,
    SupportedLanguages: ["en"]);

  private sealed class FakeModelManager : IModelManager
  {
    private readonly IReadOnlyList<ModelInfo> models;

    public FakeModelManager(IReadOnlyList<ModelInfo> models)
    {
      this.models = models;
    }

    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(models);
    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) => Task.FromResult<ModelInfo?>(null);
    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
