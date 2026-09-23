using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Benchmarking;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Diagnostics;
using DictateAnywhere.Settings;

namespace DictateAnywhere.App.Settings;

/// <summary>
/// Deterministic operation controller for settings editing.
/// Manages draft state, validation, autosave revision coordination, race-safe async
/// model and audio refreshes, and atomic import/export failure recovery.
/// </summary>
[SuppressMessage(
  "Design",
  "CA1031:Do not catch general exception types",
  Justification = "This asynchronous operation controller boundary records unexpected runtime failures to IDiagnostics and transitions to a bounded failure status.")]
internal sealed class SettingsOperationController : IAsyncDisposable
{
  private readonly ISettingsStore settingsStore;
  private readonly ISettingsFileTransferService fileTransferService;
  private readonly IModelManager modelManager;
  private readonly IAudioInputDeviceService audioDeviceService;
  private readonly IBenchmarkService benchmarkService;
  private readonly IDiagnostics diagnostics;
  private readonly SettingsAutoSaveCoordinator autoSaveCoordinator;
  private readonly bool ownsAutoSaveCoordinator;

  private long modelRefreshSequence;
  private long audioRefreshSequence;
  private AppSettings? lastScheduledSettings;
  private bool disposed;

  public SettingsOperationController(
    ISettingsStore settingsStore,
    ISettingsFileTransferService fileTransferService,
    IModelManager modelManager,
    IAudioInputDeviceService audioDeviceService,
    IBenchmarkService benchmarkService,
    IDiagnostics diagnostics,
    SettingsAutoSaveCoordinator? autoSaveCoordinator = null)
  {
    this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    this.fileTransferService = fileTransferService ?? throw new ArgumentNullException(nameof(fileTransferService));
    this.modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
    this.audioDeviceService = audioDeviceService ?? throw new ArgumentNullException(nameof(audioDeviceService));
    this.benchmarkService = benchmarkService ?? throw new ArgumentNullException(nameof(benchmarkService));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    if (autoSaveCoordinator is not null)
    {
      this.autoSaveCoordinator = autoSaveCoordinator;
      ownsAutoSaveCoordinator = false;
    }
    else
    {
      this.autoSaveCoordinator = new SettingsAutoSaveCoordinator(this.settingsStore.SaveAsync);
      ownsAutoSaveCoordinator = true;
    }

    this.autoSaveCoordinator.StatusChanged += OnAutoSaveStatusChanged;

    PersistedSettings = AppSettings.Default;
    CurrentDraft = SettingsDraft.FromSettings(PersistedSettings);
    Status = SettingsOperationStatus.Idle;
    AvailableModels = Array.Empty<ModelOptionViewModel>();
    AvailableAudioDevices = Array.Empty<AudioDeviceOptionViewModel>();
  }

  public SettingsDraft CurrentDraft { get; private set; }

  public AppSettings PersistedSettings { get; private set; }

  public SettingsOperationStatus Status { get; private set; }

  public IReadOnlyList<ModelOptionViewModel> AvailableModels { get; private set; }

  public IReadOnlyList<AudioDeviceOptionViewModel> AvailableAudioDevices { get; private set; }

  public BenchmarkResult? LastBenchmarkResult { get; private set; }

  public bool IsDirty => CurrentDraft.IsDirty(PersistedSettings);

  public bool IsBusy => Status.IsBusy;

  public event EventHandler<SettingsDraft>? DraftChanged;

  public event EventHandler<SettingsOperationStatus>? StatusChanged;

  public event EventHandler<AppSettings>? SettingsSaved;

