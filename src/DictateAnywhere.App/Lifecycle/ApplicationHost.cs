using DictateAnywhere.Core.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.App.Lifecycle;

internal interface IApplicationRuntimeSession : IRuntimeSupervisor, IAsyncDisposable
{
  DictationSessionState CurrentState { get; }
  Task StopAsync(CancellationToken cancellationToken = default);
  Task<bool> TryRestartWhenIdleAsync(CancellationToken cancellationToken = default);
  RuntimeStartupNotice? ConsumeStartupNotice();
}

internal interface IModelReadinessSession : IAsyncDisposable
{
  event EventHandler<ModelReadinessSnapshot>? SnapshotChanged;
  ModelReadinessSnapshot CurrentSnapshot { get; }
  Task RefreshAsync(AppSettings settings, CancellationToken cancellationToken = default);
  ITranscriptionService CreateTranscriptionService(AppSettings settings, IDiagnostics diagnostics);
}

internal sealed record RuntimeSettingsApplyResult(
  bool IsRunning,
  bool Deferred,
  RuntimeStartupNotice? Notice,
  Exception? Failure);

/// <summary>Owns model-readiness and dictation-runtime startup, replacement, pending settings, and shutdown.</summary>
internal sealed class ApplicationHost : IRuntimeSupervisor, IAsyncDisposable
{
  private readonly IApplicationRuntimeSession runtime;
  private readonly IModelReadinessSession modelReadiness;
  private readonly IDiagnostics diagnostics;
  private readonly SemaphoreSlim lifecycleLock = new(1, 1);
  private AppSettings? activeSettings;
  private AppSettings? pendingSettings;
  private volatile bool disposed;
  private readonly object disposalSync = new();
  private readonly CancellationTokenSource stopping = new();
  private Task? disposalTask;
  private Task runtimeStopping = Task.CompletedTask;

  public ApplicationHost(
    IApplicationRuntimeSession runtime,
    IModelReadinessSession modelReadiness,
    IDiagnostics diagnostics)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    this.modelReadiness = modelReadiness ?? throw new ArgumentNullException(nameof(modelReadiness));
    this.diagnostics = DiagnosticBoundary.Wrap(diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
    this.modelReadiness.SnapshotChanged += OnModelReadinessSnapshotChanged;
  }

  public event EventHandler<ModelReadinessSnapshot>? ModelReadinessChanged;

  public bool IsRunning => runtime.IsRunning;
  public DictationSessionState CurrentState => runtime.CurrentState;
  public ModelReadinessSnapshot CurrentReadiness => modelReadiness.CurrentSnapshot;
  public bool HasPendingSettings => pendingSettings is not null;

  public ITranscriptionService CreateTranscriptionService(AppSettings settings, IDiagnostics runtimeDiagnostics) =>
    disposed ? throw new ObjectDisposedException(nameof(ApplicationHost))
      : modelReadiness.CreateTranscriptionService(settings, runtimeDiagnostics);

