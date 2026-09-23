using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed record TranscriptionModelOptionViewModel(
  TranscriptionModelSelection Selection,
  string Label,
  string Description,
  bool IsInstalled)
{
  public static TranscriptionModelOptionViewModel FromModelInfo(ModelInfo model, bool isConfigured)
  {
    ArgumentNullException.ThrowIfNull(model);
    string state = isConfigured
      ? model.IsInstalled ? "active" : "not installed"
      : model.IsInstalled ? "installed" : "not installed";
    return new TranscriptionModelOptionViewModel(
      new TranscriptionModelSelection(model.ProviderId, model.ModelId).Normalize(),
      model.DisplayName,
      $"{model.ProviderId}/{model.ModelId} - {state}",
      model.IsInstalled);
  }

  public override string ToString() => Label;
}

internal sealed record WorkbenchTranscriptionModelState(
  IReadOnlyList<TranscriptionModelOptionViewModel> Options,
  TranscriptionModelOptionViewModel? Selected,
  bool HasInstalledModel,
  string Status);

/// <summary>Owns Workbench quick-settings model queries, latest-request arbitration, and option policy.</summary>
internal sealed class WorkbenchQuickSettingsController : IAsyncDisposable
{
  private readonly IModelManager modelManager;
  private readonly IDiagnostics diagnostics;
  private readonly LatestOperationSession refreshSession = new();

  public WorkbenchQuickSettingsController(IModelManager modelManager, IDiagnostics diagnostics)
  {
    this.modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public Task<WorkbenchTranscriptionModelState> QueryModelsAsync(
    AppSettings settings,
    CancellationToken cancellationToken = default) =>
    QueryCoreAsync(settings, cancellationToken);

  public async Task<WorkbenchTranscriptionModelState?> QueryLatestModelsAsync(
    AppSettings settings,
    CancellationToken cancellationToken = default)
  {
    LatestOperationResult<WorkbenchTranscriptionModelState> result = await refreshSession
      .RunLatestAsync(token => QueryCoreAsync(settings, token), cancellationToken)
      .ConfigureAwait(true);
    return result.Completed ? result.Value : null;
  }

  public void CancelLatestQuery() => refreshSession.Cancel();

  public Task CancelLatestQueryAndWaitAsync() => refreshSession.CancelAndWaitAsync();

  public ValueTask DisposeAsync() => refreshSession.DisposeAsync();

  private async Task<WorkbenchTranscriptionModelState> QueryCoreAsync(
    AppSettings settings,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(settings);
    try
    {
      IReadOnlyList<ModelInfo> models = await modelManager.GetModelsAsync(cancellationToken).ConfigureAwait(true);
      TranscriptionModelSelection configured = settings.GetConfiguredTranscriptionSelection().Normalize();
      IReadOnlyList<TranscriptionModelOptionViewModel> options = models
        .Where(model => model.IsInstalled || Matches(model, configured))
        .OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
        .Select(model => TranscriptionModelOptionViewModel.FromModelInfo(model, Matches(model, configured)))
        .ToArray();
      TranscriptionModelOptionViewModel? selected = options.FirstOrDefault(option => Matches(option.Selection, configured))
        ?? options.FirstOrDefault(option => option.IsInstalled)
        ?? options.FirstOrDefault();
      bool hasInstalled = options.Any(option => option.IsInstalled);
      string status = selected is null
        ? "No speech models installed."
        : selected.IsInstalled
          ? $"Speech: {selected.Label}"
          : "Selected speech model is not installed.";
      return new WorkbenchTranscriptionModelState(options, selected, hasInstalled, status);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException || IsModelManagementException(ex))
    {
      diagnostics.Warning($"Workbench speech-model query failed: {ex.Message}");
      return new WorkbenchTranscriptionModelState(
        Array.Empty<TranscriptionModelOptionViewModel>(),
        Selected: null,
        HasInstalledModel: false,
        "Speech models unavailable. See Diagnostics.");
    }
  }

  private static bool Matches(ModelInfo model, TranscriptionModelSelection selection) =>
    string.Equals(model.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase)
    && string.Equals(model.ModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase);

  private static bool Matches(TranscriptionModelSelection left, TranscriptionModelSelection right)
  {
    TranscriptionModelSelection normalizedLeft = left.Normalize();
    TranscriptionModelSelection normalizedRight = right.Normalize();
    return string.Equals(normalizedLeft.ProviderId, normalizedRight.ProviderId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(normalizedLeft.ModelId, normalizedRight.ModelId, StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsModelManagementException(Exception exception) =>
    string.Equals(exception.GetType().Name, "ModelManagementException", StringComparison.Ordinal);
}
