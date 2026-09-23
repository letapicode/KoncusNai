using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Runtime;

public sealed class ModelReadinessCoordinator : IModelReadinessSession
{
  private readonly IModelManager modelManager;
  private readonly IDiagnostics diagnostics;
  private readonly Func<AppSettings, IDiagnostics, TranscriptionModelRegistry> modelRegistryFactory;
  private readonly Func<AppSettings, IDiagnostics, ITranscriptionService> fallbackTranscriptionServiceFactory;
  private readonly SemaphoreSlim lifecycleLock = new(1, 1);
  private readonly object snapshotSync = new();

  private TranscriptionModelRegistry? modelRegistry;
  private CancellationTokenSource? warmupCts;
  private List<Task> warmupTasks = new();
  private ModelReadinessSnapshot snapshot = ModelReadinessSnapshot.Empty;
  private bool disposed;

  internal ModelReadinessCoordinator(
    IModelManager modelManager,
    IDiagnostics diagnostics,
    Func<AppSettings, IDiagnostics, TranscriptionModelRegistry> modelRegistryFactory,
    Func<AppSettings, IDiagnostics, ITranscriptionService> fallbackTranscriptionServiceFactory)
  {
    this.modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.modelRegistryFactory = modelRegistryFactory ?? throw new ArgumentNullException(nameof(modelRegistryFactory));
    this.fallbackTranscriptionServiceFactory = fallbackTranscriptionServiceFactory ?? throw new ArgumentNullException(nameof(fallbackTranscriptionServiceFactory));
  }

  public event EventHandler<ModelReadinessSnapshot>? SnapshotChanged;

  public ModelReadinessSnapshot CurrentSnapshot
  {
    get
    {
      lock (snapshotSync)
      {
        return snapshot;
      }
    }
  }

  public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
  {
    await RefreshAsync(settings, cancellationToken).ConfigureAwait(false);
  }

  public async Task RefreshAsync(AppSettings settings, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ObjectDisposedException.ThrowIf(disposed, this);

    await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);

      await StopWarmupsAndDisposeRegistryAsync().ConfigureAwait(false);
      modelRegistry = modelRegistryFactory(settings, diagnostics);

      IReadOnlyList<ModelReadinessEntry> entries = await ResolveReadinessEntriesAsync(settings, cancellationToken)
        .ConfigureAwait(false);
      Publish(new ModelReadinessSnapshot(entries, DateTimeOffset.UtcNow));

      warmupCts = new CancellationTokenSource();
      List<Task> startedTasks = new();
      foreach (ModelReadinessEntry entry in entries.Where(entry => entry.State == ModelReadinessState.Pending))
      {
        startedTasks.Add(Task.Run(
          () => WarmModelAsync(entry, warmupCts.Token),
          CancellationToken.None));
      }

