using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchChatModelControllerTests
{
  [Xunit.Fact]
  public async Task CheckAsync_ManagedInstalledModel_CombinesAcquisitionAndRuntimeReadiness()
  {
    ChatModelSelection selection = new(ChatProviderIds.GemmaLocal, "managed-chat-model");
    FakeModelManager models = new([
      new ModelInfo(selection.ProviderId, selection.ModelId, "Chat", true, true, ["en"]),
    ]);
    WorkbenchChatModelController controller = new(models, new ReadyRuntimeProbe(), new NoOpDiagnostics());

    WorkbenchChatModelReadinessState state = await controller.CheckAsync(selection);

    Xunit.Assert.True(state.IsInstalled);
    Xunit.Assert.True(state.IsRuntimeReady);
    Xunit.Assert.Equal(100, state.Progress);
    Xunit.Assert.Null(state.Failure);
  }

  [Xunit.Fact]
  public async Task SetupAsync_ManagedMissingModel_DownloadsActivatesAndRepairsRuntime()
  {
    ChatModelSelection selection = new(ChatProviderIds.GemmaLocal, "managed-chat-model");
    FakeModelManager models = new([]);
    ReadyRuntimeProbe runtime = new();
    WorkbenchChatModelController controller = new(models, runtime, new NoOpDiagnostics());

    WorkbenchChatModelSetupResult result = await controller.SetupAsync(
      selection,
      isInstalled: false,
      isRuntimeReady: false);

    Xunit.Assert.True(result.RefreshReadiness);
    Xunit.Assert.Null(result.Failure);
    Xunit.Assert.Equal(selection.ProviderId, models.DownloadedSelection?.ProviderId);
    Xunit.Assert.Equal(selection.ModelId, models.DownloadedSelection?.ModelId);
    Xunit.Assert.Equal(models.DownloadedSelection, models.ActiveSelection);
    Xunit.Assert.Equal(1, runtime.RepairCount);
  }

  private sealed class ReadyRuntimeProbe : IChatRuntimeReadinessProbe
  {
    public int RepairCount { get; private set; }

    public Task<LocalChatRuntimeReadiness> CheckGemmaChatAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(LocalChatRuntimeReadiness.Ready);

    public Task<LocalChatRuntimeReadiness> RepairGemmaChatAsync(
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default)
    {
      RepairCount++;
      return Task.FromResult(LocalChatRuntimeReadiness.Ready);
    }
  }

  private sealed class FakeModelManager : IModelManager
  {
    private readonly IReadOnlyList<ModelInfo> models;
    public FakeModelManager(IReadOnlyList<ModelInfo> models) => this.models = models;
    public TranscriptionModelSelection? ActiveSelection { get; private set; }
    public TranscriptionModelSelection? DownloadedSelection { get; private set; }
    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(models);
    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) => Task.FromResult<ModelInfo?>(null);
    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
    {
      ActiveSelection = selection;
      return Task.CompletedTask;
    }

    public Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
      DownloadedSelection = selection;
      return Task.CompletedTask;
    }

    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
