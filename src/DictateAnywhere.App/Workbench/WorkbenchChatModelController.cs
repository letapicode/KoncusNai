using DictateAnywhere.Core.Services;
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
  Exception? Failure = null,
  bool IsInstallationKnown = true);

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
  private readonly Func<OllamaListenerIdentity, bool> approveExternalOllama;

  public WorkbenchChatModelController(
    IModelManager modelManager,
    IChatRuntimeReadinessProbe runtimeReadinessProbe,
    IDiagnostics diagnostics,
    Func<OllamaListenerIdentity, bool>? approveExternalOllama = null)
  {
    this.modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
    this.runtimeReadinessProbe = runtimeReadinessProbe ?? throw new ArgumentNullException(nameof(runtimeReadinessProbe));
    this.diagnostics = DiagnosticBoundary.Wrap(diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
    this.approveExternalOllama = approveExternalOllama ?? (_ => false);
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
      DiagnosticBoundary.Report(() => diagnostics.Warning($"Local chat model setup failed: {ex.Message}"));
      return new WorkbenchChatModelSetupResult("Model setup failed. See Diagnostics.", RefreshReadiness: false, ex);
    }
  }

  public async Task<WorkbenchChatModelReadinessState> CheckAsync(
    ChatModelSelection selection,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);
    ChatModelSelection normalized = selection.Normalize();
    bool? discoveredInstallation = null;
    try
    {
      if (string.Equals(normalized.ProviderId, ChatProviderIds.OllamaLocal, StringComparison.OrdinalIgnoreCase))
      {
        LocalChatRuntimeReadiness readiness = await LocalOllamaRuntimeReadiness
          .CheckAsync(normalized.ModelId, cancellationToken)
          .ConfigureAwait(true);
        if (readiness.IsReady && !OllamaListenerTrust.IsCurrentTrusted())
          return new WorkbenchChatModelReadinessState(true, false, 0,
            "Ollama listener needs review.",
            "Review the running Ollama service from model setup before sending private chat content.");
        return FromProviderReadiness(readiness, "Ready in Ollama.", "Start Ollama or pull Gemma 4.");
      }

      if (string.Equals(normalized.ProviderId, ChatProviderIds.LlamaCppLocal, StringComparison.OrdinalIgnoreCase))
      {
        LocalChatRuntimeReadiness readiness = await LocalLlamaCppRuntimeReadiness
          .CheckAsync(normalized.ModelId, cancellationToken)
          .ConfigureAwait(true);
        return FromProviderReadiness(readiness, "Ready on this device.", "Local setup needed.");
      }

      IReadOnlyList<ModelInfo> models = await Task.Run(() => modelManager.GetModelsAsync(cancellationToken),
        cancellationToken).ConfigureAwait(true);
      ModelInfo? model = models.FirstOrDefault(candidate =>
        string.Equals(candidate.ProviderId, normalized.ProviderId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.ModelId, normalized.ModelId, StringComparison.OrdinalIgnoreCase));
      discoveredInstallation = model?.IsInstalled == true;
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
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
      or HttpRequestException or System.Text.Json.JsonException
      || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested) || IsModelManagementException(ex))
    {
      return new WorkbenchChatModelReadinessState(
        IsInstalled: discoveredInstallation == true,
        IsRuntimeReady: false,
        Progress: 0,
        Detail: "Model status unavailable.",
        Status: "Could not check chat model status. See Diagnostics.",
        ex,
        IsInstallationKnown: discoveredInstallation.HasValue);
    }
  }

  private static WorkbenchChatModelReadinessState FromProviderReadiness(
    LocalChatRuntimeReadiness readiness,
    string readyDetail,
    string unavailableDetail) => new(
      readiness.IsInstalled ?? readiness.IsReady,
      readiness.IsReady,
      readiness.IsReady ? 100 : 0,
      readiness.IsReady ? readyDetail : unavailableDetail,
      readiness.StatusMessage,
      IsInstallationKnown: readiness.IsInstalled.HasValue || readiness.IsReady);

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
    if (!OllamaListenerTrust.IsCurrentTrusted())
    {
      if (!OllamaListenerTrust.TryCapture(out OllamaListenerIdentity? identity) || identity is null)
        return new WorkbenchChatModelSetupResult(
          "Could not verify the process serving Ollama on this device. Close it and retry.", RefreshReadiness: false);
      if (!approveExternalOllama(identity))
        return new WorkbenchChatModelSetupResult(
          "The external Ollama listener was not trusted. No private chat content was sent.", RefreshReadiness: false);
      if (!OllamaListenerTrust.ApproveExternal(identity))
        return new WorkbenchChatModelSetupResult(
          "The Ollama listener changed during review. Retry model setup.", RefreshReadiness: false);
    }
    if (!readiness.IsReady && readiness.IsInstalled == false)
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
    LocalChatRuntimeReadiness current = await LocalLlamaCppRuntimeReadiness
      .CheckAsync(selection.ModelId, cancellationToken).ConfigureAwait(true);
    if (current.IsReady)
      return new WorkbenchChatModelSetupResult(current.StatusMessage, RefreshReadiness: true);
    string status = current.IsInstalled == true
      ? "Repairing the selected local chat model / runtime..."
      : "Downloading the selected local chat model...";
    progress?.Report(new WorkbenchChatModelSetupProgress(status));
    Progress<double> transferProgress = CreateTransferProgress(progress, status);
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
    WorkbenchChatModelReadinessState current = await CheckAsync(selection, cancellationToken).ConfigureAwait(true);
    if (!current.IsInstallationKnown)
      return new WorkbenchChatModelSetupResult(current.Status, RefreshReadiness: true, current.Failure);
    isInstalled = current.IsInstalled;
    isRuntimeReady = current.IsRuntimeReady;
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