  /// <summary>
  /// Initializes the controller by loading settings from persistence.
  /// Unsupported future schemas are represented as read-only drafts.
  /// </summary>
  public async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Load, "Loading settings..."));

    try
    {
      AppSettings loadedSettings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
      AppSettings normalized = CurrentSettingsPolicy.Normalize(loadedSettings);
      PersistedSettings = normalized;
      CurrentDraft = SettingsDraft.FromSettings(normalized);
      DraftChanged?.Invoke(this, CurrentDraft);

      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.Load, "Settings loaded."));

      if (!ReferenceEquals(loadedSettings, normalized))
      {
        ScheduleAutoSave();
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Initialization was canceled.
    }
    catch (Exception ex) when (ex is UnsupportedSettingsSchemaException or UnrecognizedSettingsSchemaException)
    {
      CurrentDraft = SettingsDraft.CreateReadOnly(ex.Message);
      DraftChanged?.Invoke(this, CurrentDraft);
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.Load, "Settings loaded in read-only mode (unsupported schema detected)."));
    }
    catch (Exception ex)
    {
      diagnostics.Error("Settings load failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Load, "Could not load settings. See Diagnostics."));
    }
  }

  /// <summary>
  /// Updates the draft through a functional mutation. Automatically schedules an autosave if dirty.
  /// </summary>
  public void UpdateDraft(Func<SettingsDraft, SettingsDraft> update, bool scheduleAutoSave = true)
  {
    ArgumentNullException.ThrowIfNull(update);

    if (CurrentDraft.IsReadOnly)
    {
      throw new InvalidOperationException($"Cannot update read-only settings draft: {CurrentDraft.ReadOnlyReason ?? "Settings are read-only."}");
    }

    SettingsDraft updated = update(CurrentDraft);
    CurrentDraft = updated;
    DraftChanged?.Invoke(this, updated);

    if (scheduleAutoSave && updated.IsDirty(PersistedSettings))
    {
      ScheduleAutoSave();
    }
  }

  /// <summary>
  /// Schedules an autosave snapshot if the current draft is valid.
  /// </summary>
  public bool ScheduleAutoSave()
  {
    if (CurrentDraft.IsReadOnly)
    {
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Save, "Settings are read-only and cannot be saved."));
      return false;
    }

    SettingsValidationResult validation = CurrentDraft.Validate();
    if (!validation.IsValid)
    {
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Save, "Cannot save invalid settings. Review the highlighted values."));
      return false;
    }

    AppSettings snapshot = CurrentDraft.ToSettings();
    lastScheduledSettings = snapshot;
    autoSaveCoordinator.Schedule(snapshot);
    return true;
  }

  /// <summary>
  /// Flushes pending saves immediately and waits for persistence to complete.
  /// </summary>
  public async Task<bool> FlushSaveAsync(CancellationToken cancellationToken = default)
  {
    using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
      diagnostics,
      operationName: "SettingsSave");

    if (CurrentDraft.IsReadOnly)
    {
      opScope.Fail(null, "Settings are read-only and cannot be saved.", DiagnosticRemediationCodes.OperationFailed);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Save, "Settings are read-only and cannot be saved."));
      return false;
    }

    opScope.Stage("validation");
    SettingsValidationResult validation = CurrentDraft.Validate();
    if (!validation.IsValid)
    {
      opScope.Fail(null, "Cannot save invalid settings.", DiagnosticRemediationCodes.OperationFailed);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Save, "Cannot save invalid settings. Review the highlighted values."));
      return false;
    }

    opScope.Stage("storage");
    AppSettings snapshot = CurrentDraft.ToSettings();
    lastScheduledSettings = snapshot;
    bool saved = await autoSaveCoordinator.FlushAsync(snapshot).ConfigureAwait(false);
    if (saved)
    {
      PersistedSettings = snapshot;
      CurrentDraft = CurrentDraft.ClearPreviews();
      SettingsSaved?.Invoke(this, PersistedSettings);
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.Save, "Settings saved."));
      opScope.Complete();
    }
    else
    {
      opScope.Fail(null, "Settings save did not persist.", DiagnosticRemediationCodes.OperationFailed);
    }

    return saved;
  }

  /// <summary>
  /// Cancels any scheduled background autosave.
  /// </summary>
  public void CancelPendingSave()
  {
    autoSaveCoordinator.CancelPending();
  }

  /// <summary>
  /// Refreshes speech models asynchronously. Monotonically sequenced so latest refresh always wins.
  /// </summary>
  public async Task RefreshModelsAsync(TranscriptionModelSelection? targetSelection = null, CancellationToken cancellationToken = default)
  {
    long sequence = Interlocked.Increment(ref modelRefreshSequence);
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.RefreshModels, "Refreshing speech models..."));
    using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
      diagnostics,
      operationName: "SettingsModelRefresh");

    try
    {
      opScope.Stage("model_query");
      IReadOnlyList<ModelInfo> models = await modelManager.GetModelsAsync(cancellationToken).ConfigureAwait(false);
      if (sequence != Interlocked.Read(ref modelRefreshSequence))
      {
        opScope.Cancel("Superseded by newer model refresh.");
        return; // A newer refresh superseded this one
      }

      AvailableModels = models.Select(ModelOptionViewModel.FromModelInfo).ToList();
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.RefreshModels, "Models refreshed."));
      opScope.Complete();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      opScope.Cancel("Model refresh cancelled.");
    }
    catch (Exception ex)
    {
      opScope.Fail(ex, "Speech-model refresh failed");
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.RefreshModels, "Model refresh failed. See Diagnostics."));
    }
  }

  /// <summary>
  /// Refreshes available audio input devices asynchronously. Monotonically sequenced.
  /// </summary>
  public async Task RefreshAudioDevicesAsync(CancellationToken cancellationToken = default)
  {
    long sequence = Interlocked.Increment(ref audioRefreshSequence);
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.RefreshAudioDevices, "Refreshing audio devices..."));
    using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
      diagnostics,
      operationName: "SettingsAudioRefresh");

    try
    {
      opScope.Stage("device_enumeration");
      IReadOnlyList<AudioInputDeviceOption> devices = await audioDeviceService.GetInputDevicesAsync(cancellationToken).ConfigureAwait(false);
      if (sequence != Interlocked.Read(ref audioRefreshSequence))
      {
        opScope.Cancel("Superseded by newer audio refresh.");
        return; // Stale result discarded
      }

      List<AudioDeviceOptionViewModel> items = new(devices.Count + 1)
      {
        new(null, "Default communication device"),
      };
      items.AddRange(devices.Select(d => new AudioDeviceOptionViewModel(d.DeviceId, d.Label)));
      AvailableAudioDevices = items;

      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.RefreshAudioDevices, "Audio devices refreshed."));
      opScope.Complete();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      opScope.Cancel("Audio refresh cancelled.");
    }
    catch (Exception ex)
    {
      opScope.Fail(ex, "Audio-device refresh failed");
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.RefreshAudioDevices, "Audio device refresh failed. See Diagnostics."));
    }
  }

  /// <summary>
  /// Downloads a speech model asynchronously with progress reporting.
  /// </summary>
  public async Task<bool> DownloadModelAsync(
    TranscriptionModelSelection selection,
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.DownloadModel, $"Downloading {selection.ProviderId}/{selection.ModelId}..."));
    try
    {
      await modelManager.DownloadModelAsync(selection, progress, cancellationToken).ConfigureAwait(false);
      await RefreshModelsAsync(selection, cancellationToken).ConfigureAwait(false);
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.DownloadModel, $"Downloaded {selection.ProviderId}/{selection.ModelId}."));
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return false;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Speech-model download failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.DownloadModel, "Download failed. See Diagnostics."));
      return false;
    }
  }

  /// <summary>
  /// Sets a model as active in the model manager and updates the draft.
  /// </summary>
  public async Task<bool> ActivateModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.ActivateModel, $"Activating {selection.ProviderId}/{selection.ModelId}..."));
    try
    {
      await modelManager.SetActiveModelAsync(selection, cancellationToken).ConfigureAwait(false);
      UpdateDraft(d => d with
      {
        TranscriptionProviderId = selection.ProviderId,
        TranscriptionModelId = selection.ModelId,
      });

      await RefreshModelsAsync(selection, cancellationToken).ConfigureAwait(false);
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.ActivateModel, $"Activated {selection.ProviderId}/{selection.ModelId}."));
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return false;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Speech-model activation failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.ActivateModel, "Activation failed. See Diagnostics."));
      return false;
    }
  }

  /// <summary>
  /// Deletes a speech model and resolves fallback if it was the currently configured model.
  /// </summary>
  public async Task<bool> DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(selection);

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.DeleteModel, $"Deleting {selection.ProviderId}/{selection.ModelId}..."));
    try
    {
      await modelManager.DeleteModelAsync(selection, cancellationToken).ConfigureAwait(false);

      if (string.Equals(CurrentDraft.TranscriptionProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase)
          && string.Equals(CurrentDraft.TranscriptionModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase))
      {
        TranscriptionModelSelection fallback = await ResolveFallbackModelAfterDeleteAsync(selection, cancellationToken).ConfigureAwait(false);
        UpdateDraft(d => d with
        {
          TranscriptionProviderId = fallback.ProviderId,
          TranscriptionModelId = fallback.ModelId,
        });
      }

      await RefreshModelsAsync(CurrentDraft.ToSettings().GetConfiguredTranscriptionSelection(), cancellationToken).ConfigureAwait(false);
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.DeleteModel, $"Deleted {selection.ProviderId}/{selection.ModelId}."));
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return false;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Speech-model deletion failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.DeleteModel, "Delete failed. See Diagnostics."));
      return false;
    }
  }

  /// <summary>
  /// Runs a calibration benchmark for speech models.
  /// </summary>
  public async Task<BenchmarkResult?> RunBenchmarkAsync(string? languageScope = null, CancellationToken cancellationToken = default)
  {
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.RunBenchmark, "Benchmarking speech models..."));
    try
    {
      BenchmarkResult result = await benchmarkService.RunAsync(languageScope, cancellationToken).ConfigureAwait(false);
      LastBenchmarkResult = result;

      await RefreshModelsAsync(new TranscriptionModelSelection(result.RecommendedProviderId, result.RecommendedModelId), cancellationToken).ConfigureAwait(false);
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.RunBenchmark, $"Benchmark finished at {result.ExecutedAtUtc.LocalDateTime:HH:mm:ss}."));
      return result;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return null;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Speech-model benchmark failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.RunBenchmark, "Benchmark failed. See Diagnostics."));
      return null;
    }
  }

  /// <summary>
  /// Atomically imports settings from a file. If validation or transfer fails,
  /// the active draft and unsaved changes are strictly preserved.
  /// </summary>
  public async Task<bool> ImportAsync(string filePath, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Import, "Importing settings..."));
    try
    {
      AppSettings importedSettings = await fileTransferService.ImportAsync(filePath, cancellationToken).ConfigureAwait(false);
      SettingsDraft candidateDraft = SettingsDraft.FromSettings(importedSettings) with
      {
        HasCompletedFirstRun = true,
      };

      SettingsValidationResult validation = candidateDraft.Validate();
      if (!validation.IsValid)
      {
        SetStatus(SettingsOperationStatus.Failed(
          SettingsOperationKind.Import,
          "Imported settings are invalid. Review the file and try again."));
        // Note: CurrentDraft is strictly untouched to preserve user edits.
        return false;
      }

      // Atomically replace the draft
      CurrentDraft = candidateDraft;
      DraftChanged?.Invoke(this, CurrentDraft);
      ScheduleAutoSave();

      await RefreshModelsAsync(CurrentDraft.ToSettings().GetConfiguredTranscriptionSelection(), cancellationToken).ConfigureAwait(false);
      await RefreshAudioDevicesAsync(cancellationToken).ConfigureAwait(false);

      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.Import, "Settings imported successfully."));
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return false;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Settings import failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Import, "Import failed. See Diagnostics."));
      // Note: CurrentDraft is strictly untouched to preserve user edits.
      return false;
    }
  }

  /// <summary>
  /// Exports the current draft to an external file.
  /// </summary>
  public async Task<bool> ExportAsync(string filePath, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Export, "Exporting settings..."));
    try
    {
      if (CurrentDraft.IsReadOnly)
      {
        SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Export, "Settings are in read-only mode."));
        return false;
      }

      SettingsValidationResult validation = CurrentDraft.Validate();
      if (!validation.IsValid)
      {
        SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Export, "Cannot export invalid settings. Review the highlighted values."));
        return false;
      }

      AppSettings snapshot = CurrentDraft.ToSettings();
      await fileTransferService.ExportAsync(filePath, snapshot, cancellationToken).ConfigureAwait(false);
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.Export, $"Exported settings to {filePath}."));
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return false;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Settings export failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Export, "Export failed. See Diagnostics."));
      return false;
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    autoSaveCoordinator.StatusChanged -= OnAutoSaveStatusChanged;
    if (ownsAutoSaveCoordinator)
    {
      await autoSaveCoordinator.DisposeAsync().ConfigureAwait(false);
    }
  }

  private void SetStatus(SettingsOperationStatus status)
  {
    Status = status;
    StatusChanged?.Invoke(this, status);
  }

  private void OnAutoSaveStatusChanged(object? sender, SettingsAutoSaveStatus status)
  {
    switch (status.State)
    {
      case SettingsAutoSaveState.Saving:
        if (Status.Kind is SettingsOperationKind.Idle or SettingsOperationKind.Save)
        {
          SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Save, "Saving settings..."));
        }
        break;

      case SettingsAutoSaveState.Saved:
        PersistedSettings = lastScheduledSettings ?? PersistedSettings;
        CurrentDraft = CurrentDraft.ClearPreviews();
        SettingsSaved?.Invoke(this, PersistedSettings);
        if (Status.Kind is SettingsOperationKind.Idle or SettingsOperationKind.Save)
        {
          SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.Save, "Settings saved."));
        }
        break;

      case SettingsAutoSaveState.Failed:
        diagnostics.Error("Settings auto-save failed", new IOException(status.ErrorMessage));
        SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Save, "Save failed. See Diagnostics."));
        break;
    }
  }

  private async Task<TranscriptionModelSelection> ResolveFallbackModelAfterDeleteAsync(
    TranscriptionModelSelection deletedSelection,
    CancellationToken cancellationToken)
  {
    try
    {
      IReadOnlyList<ModelInfo> available = await modelManager.GetModelsAsync(cancellationToken).ConfigureAwait(false);
      foreach (ModelInfo model in available)
      {
        if (model.IsInstalled
            && (!string.Equals(model.ProviderId, deletedSelection.ProviderId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(model.ModelId, deletedSelection.ModelId, StringComparison.OrdinalIgnoreCase)))
        {
          return new TranscriptionModelSelection(model.ProviderId, model.ModelId);
        }
      }
    }
    catch
    {
      // Fallback to default
    }

    return AppSettings.Default.GetConfiguredTranscriptionSelection();
  }
}
