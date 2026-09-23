using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed record WorkbenchChatModelReadinessState(
  bool IsInstalled,
  bool IsRuntimeReady,
  double Progress,
  string Detail,
  string Status,
  Exception? Failure = null);

internal sealed record WorkbenchChatModelSetupProgress(
  string Status,
  double? Completion = null);

internal sealed record WorkbenchChatModelSetupResult(
  string Status,
  bool RefreshReadiness,
  Exception? Failure = null);

/// <summary>Owns provider-specific chat-model readiness checks and maps them to one Workbench contract.</summary>
internal sealed class WorkbenchChatModelController
{
  private readonly IModelManager modelManager;
  private readonly IChatRuntimeReadinessProbe runtimeReadinessProbe;
  private readonly IDiagnostics diagnostics;

  public WorkbenchChatModelController(
    IModelManager modelManager,
    IChatRuntimeReadinessProbe runtimeReadinessProbe,
    IDiagnostics diagnostics)
  {
    this.modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
    this.runtimeReadinessProbe = runtimeReadinessProbe ?? throw new ArgumentNullException(nameof(runtimeReadinessProbe));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public async Task<WorkbenchChatModelSetupResult> SetupAsync(
    ChatModelSelection selection,
    bool isInstalled,
    bool isRuntimeReady,
    IProgress<WorkbenchChatModelSetupProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);
    ChatModelSelection normalized = selection.Normalize();
    try
    {
      if (string.Equals(normalized.ProviderId, ChatProviderIds.OllamaLocal, StringComparison.OrdinalIgnoreCase))
      {
        return await SetupOllamaAsync(normalized, progress, cancellationToken).ConfigureAwait(true);
      }

      if (string.Equals(normalized.ProviderId, ChatProviderIds.LlamaCppLocal, StringComparison.OrdinalIgnoreCase))
      {
        return await SetupLlamaCppAsync(normalized, progress, cancellationToken).ConfigureAwait(true);
      }

      return await SetupManagedAsync(
          normalized,
          isInstalled,
          isRuntimeReady,
          progress,
          cancellationToken)
        .ConfigureAwait(true);
    }
    catch (Exception ex) when (ex is IOException
                               or UnauthorizedAccessException
                               or InvalidOperationException
                               or HttpRequestException
                               || IsModelManagementException(ex))
    {
      diagnostics.Warning($"Local chat model setup failed: {ex.Message}");
      return new WorkbenchChatModelSetupResult("Model setup failed. See Diagnostics.", RefreshReadiness: false, ex);
    }
  }