      warmupTasks = startedTasks;
    }
    finally
    {
      lifecycleLock.Release();
    }
  }

  public ITranscriptionService CreateTranscriptionService(AppSettings settings, IDiagnostics runtimeDiagnostics)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(runtimeDiagnostics);

    TranscriptionModelRegistry? registry = Volatile.Read(ref modelRegistry);
    if (registry is null)
    {
      diagnostics.Warning("Model readiness registry was not initialized; creating an owned transcription service fallback.");
      return fallbackTranscriptionServiceFactory(settings, runtimeDiagnostics);
    }

    TranscriptionModelSelection selection = RuntimeServiceSelection.ResolveTranscription(settings);
    return new TranscriptionService(
      selection.ProviderId,
      registry,
      runtimeDiagnostics,
      ownsModelRegistry: false);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    await lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
    try
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      await StopWarmupsAndDisposeRegistryAsync().ConfigureAwait(false);
    }
    finally
    {
      lifecycleLock.Release();
      lifecycleLock.Dispose();
    }
  }

  private async Task<IReadOnlyList<ModelReadinessEntry>> ResolveReadinessEntriesAsync(
    AppSettings settings,
    CancellationToken cancellationToken)
  {
    TranscriptionModelSelection activeSelection = RuntimeServiceSelection.ResolveTranscription(settings);
    IReadOnlyList<ModelInfo> models = await modelManager.GetModelsAsync(cancellationToken).ConfigureAwait(false);
    List<ModelReadinessEntry> entries = new();

    foreach (IGrouping<string, ModelInfo> providerGroup in models.GroupBy(model => model.ProviderId, StringComparer.OrdinalIgnoreCase))
    {
      ModelInfo? selected = providerGroup
        .Where(model => model.IsInstalled)
        .OrderByDescending(model => IsSelectedModel(model, activeSelection))
        .ThenByDescending(model => model.IsActive)
        .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();

      if (selected is not null)
      {
        bool shouldWarm = IsSelectedModel(selected, activeSelection);
        entries.Add(new ModelReadinessEntry(
          selected.ProviderId,
          selected.ModelId,
          selected.DisplayName,
          shouldWarm ? ModelReadinessState.Pending : ModelReadinessState.Ready,
          WarmupDuration: null,
          ErrorMessage: null));
        continue;
      }

      ModelInfo? first = providerGroup.OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
      if (first is not null)
      {
        entries.Add(new ModelReadinessEntry(
          first.ProviderId,
          first.ModelId,
          first.DisplayName,
          ModelReadinessState.NotInstalled,
          WarmupDuration: null,
          ErrorMessage: "No installed model is available for this provider."));
      }
    }

    return entries
      .OrderBy(entry => entry.ProviderId, StringComparer.OrdinalIgnoreCase)
      .ToArray();
  }

  private static bool IsSelectedModel(ModelInfo model, TranscriptionModelSelection selection)
  {
    return string.Equals(model.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(model.ModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Background readiness failures must update tray state without crashing the app.")]
  private async Task WarmModelAsync(ModelReadinessEntry entry, CancellationToken cancellationToken)
  {
    UpdateEntry(entry.ProviderId, entry.ModelId, ModelReadinessState.Warming, null, null);
    Stopwatch stopwatch = Stopwatch.StartNew();

    try
    {
      TranscriptionModelRegistry? registry = Volatile.Read(ref modelRegistry);
      if (registry is null
          || !registry.TryResolve(entry.ProviderId, out ITranscriptionModel? model)
          || model is null)
      {
        throw new InvalidOperationException(
          $"No transcription model is registered for provider '{entry.ProviderId}'.");
      }

      if (model is ITranscriptionModelWarmup warmup)
      {
        await warmup.WarmUpAsync(entry.ModelId, cancellationToken).ConfigureAwait(false);
      }

      stopwatch.Stop();
      UpdateEntry(entry.ProviderId, entry.ModelId, ModelReadinessState.Ready, stopwatch.Elapsed, null);
      diagnostics.Info(
        string.Format(
          CultureInfo.InvariantCulture,
          "Model readiness warmup completed for provider '{0}' model '{1}' in {2:F0} ms.",
          entry.ProviderId,
          entry.ModelId,
          stopwatch.Elapsed.TotalMilliseconds));
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
      stopwatch.Stop();
      string message = ex.Message;
      UpdateEntry(entry.ProviderId, entry.ModelId, ModelReadinessState.Failed, stopwatch.Elapsed, message);
      diagnostics.Warning(
        string.Format(
          CultureInfo.InvariantCulture,
          "Model readiness warmup failed for provider '{0}' model '{1}' after {2:F0} ms: {3}",
          entry.ProviderId,
          entry.ModelId,
          stopwatch.Elapsed.TotalMilliseconds,
          message));
    }
  }

  private void UpdateEntry(
    string providerId,
    string modelId,
    ModelReadinessState state,
    TimeSpan? warmupDuration,
    string? errorMessage)
  {
    ModelReadinessSnapshot current = CurrentSnapshot;
    ModelReadinessEntry[] updated = current.Entries
      .Select(entry =>
        string.Equals(entry.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.ModelId, modelId, StringComparison.OrdinalIgnoreCase)
          ? entry with
          {
            State = state,
            WarmupDuration = warmupDuration,
            ErrorMessage = errorMessage,
          }
          : entry)
      .ToArray();

    Publish(new ModelReadinessSnapshot(updated, DateTimeOffset.UtcNow));
  }

  private void Publish(ModelReadinessSnapshot nextSnapshot)
  {
    lock (snapshotSync)
    {
      snapshot = nextSnapshot;
    }

    SnapshotChanged?.Invoke(this, nextSnapshot);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Shutdown cancels best-effort background warmups and must tolerate provider cleanup races.")]
  private async Task StopWarmupsAndDisposeRegistryAsync()
  {
    CancellationTokenSource? ctsToDispose = warmupCts;
    warmupCts = null;
    List<Task> tasksToAwait = warmupTasks;
    warmupTasks = new List<Task>();
    if (ctsToDispose is not null)
    {
      ctsToDispose.Cancel();
      try
      {
        await Task.WhenAll(tasksToAwait).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
      }
      catch (Exception)
      {
      }
      finally
      {
        ctsToDispose.Dispose();
      }
    }

    TranscriptionModelRegistry? registryToDispose = modelRegistry;
    modelRegistry = null;
    if (registryToDispose is not null)
    {
      foreach (ITranscriptionModel model in registryToDispose.GetModels())
      {
        if (model is IAsyncDisposable disposable)
        {
          await disposable.DisposeAsync().ConfigureAwait(false);
        }
      }
    }
  }
}
