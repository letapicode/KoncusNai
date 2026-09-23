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
  private bool disposed;

  public ApplicationHost(
    IApplicationRuntimeSession runtime,
    IModelReadinessSession modelReadiness,
    IDiagnostics diagnostics)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    this.modelReadiness = modelReadiness ?? throw new ArgumentNullException(nameof(modelReadiness));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.modelReadiness.SnapshotChanged += OnModelReadinessSnapshotChanged;
  }

  public event EventHandler<ModelReadinessSnapshot>? ModelReadinessChanged;

  public bool IsRunning => runtime.IsRunning;
  public DictationSessionState CurrentState => runtime.CurrentState;
  public ModelReadinessSnapshot CurrentReadiness => modelReadiness.CurrentSnapshot;
  public bool HasPendingSettings => pendingSettings is not null;

  public ITranscriptionService CreateTranscriptionService(AppSettings settings, IDiagnostics runtimeDiagnostics) =>
    modelReadiness.CreateTranscriptionService(settings, runtimeDiagnostics);

  public Task RefreshReadinessAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
    modelReadiness.RefreshAsync(settings, cancellationToken);

  public Task StartAsync(CancellationToken cancellationToken = default) => runtime.StartAsync(cancellationToken);

  public async Task<RuntimeSettingsApplyResult> ApplyRuntimeSettingsAsync(
    AppSettings settings,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ObjectDisposedException.ThrowIf(disposed, this);
    await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (runtime.IsRunning && !RuntimeSettingsRestartPolicy.RequiresRestart(activeSettings, settings))
      {
        pendingSettings = null;
        return new RuntimeSettingsApplyResult(true, false, null, null);
      }

      if (runtime.IsRunning && runtime.CurrentState != DictationSessionState.Idle)
      {
        pendingSettings = settings;
        diagnostics.Info($"Runtime settings update deferred until dictation is idle (state={runtime.CurrentState}).");
        return new RuntimeSettingsApplyResult(true, true, null, null);
      }

      await modelReadiness.RefreshAsync(settings, cancellationToken).ConfigureAwait(false);
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

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    modelReadiness.SnapshotChanged -= OnModelReadinessSnapshotChanged;
    await lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
    try
    {
      await runtime.DisposeAsync().ConfigureAwait(false);
      await modelReadiness.DisposeAsync().ConfigureAwait(false);
    }
    finally
    {
      lifecycleLock.Release();
      lifecycleLock.Dispose();
    }
  }

  private async Task<bool> StartRuntimeAsync(CancellationToken cancellationToken)
  {
    await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
    return runtime.IsRunning;
  }

  private void OnModelReadinessSnapshotChanged(object? sender, ModelReadinessSnapshot snapshot) =>
    ModelReadinessChanged?.Invoke(this, snapshot);
}
