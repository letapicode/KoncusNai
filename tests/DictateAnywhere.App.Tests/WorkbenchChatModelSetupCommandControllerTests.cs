using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchChatModelSetupCommandControllerTests
{
  [Xunit.Fact]
  public async Task SetupAsync_OwnsSetupResetReadinessAndOperationLifetime()
  {
    ChatModelSelection selection = new(ChatProviderIds.GemmaLocal, "managed-chat-model");
    MutableModelManager models = new();
    WorkbenchChatModelController model = new(models, new ReadyRuntimeProbe(), new NoOpDiagnostics());
    await using WorkbenchChatController chat = new(_ => new UnusedChatService());
    chat.SelectModel(selection);
    WorkbenchChatSendController sender = new(
      chat,
      model.CheckAsync,
      (_, record, _) => Task.FromResult(Saved(record)),
      new NoOpDiagnostics());
    WorkbenchChatModelSetupCommandController command = new(chat, model, sender, new NoOpDiagnostics());
    RecordingProgress progress = new();
    int startedCount = 0;

    WorkbenchChatModelSetupCommandResult result = await command.SetupAsync(
      progress,
      operationStarted: () => startedCount++);

    Xunit.Assert.True(result.OperationAccepted);
    Xunit.Assert.Equal(1, startedCount);
    Xunit.Assert.True(models.Installed);
    Xunit.Assert.True(chat.IsModelInstalled);
    Xunit.Assert.True(chat.IsRuntimeReady);
    Xunit.Assert.False(chat.IsBusy);
    Xunit.Assert.NotEmpty(progress.Updates);
  }

  [Xunit.Fact]
  public async Task SetupAsync_ExpectedManagementFailureReturnsConciseStatus()
  {
    ChatModelSelection selection = new(ChatProviderIds.GemmaLocal, "managed-chat-model");
    WorkbenchChatModelController model = new(
      new FailingModelManager(),
      new ReadyRuntimeProbe(),
      new NoOpDiagnostics());
    await using WorkbenchChatController chat = new(_ => new UnusedChatService());
    chat.SelectModel(selection);
    WorkbenchChatSendController sender = new(
      chat,
      model.CheckAsync,
      (_, record, _) => Task.FromResult(Saved(record)),
      new NoOpDiagnostics());
    WorkbenchChatModelSetupCommandController command = new(chat, model, sender, new NoOpDiagnostics());

    WorkbenchChatModelSetupCommandResult result = await command.SetupAsync();

    Xunit.Assert.True(result.OperationAccepted);
    Xunit.Assert.Equal("Model setup failed. See Diagnostics.", result.StatusMessage);
    Xunit.Assert.DoesNotContain("deliberately verbose", result.StatusMessage, StringComparison.Ordinal);
    Xunit.Assert.False(chat.IsBusy);
  }

  private static WorkbenchHistoryMutationResult Saved(ChatHistoryRecord record) => new(
    HistoryCommandStatus.Succeeded,
    "Chat saved.",
    ChatRecord: record,
    AffectedCount: 1);

  private sealed class RecordingProgress : IProgress<WorkbenchChatModelSetupProgress>
  {
    public List<WorkbenchChatModelSetupProgress> Updates { get; } = [];
    public void Report(WorkbenchChatModelSetupProgress value) => Updates.Add(value);
  }

  private sealed class ReadyRuntimeProbe : IChatRuntimeReadinessProbe
  {
    public Task<LocalChatRuntimeReadiness> CheckGemmaChatAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(LocalChatRuntimeReadiness.Ready);

    public Task<LocalChatRuntimeReadiness> RepairGemmaChatAsync(
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(LocalChatRuntimeReadiness.Ready);
  }

  private sealed class MutableModelManager : IModelManager
  {
    public bool Installed { get; private set; }

    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<ModelInfo>>(Installed
        ? [new ModelInfo(ChatProviderIds.GemmaLocal, "managed-chat-model", "Chat", true, true, ["en"])]
        : []);

    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) =>
      Task.FromResult<ModelInfo?>(null);

    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task DownloadModelAsync(
      TranscriptionModelSelection selection,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default)
    {
      Installed = true;
      return Task.CompletedTask;
    }

    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;
  }

  private sealed class FailingModelManager : IModelManager
  {
    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<ModelInfo>>([]);

    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) =>
      Task.FromResult<ModelInfo?>(null);

    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task DownloadModelAsync(
      TranscriptionModelSelection selection,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default) =>
      Task.FromException(new IOException("deliberately verbose internal transport failure"));

    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;
  }

  private sealed class UnusedChatService : IChatCompletionService
  {
    public Task<ChatCompletionResult> CompleteAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default) =>
      throw new InvalidOperationException("Chat completion must not be called.");
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