  public async Task<WorkbenchChatModelReadinessState> CheckAsync(
    ChatModelSelection selection,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);
    ChatModelSelection normalized = selection.Normalize();
    try
    {
      if (string.Equals(normalized.ProviderId, ChatProviderIds.OllamaLocal, StringComparison.OrdinalIgnoreCase))
      {
        LocalChatRuntimeReadiness readiness = await LocalOllamaRuntimeReadiness
          .CheckAsync(normalized.ModelId, cancellationToken)
          .ConfigureAwait(true);
        return FromProviderReadiness(readiness, "Ready in Ollama.", "Start Ollama or pull Gemma 4.");
      }

      if (string.Equals(normalized.ProviderId, ChatProviderIds.LlamaCppLocal, StringComparison.OrdinalIgnoreCase))
      {
        LocalChatRuntimeReadiness readiness = await LocalLlamaCppRuntimeReadiness
          .CheckAsync(normalized.ModelId, cancellationToken)
          .ConfigureAwait(true);
        return FromProviderReadiness(readiness, "Ready on this device.", "Local setup needed.");
      }

      IReadOnlyList<ModelInfo> models = await modelManager.GetModelsAsync(cancellationToken).ConfigureAwait(true);
      ModelInfo? model = models.FirstOrDefault(candidate =>
        string.Equals(candidate.ProviderId, normalized.ProviderId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.ModelId, normalized.ModelId, StringComparison.OrdinalIgnoreCase));
      if (model?.IsInstalled != true)
      {
        return new WorkbenchChatModelReadinessState(
          IsInstalled: false,
          IsRuntimeReady: false,
          Progress: 0,
          Detail: "Not downloaded.",
          Status: "Download the selected chat model before sending.");
      }

      LocalChatRuntimeReadiness runtime = await runtimeReadinessProbe
        .CheckGemmaChatAsync(cancellationToken)
        .ConfigureAwait(true);
      return new WorkbenchChatModelReadinessState(
        IsInstalled: true,
        IsRuntimeReady: runtime.IsReady,
        Progress: runtime.IsReady ? 100 : 55,
        Detail: runtime.IsReady ? "Ready on this device." : "Runtime needs update.",
        Status: runtime.IsReady ? "Selected chat model is ready." : runtime.StatusMessage);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException || IsModelManagementException(ex))
    {
      return new WorkbenchChatModelReadinessState(
        IsInstalled: false,
        IsRuntimeReady: false,
        Progress: 0,
        Detail: "Model status unavailable.",
        Status: "Could not check chat model status. See Diagnostics.",
        ex);
    }
  }

  private static WorkbenchChatModelReadinessState FromProviderReadiness(
    LocalChatRuntimeReadiness readiness,
    string readyDetail,
    string unavailableDetail) => new(
      readiness.IsReady,
      readiness.IsReady,
      readiness.IsReady ? 100 : 0,
      readiness.IsReady ? readyDetail : unavailableDetail,
      readiness.StatusMessage);

  private async Task<WorkbenchChatModelSetupResult> SetupOllamaAsync(
    ChatModelSelection selection,
    IProgress<WorkbenchChatModelSetupProgress>? progress,
    CancellationToken cancellationToken)
  {
    diagnostics.Info("Ollama chat model setup requested from the Workbench.");
    progress?.Report(new WorkbenchChatModelSetupProgress(
      "Starting Ollama locally. The model stays unloaded until the first message."));
    Progress<double> transferProgress = CreateTransferProgress(progress, "Setting up Ollama...");
    LocalChatRuntimeReadiness readiness = await LocalOllamaRuntimeReadiness
      .StartAsync(selection.ModelId, transferProgress, cancellationToken)
      .ConfigureAwait(true);
    if (!readiness.IsReady)
    {
      progress?.Report(new WorkbenchChatModelSetupProgress("Downloading the selected Ollama model. This is only needed once."));
      readiness = await LocalOllamaRuntimeReadiness
        .PullAsync(selection.ModelId, transferProgress, cancellationToken)
        .ConfigureAwait(true);
    }

    diagnostics.Info($"Ollama model setup completed: {readiness.StatusMessage}");
    return new WorkbenchChatModelSetupResult(readiness.StatusMessage, RefreshReadiness: true);
  }

  private async Task<WorkbenchChatModelSetupResult> SetupLlamaCppAsync(
    ChatModelSelection selection,
    IProgress<WorkbenchChatModelSetupProgress>? progress,
    CancellationToken cancellationToken)
  {
    diagnostics.Info("llama.cpp chat model setup requested from the Workbench.");
    progress?.Report(new WorkbenchChatModelSetupProgress("Downloading the selected local chat model..."));
    Progress<double> transferProgress = CreateTransferProgress(progress, "Downloading the selected local chat model...");
    LocalChatRuntimeReadiness readiness = await LocalLlamaCppRuntimeReadiness
      .ProvisionAsync(selection.ModelId, transferProgress, cancellationToken)
      .ConfigureAwait(true);
    diagnostics.Info($"llama.cpp chat model setup completed: {readiness.StatusMessage}");
    return new WorkbenchChatModelSetupResult(readiness.StatusMessage, RefreshReadiness: true);
  }

  private async Task<WorkbenchChatModelSetupResult> SetupManagedAsync(
    ChatModelSelection selection,
    bool isInstalled,
    bool isRuntimeReady,
    IProgress<WorkbenchChatModelSetupProgress>? progress,
    CancellationToken cancellationToken)
  {
    bool shouldDownload = !isInstalled;
    bool shouldRepairRuntime = isInstalled && !isRuntimeReady;
    Progress<double> transferProgress = CreateTransferProgress(
      progress,
      shouldDownload ? "Downloading the selected chat model..." : "Updating the local model runtime...");
    progress?.Report(new WorkbenchChatModelSetupProgress(
      shouldDownload ? "Downloading the selected chat model..." : "Updating the local model runtime..."));
    TranscriptionModelSelection modelSelection = new(selection.ProviderId, selection.ModelId);
    if (shouldDownload)
    {
      await modelManager.DownloadModelAsync(modelSelection, transferProgress, cancellationToken).ConfigureAwait(true);
      await modelManager.SetActiveModelAsync(modelSelection, cancellationToken).ConfigureAwait(true);
    }

    if (shouldDownload || shouldRepairRuntime)
    {
      progress?.Report(new WorkbenchChatModelSetupProgress("Preparing the local model runtime..."));
      LocalChatRuntimeReadiness readiness = await runtimeReadinessProbe
        .RepairGemmaChatAsync(transferProgress, cancellationToken)
        .ConfigureAwait(true);
      if (!readiness.IsReady)
      {
        return new WorkbenchChatModelSetupResult(readiness.StatusMessage, RefreshReadiness: false);
      }
    }

    return new WorkbenchChatModelSetupResult("The selected chat model is ready on this device.", RefreshReadiness: true);
  }

  private static Progress<double> CreateTransferProgress(
    IProgress<WorkbenchChatModelSetupProgress>? progress,
    string status) => new(value => progress?.Report(new WorkbenchChatModelSetupProgress(
      status,
      Math.Clamp(value, 0, 1))));

  private static bool IsModelManagementException(Exception exception) =>
    string.Equals(exception.GetType().Name, "ModelManagementException", StringComparison.Ordinal);
}
