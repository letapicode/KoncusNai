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
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Lifecycle;
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
  private readonly object stateSync = new();
  private static readonly AsyncLocal<OperationLease?> Executing = new();
  private readonly CancellationTokenSource lifetime = new();
  private readonly HashSet<OperationLease> activeOperations = new();
  private long draftRevision;
  private long pendingReplacementRevision;
  private AppSettings? lastAcceptedSnapshot;
  private AppSettings? lastOwnedCommit;
  private AppSettings? pendingIntentBaseline;
  private long lastAcceptedRevision;
  private Task? disposalTask;
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
    this.diagnostics = DiagnosticBoundary.Wrap(diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
    if (modelManager is AutomaticDictationModelManager automatic)
      automatic.PreparationProgress += OnPreparationProgress;

    if (autoSaveCoordinator is not null)
    {
      this.autoSaveCoordinator = autoSaveCoordinator;
      ownsAutoSaveCoordinator = false;
    }
    else
    {
      this.autoSaveCoordinator = new SettingsAutoSaveCoordinator(SaveOwnedSettingsAsync);
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
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    ObjectDisposedException.ThrowIf(disposed, this);
    long initialRevision = draftRevision;
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Load, "Loading settings..."));

    try
    {
      AppSettings loadedSettings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      if (disposed || initialRevision != draftRevision) return;
      AppSettings normalized = CurrentSettingsPolicy.Normalize(loadedSettings);
      SettingsDraft loadedDraft = SettingsDraft.FromSettings(normalized);
      lock (stateSync)
      {
        if (disposed || initialRevision != draftRevision) return;
        PersistedSettings = normalized;
        CurrentDraft = loadedDraft;
      }
      Notify(DraftChanged, CurrentDraft);

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
      lock (stateSync)
      {
        if (disposed || initialRevision != draftRevision) return;
        CurrentDraft = SettingsDraft.CreateReadOnly(ex.Message);
      }
      Notify(DraftChanged, CurrentDraft);
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
    SettingsDraft original;
    long originalRevision;
    lock (stateSync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      original = CurrentDraft;
      originalRevision = draftRevision;
    }
    if (original.IsReadOnly) throw new InvalidOperationException("Cannot update read-only settings.");
    SettingsDraft updated = update(original) ?? throw new InvalidOperationException("Draft update returned null.");
    updated = updated with { InsertionBlockedProcessNames = SettingsSnapshot.Capture(updated.ToSettings()).InsertionBlockedProcessNames };
    lock (stateSync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (originalRevision != draftRevision)
        throw new InvalidOperationException("The settings draft changed during this update; resubmit the edit against the current draft.");
      CurrentDraft = updated;
      draftRevision++;
    }
    Notify(DraftChanged, updated);

    if (scheduleAutoSave && (updated.IsDirty(PersistedSettings) || lastAcceptedSnapshot is not null))
    {
      ScheduleAutoSave();
    }
    else if (scheduleAutoSave) autoSaveCoordinator.CancelPending();
  }

  /// <summary>
  /// Schedules an autosave snapshot if the current draft is valid.
  /// </summary>
  public bool ScheduleAutoSave()
  {
    var request = CaptureSave(out string? rejection, out long rejectedRevision);
    if (request is null)
    {
      autoSaveCoordinator.CancelPending(rejectedRevision);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Save, rejection!));
      return false;
    }
    (AppSettings snapshot, AppSettings? baseline, long ownerRevision) = request.Value;
    if (!Status.IsBusy) SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Save, "Saving settings..."));
    autoSaveCoordinator.Schedule(snapshot, baseline, ownerRevision);
    return true;
  }

  /// <summary>
  /// Flushes pending saves immediately and waits for persistence to complete.
  /// </summary>
  public async Task<bool> FlushSaveAsync(CancellationToken cancellationToken = default)
  {
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    ObjectDisposedException.ThrowIf(disposed, this);
    cancellationToken.ThrowIfCancellationRequested();
    using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
      diagnostics,
      operationName: "SettingsSave");

    opScope.Stage("validation");
    var request = CaptureSave(out string? rejection, out long rejectedRevision);
    if (request is null)
    {
      autoSaveCoordinator.CancelPending(rejectedRevision);
      opScope.Fail(null, rejection!, DiagnosticRemediationCodes.OperationFailed);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Save, rejection!));
      return false;
    }

    opScope.Stage("storage");
    (AppSettings snapshot, AppSettings? baseline, long ownerRevision) = request.Value;
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Save, "Saving settings..."));
    cancellationToken.ThrowIfCancellationRequested();
    bool saved = await autoSaveCoordinator.FlushAsync(snapshot, baseline, cancellationToken, ownerRevision).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    if (saved)
    {
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
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    long sequence = Interlocked.Increment(ref modelRefreshSequence);
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.RefreshModels, "Refreshing speech models..."));
    using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
      diagnostics,
      operationName: "SettingsModelRefresh");

    try
    {
      opScope.Stage("model_query");
      IReadOnlyList<ModelInfo> models = await modelManager.GetModelsAsync(cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
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
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    long sequence = Interlocked.Increment(ref audioRefreshSequence);
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.RefreshAudioDevices, "Refreshing audio devices..."));
    using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
      diagnostics,
      operationName: "SettingsAudioRefresh");

    try
    {
      opScope.Stage("device_enumeration");
      IReadOnlyList<AudioInputDeviceOption> devices = await audioDeviceService.GetInputDevicesAsync(cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
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
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    ArgumentNullException.ThrowIfNull(selection);

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.DownloadModel,
      $"Verifying local files or downloading {selection.ProviderId}/{selection.ModelId}..."));
    try
    {
      await modelManager.DownloadModelAsync(selection, progress, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      await RefreshModelsAsync(selection, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.DownloadModel,
        $"Speech model installed: {selection.ProviderId}/{selection.ModelId}."));
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return false;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Speech-model download failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.DownloadModel,
        "Speech model verification or download failed. See Diagnostics."));
      return false;
    }
  }

  /// <summary>
  /// Sets a model as active in the model manager and updates the draft.
  /// </summary>
  public async Task<bool> ActivateModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
  {
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    ArgumentNullException.ThrowIfNull(selection);

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.ActivateModel, $"Activating {selection.ProviderId}/{selection.ModelId}..."));
    try
    {
      await modelManager.SetActiveModelAsync(selection, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      UpdateDraft(d => d with
      {
        TranscriptionProviderId = selection.ProviderId,
        TranscriptionModelId = selection.ModelId,
      });

      await RefreshModelsAsync(selection, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
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

  internal async Task<bool> PrepareRuntimeAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken)
  {
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    if (IsBusy || !CohereRuntimePreparation.Supports(selection)) return false;
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.PrepareRuntime, "Preparing local acceleration..."));
    try
    {
      if (modelManager is not AutomaticDictationModelManager automatic)
        throw new InvalidOperationException("Automatic preparation is unavailable in this installation.");
      await automatic.PrepareAsync(selection, null, true, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.PrepareRuntime,
        automatic.LastPreparationMessage ?? "Local acceleration prepared."));
      return true;
    }
    catch (OperationCanceledException)
    {
      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.PrepareRuntime,
        "Preparation cancelled. Verified downloads can be reused when you retry."));
      return false;
    }
    catch (InvalidOperationException ex)
    {
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.PrepareRuntime, ex.Message));
      return false;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Dictation runtime preparation failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.PrepareRuntime,
        "Preparation failed. Check Diagnostics, repair the installation if needed, and retry."));
      return false;
    }
  }

  private void OnPreparationProgress(object? sender, string message)
  {
    if (Status.Kind is SettingsOperationKind.DownloadModel or SettingsOperationKind.PrepareRuntime)
      SetStatus(SettingsOperationStatus.Running(Status.Kind, message));
  }

  /// <summary>
  /// Deletes a speech model and resolves fallback if it was the currently configured model.
  /// </summary>
  public async Task<bool> DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
  {
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    ArgumentNullException.ThrowIfNull(selection);

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.DeleteModel, $"Deleting {selection.ProviderId}/{selection.ModelId}..."));
    try
    {
      await modelManager.DeleteModelAsync(selection, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();

      if (string.Equals(CurrentDraft.TranscriptionProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase)
          && string.Equals(CurrentDraft.TranscriptionModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase))
      {
        TranscriptionModelSelection fallback = await ResolveFallbackModelAfterDeleteAsync(selection, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
        UpdateDraft(d => d with
        {
          TranscriptionProviderId = fallback.ProviderId,
          TranscriptionModelId = fallback.ModelId,
        });
      }

      await RefreshModelsAsync(CurrentDraft.ToSettings().GetConfiguredTranscriptionSelection(), cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
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
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.RunBenchmark, "Benchmarking speech models..."));
    try
    {
      BenchmarkResult result = await benchmarkService.RunAsync(languageScope, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      LastBenchmarkResult = result;

      await RefreshModelsAsync(new TranscriptionModelSelection(result.RecommendedProviderId, result.RecommendedModelId), cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
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
  /// Validates and adopts an imported draft without overwriting concurrent edits.
  /// Persistence is tracked separately; later option refresh cannot undo adoption.
  /// </summary>
  public async Task<bool> ImportAsync(string filePath, CancellationToken cancellationToken = default)
  {
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
    ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
    long originalRevision = draftRevision;
    bool adopted = false;

    SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Import, "Importing settings..."));
    try
    {
      AppSettings importedSettings = await fileTransferService.ImportAsync(filePath, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
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

      lock (stateSync)
      {
        if (disposed || originalRevision != draftRevision) return false;
        CurrentDraft = candidateDraft;
        pendingReplacementRevision = ++draftRevision;
        adopted = true;
      }
      Notify(DraftChanged, CurrentDraft);
      ScheduleAutoSave();

      await RefreshModelsAsync(CurrentDraft.ToSettings().GetConfiguredTranscriptionSelection(), cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      await RefreshAudioDevicesAsync(cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();

      SetStatus(SettingsOperationStatus.Succeeded(SettingsOperationKind.Import, "Settings imported into the draft; persistence is tracked separately."));
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return adopted;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Settings import failed", ex);
      SetStatus(SettingsOperationStatus.Failed(SettingsOperationKind.Import, "Import failed. See Diagnostics."));
      // Once accepted, an import cannot be rolled back by a later option refresh
      // failure: its save may already have committed. Before admission, edits survive.
      return adopted;
    }
  }

  /// <summary>
  /// Exports the current draft to an external file.
  /// </summary>
  public async Task<bool> ExportAsync(string filePath, CancellationToken cancellationToken = default)
  {
    using OperationLease accepted = EnterOperation(cancellationToken);
    cancellationToken = accepted.Token;
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
      cancellationToken.ThrowIfCancellationRequested();
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

  public ValueTask DisposeAsync()
  {
    lock (stateSync)
    {
      disposalTask ??= DisposeCoreAsync();
      return (Executing.Value is OperationLease executing && executing.Owner == this && !executing.Completion.Task.IsCompleted)
        || autoSaveCoordinator.IsExecuting
        ? ValueTask.FromException(new InvalidOperationException("Request settings shutdown without awaiting it inside an accepted operation."))
        : new ValueTask(disposalTask);
    }
  }

  private async Task DisposeCoreAsync()
  {
    disposed = true;
    await Task.Yield();
    Task[] pending;
    lock (stateSync) pending = activeOperations.Select(operation => operation.Completion.Task).ToArray();
    try
    {
      await LifecycleCleanup.RunAsync(
        LifecycleCleanup.Sync("Cancel settings operations", lifetime.Cancel),
        new CleanupStep("Accepted settings operations", () => Task.WhenAll(pending)),
        new CleanupStep("Settings autosave", () => ownsAutoSaveCoordinator
          ? autoSaveCoordinator.RequestShutdown() : Task.CompletedTask)).ConfigureAwait(false);
    }
    finally
    {
      if (modelManager is AutomaticDictationModelManager automatic)
        automatic.PreparationProgress -= OnPreparationProgress;
      autoSaveCoordinator.StatusChanged -= OnAutoSaveStatusChanged;
      lifetime.Dispose();
    }
  }

  private void SetStatus(SettingsOperationStatus status)
  {
    if (disposed) return;
    Status = status;
    Notify(StatusChanged, status);
  }

  private void OnAutoSaveStatusChanged(object? sender, SettingsAutoSaveStatus status)
  {
    if (disposed) return;
    switch (status.State)
    {
      case SettingsAutoSaveState.Saving:
        if (Status.Kind is SettingsOperationKind.Idle or SettingsOperationKind.Save)
        {
          SetStatus(SettingsOperationStatus.Running(SettingsOperationKind.Save, "Saving settings..."));
        }
        break;

      case SettingsAutoSaveState.Saved:
        if (status.Snapshot is null || status.Submitted is null) return;
        lock (stateSync)
        {
          if (disposed || CurrentDraft.IsReadOnly) return;
          AppSettings remainingEdits = SettingsSnapshot.Merge(status.Submitted, CurrentDraft.ToSettings(), status.Snapshot);
          SettingsDraft previous = CurrentDraft;
          CurrentDraft = SettingsDraft.FromSettings(remainingEdits) with
          {
            PreviewThemePreference = previous.PreviewThemePreference,
            PreviewChatOutputFontSize = previous.PreviewChatOutputFontSize,
            PreviewChatTypefaceId = previous.PreviewChatTypefaceId,
          };
          if (status.OwnerRevision == draftRevision) CurrentDraft = CurrentDraft.ClearPreviews();
          PersistedSettings = status.Snapshot;
          if (status.OwnerRevision == lastAcceptedRevision)
          {
            lastAcceptedSnapshot = null;
            pendingIntentBaseline = null;
          }
          else if (lastAcceptedSnapshot is not null && pendingIntentBaseline is not null)
          {
            pendingIntentBaseline = SettingsSnapshot.Merge(lastAcceptedSnapshot, pendingIntentBaseline, status.Snapshot);
            lastAcceptedSnapshot = SettingsSnapshot.Merge(status.Submitted, lastAcceptedSnapshot, status.Snapshot);
          }
          if (status.Replacement && status.OwnerRevision >= pendingReplacementRevision) pendingReplacementRevision = 0;
        }
        if (disposed) return;
        Notify(DraftChanged, CurrentDraft);
        Notify(SettingsSaved, status.Snapshot);
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

  private (AppSettings Snapshot, AppSettings? Baseline, long Revision)? CaptureSave(out string? rejection, out long rejectedRevision)
  {
    lock (stateSync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      rejectedRevision = draftRevision;
      // Validation and snapshot capture refer to the same owned revision. A
      // concurrent edit cannot replace a validated draft before capture.
      rejection = CurrentDraft.IsReadOnly ? "Settings are read-only and cannot be saved."
        : !CurrentDraft.Validate().IsValid ? "Cannot save invalid settings. Review the highlighted values." : null;
      if (rejection is not null) return null;
      AppSettings snapshot = SettingsSnapshot.Capture(CurrentDraft.ToSettings());
      // Include reversions of already accepted edits as well as dirty fields. A
      // writer ignoring cancellation may commit the earlier value before this one.
      AppSettings baseline = lastAcceptedSnapshot is null ? PersistedSettings
        : SettingsSnapshot.Merge(snapshot, lastAcceptedSnapshot, pendingIntentBaseline ?? PersistedSettings);
      lastAcceptedSnapshot = snapshot;
      pendingIntentBaseline = baseline;
      lastAcceptedRevision = draftRevision;
      return (snapshot, pendingReplacementRevision != 0 ? null : baseline, draftRevision);
    }
  }

  private async Task<AppSettings> SaveOwnedSettingsAsync(AppSettings? baseline, AppSettings edited, CancellationToken token)
  {
    AppSettings actualBaseline;
    lock (stateSync)
    {
      // The preceding owned writer has settled. Rebase cumulative local intent
      // onto its actual outcome, including a commit made despite cancellation.
      actualBaseline = lastOwnedCommit ?? PersistedSettings;
      if (baseline is not null) edited = SettingsSnapshot.Merge(baseline, edited, actualBaseline);
    }
    AppSettings committed = SettingsSnapshot.Capture(await settingsStore.SaveChangesAsync(
      baseline is null ? null : actualBaseline, edited, token).ConfigureAwait(false));
    // Persistence ownership survives suppressed late UI notifications during quit.
    lock (stateSync) lastOwnedCommit = committed;
    return committed;
  }

  private void Notify<T>(EventHandler<T>? subscribers, T value)
  {
    if (subscribers is null || disposed) return;
    foreach (EventHandler<T> subscriber in subscribers.GetInvocationList())
    {
      if (disposed) return;
      try { subscriber(this, value); }
      catch (Exception error) { diagnostics.Error("Settings notification failed", error); }
    }
  }

  private OperationLease EnterOperation(CancellationToken token)
  {
    lock (stateSync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      token.ThrowIfCancellationRequested();
      OperationLease operation = new(this, token, Executing.Value);
      activeOperations.Add(operation);
      Executing.Value = operation;
      return operation;
    }
  }

  private sealed class OperationLease : IDisposable
  {
    private readonly SettingsOperationController owner;
    private readonly OperationLease? previous;
    private readonly CancellationTokenSource source;
    internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken Token => source.Token;
    internal SettingsOperationController Owner => owner;
    internal OperationLease(SettingsOperationController owner, CancellationToken token, OperationLease? previous)
    {
      this.owner = owner;
      this.previous = previous;
      source = CancellationTokenSource.CreateLinkedTokenSource(token, owner.lifetime.Token);
    }
    public void Dispose()
    {
      Executing.Value = previous;
      source.Dispose();
      lock (owner.stateSync) owner.activeOperations.Remove(this);
      Completion.TrySetResult();
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