  public async Task RefreshReadinessAsync(AppSettings settings, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
    await lifecycleLock.WaitAsync(linked.Token).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      await modelReadiness.RefreshAsync(settings, linked.Token).ConfigureAwait(false);
    }
    finally { lifecycleLock.Release(); }
  }

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
    await lifecycleLock.WaitAsync(linked.Token).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      await runtime.StartAsync(linked.Token).ConfigureAwait(false);
    }
    finally { lifecycleLock.Release(); }
  }

  public async Task<RuntimeSettingsApplyResult> ApplyRuntimeSettingsAsync(
    AppSettings settings,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ObjectDisposedException.ThrowIf(disposed, this);
    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
    cancellationToken = linked.Token;
    await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (runtime.IsRunning && !RuntimeSettingsRestartPolicy.RequiresRestart(activeSettings, settings))
      {
        pendingSettings = null;
        return new RuntimeSettingsApplyResult(true, false, null, null);
      }

      if (runtime.IsRunning && runtime.CurrentState != DictationSessionState.Idle)
      {
        pendingSettings = settings;
        DiagnosticBoundary.Report(() => diagnostics.Info($"Runtime settings update deferred until dictation is idle (state={runtime.CurrentState})."));
        return new RuntimeSettingsApplyResult(true, true, null, null);
      }

      await modelReadiness.RefreshAsync(settings, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      bool started;
      try
      {
        started = runtime.IsRunning
          ? await runtime.TryRestartWhenIdleAsync(cancellationToken).ConfigureAwait(false)
          : await StartRuntimeAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
      {
        pendingSettings = settings;
        return new RuntimeSettingsApplyResult(false, false, null, ex);
      }

      RuntimeStartupNotice? notice = started ? runtime.ConsumeStartupNotice() : null;
      if (!started)
      {
        pendingSettings = settings;
        return new RuntimeSettingsApplyResult(false, false, notice, null);
      }

      activeSettings = settings;
      pendingSettings = null;
      return new RuntimeSettingsApplyResult(true, false, notice, null);
    }
    finally
    {
      lifecycleLock.Release();
    }
  }

  public async Task<RuntimeSettingsApplyResult?> TryApplyPendingSettingsAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    AppSettings? pending = pendingSettings;
    if (pending is null || runtime.CurrentState != DictationSessionState.Idle)
    {
      return null;
    }

    return await ApplyRuntimeSettingsAsync(pending, cancellationToken).ConfigureAwait(false);
  }

  public void BeginShutdown()
  {
    TaskCompletionSource source;
    lock (disposalSync)
    {
      if (disposed) return;
      disposed = true;
      source = new(TaskCreationOptions.RunContinuationsAsynchronously);
      runtimeStopping = source.Task;
    }
    // Do not hold the publication lock while user cancellation callbacks run.
    _ = LifecycleCleanup.ObserveAsync(runtimeStopping, diagnostics.Error, "Runtime stop");
    _ = StopCoreAsync(source);
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Transfers every stop failure to the published, owned completion task.")]
  private async Task StopCoreAsync(TaskCompletionSource source)
  {
    try
    {
      await LifecycleCleanup.RunAsync(
        LifecycleCleanup.Sync("Detach readiness", () => modelReadiness.SnapshotChanged -= OnModelReadinessSnapshotChanged),
        LifecycleCleanup.Sync("Cancel lifecycle operations", stopping.Cancel),
        new CleanupStep("Stop runtime admission", () => runtime.StopAsync())).ConfigureAwait(false);
      source.TrySetResult();
    }
    catch (Exception error) { source.TrySetException(error); }
  }

  public ValueTask DisposeAsync()
  {
    Task task;
    lock (disposalSync) task = disposalTask ??= DisposeCoreAsync();
    BeginShutdown(); // Close admission before returning, including direct disposal callers.
    return new ValueTask(task);
  }

  private async Task DisposeCoreAsync()
  {
    await Task.Yield(); // Publish the shared disposal task before cancellation callbacks run.
    await LifecycleCleanup.RunAsync(
      LifecycleCleanup.Sync("Stop runtime admission", BeginShutdown),
      new CleanupStep("Drain runtime lifecycle", async () =>
      {
        await lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
          await LifecycleCleanup.RunAsync(
            new CleanupStep("Stop dictation runtime", () => runtimeStopping),
            new CleanupStep("Dictation runtime", () => runtime.DisposeAsync().AsTask()),
            new CleanupStep("Model readiness", () => modelReadiness.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
        finally { lifecycleLock.Release(); }
      })).ConfigureAwait(false);
    // Cancellation registrations/queued callers can still unwind after cancellation.
    // These managed synchronization objects are reclaimed with this host.
  }

  private async Task<bool> StartRuntimeAsync(CancellationToken cancellationToken)
  {
    await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
    return runtime.IsRunning;
  }

  private void OnModelReadinessSnapshotChanged(object? sender, ModelReadinessSnapshot snapshot) =>
    ModelReadinessChanged?.Invoke(this, snapshot);
}
