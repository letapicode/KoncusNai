using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Models;
using Forms = System.Windows.Forms;

namespace DictateAnywhere.App.Tray;

internal sealed record TrayCommandHandlers(
  Func<Task> OpenSettingsAsync,
  Func<Task> OpenWorkbenchAsync,
  Func<Task> OpenHistoryAsync,
  Func<bool, Task> ApplyStartupToggleAsync,
  Func<Task> RetryLastDictationAsync,
  Func<TranscriptionModelSelection, Task> ApplyQuickModelSwitchAsync,
  Func<Task> ExportDiagnosticsAsync,
  Action Quit,
  Action<string, string, Exception> ReportFailure);

/// <summary>Owns the tray surface, its event wiring, status timer, and serialized command execution.</summary>
internal sealed class TrayCommandCoordinator : IAsyncDisposable
{
  private readonly ITrayIconHost trayHost;
  private readonly TrayCommandHandlers handlers;
  private readonly Func<DictationSessionState> getRuntimeState;
  private readonly SemaphoreSlim commandLock = new(1, 1);
  private readonly CancellationTokenSource shutdownCancellation = new();
  private readonly DispatcherTimer statusTimer;
  private Task? disposalTask;
  private bool shuttingDown;
  private bool disposed;

  public TrayCommandCoordinator(
    ITrayIconHost trayHost,
    TrayCommandHandlers handlers,
    Func<DictationSessionState> getRuntimeState)
  {
    this.trayHost = trayHost ?? throw new ArgumentNullException(nameof(trayHost));
    this.handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    this.getRuntimeState = getRuntimeState ?? throw new ArgumentNullException(nameof(getRuntimeState));

    trayHost.OpenSettingsRequested += OnOpenSettingsRequested;
    trayHost.OpenWorkbenchRequested += OnOpenWorkbenchRequested;
    trayHost.OpenHistoryRequested += OnOpenHistoryRequested;
    trayHost.StartupToggleRequested += OnStartupToggleRequested;
    trayHost.RetryLastDictationRequested += OnRetryLastDictationRequested;
    trayHost.QuickModelSwitchRequested += OnQuickModelSwitchRequested;
    trayHost.ExportDiagnosticsRequested += OnExportDiagnosticsRequested;
    trayHost.QuitRequested += OnQuitRequested;

    statusTimer = new DispatcherTimer
    {
      Interval = TimeSpan.FromMilliseconds(250),
    };
    statusTimer.Tick += OnStatusTimerTick;
  }

  public void Start(ModelReadinessSnapshot readiness, bool startOnLoginEnabled)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    trayHost.SetStatus(getRuntimeState());
    trayHost.SetModelReadiness(readiness ?? ModelReadinessSnapshot.Empty);
    trayHost.SetStartOnLoginEnabled(startOnLoginEnabled);
    statusTimer.Start();
  }

  internal bool IsDisposed => disposalTask?.IsCompletedSuccessfully == true;

  public void SetStatus(DictationSessionState state)
  {
    if (!shuttingDown && !disposed) trayHost.SetStatus(state);
  }

  public void SetModelReadiness(ModelReadinessSnapshot snapshot)
  {
    if (!shuttingDown && !disposed) trayHost.SetModelReadiness(snapshot);
  }

  public void SetStartOnLoginEnabled(bool enabled)
  {
    if (!shuttingDown && !disposed) trayHost.SetStartOnLoginEnabled(enabled);
  }

  public void SetModelMenu(IReadOnlyList<ModelInfo> models, TranscriptionModelSelection selection)
  {
    if (!shuttingDown && !disposed) trayHost.SetModelMenu(models, selection);
  }

  public void ShowNotification(string title, string message, Forms.ToolTipIcon icon)
  {
    if (!shuttingDown && !disposed) trayHost.ShowNotification(title, message, icon);
  }

  public void BeginShutdown()
  {
    if (shuttingDown || disposed) return;
    shuttingDown = true;
    statusTimer.Stop();
    try { shutdownCancellation.Cancel(); }
    finally { trayHost.BeginShutdown(); }
  }

  public Task RunAsync(Func<Task> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    if (shuttingDown || disposed) return Task.CompletedTask;
    return RunCoreAsync(action, waitForTurn: true);
  }

  public async Task<bool> TryRunAsync(Func<Task> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    if (shuttingDown || disposed) return false;
    return await RunCoreAsync(action, waitForTurn: false).ConfigureAwait(true);
  }

  public ValueTask DisposeAsync()
  {
    disposalTask ??= DisposeCoreAsync();
    return new ValueTask(disposalTask);
  }

  private async Task DisposeCoreAsync()
  {
    await Task.Yield();
    await LifecycleCleanup.RunAsync(
      LifecycleCleanup.Sync("Stop tray input", BeginShutdown),
      new CleanupStep("Tray resources", DisposeHostAsync)).ConfigureAwait(true);
  }

  private async Task DisposeHostAsync()
  {
    disposed = true;
    statusTimer.Tick -= OnStatusTimerTick;
    trayHost.OpenSettingsRequested -= OnOpenSettingsRequested;
    trayHost.OpenWorkbenchRequested -= OnOpenWorkbenchRequested;
    trayHost.OpenHistoryRequested -= OnOpenHistoryRequested;
    trayHost.StartupToggleRequested -= OnStartupToggleRequested;
    trayHost.RetryLastDictationRequested -= OnRetryLastDictationRequested;
    trayHost.QuickModelSwitchRequested -= OnQuickModelSwitchRequested;
    trayHost.ExportDiagnosticsRequested -= OnExportDiagnosticsRequested;
    trayHost.QuitRequested -= OnQuitRequested;
    await commandLock.WaitAsync().ConfigureAwait(true);
    try
    {
      trayHost.Dispose();
    }
    finally
    {
      commandLock.Release();
      // Cancelled queued callers may still be unwinding; do not invalidate
      // their token/semaphore while they release their accepted command scope.
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Tray event commands are an application boundary; all failures must be observed and presented without crashing the dispatcher.")]
  private async Task<bool> RunCoreAsync(Func<Task> action, bool waitForTurn)
  {
    bool entered;
    try
    {
      entered = waitForTurn
        ? await WaitForTurnAsync().ConfigureAwait(true)
        : await commandLock.WaitAsync(0, shutdownCancellation.Token).ConfigureAwait(true);
    }
    catch (OperationCanceledException) when (shuttingDown)
    {
      return false;
    }
    if (!entered)
    {
      return false;
    }

    try
    {
      if (shuttingDown) return false;
      await action().ConfigureAwait(true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ModelManagementException)
    {
      if (!shuttingDown) ReportFailureSafely("Tray action failed.", ex);
    }
    catch (Exception ex)
    {
      if (!shuttingDown) ReportFailureSafely("Tray action failed unexpectedly.", ex);
    }
    finally
    {
      commandLock.Release();
    }

    return true;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "The error reporter is the final tray event boundary; its own failure must not crash the dispatcher.")]
  private void ReportFailureSafely(string message, Exception exception)
  {
    LifecycleCleanup.Report((_, error) => handlers.ReportFailure("Tray action", message, error),
      "Tray error reporting", exception);
  }

  private async Task<bool> WaitForTurnAsync()
  {
    await commandLock.WaitAsync(shutdownCancellation.Token).ConfigureAwait(true);
    return true;
  }

  private async void OnOpenSettingsRequested(object? sender, EventArgs e) => await RunAsync(handlers.OpenSettingsAsync).ConfigureAwait(true);
  private async void OnOpenWorkbenchRequested(object? sender, EventArgs e) => await RunAsync(handlers.OpenWorkbenchAsync).ConfigureAwait(true);
  private async void OnOpenHistoryRequested(object? sender, EventArgs e) => await RunAsync(handlers.OpenHistoryAsync).ConfigureAwait(true);
  private async void OnStartupToggleRequested(object? sender, bool enabled) => await RunAsync(() => handlers.ApplyStartupToggleAsync(enabled)).ConfigureAwait(true);
  private async void OnRetryLastDictationRequested(object? sender, EventArgs e) => await RunAsync(handlers.RetryLastDictationAsync).ConfigureAwait(true);
  private async void OnQuickModelSwitchRequested(object? sender, TranscriptionModelSelection selection) => await RunAsync(() => handlers.ApplyQuickModelSwitchAsync(selection)).ConfigureAwait(true);
  private async void OnExportDiagnosticsRequested(object? sender, EventArgs e) => await RunAsync(handlers.ExportDiagnosticsAsync).ConfigureAwait(true);
  private void OnQuitRequested(object? sender, EventArgs e) => handlers.Quit();
  private void OnStatusTimerTick(object? sender, EventArgs e)
  {
    if (!shuttingDown) trayHost.SetStatus(getRuntimeState());
  }
}
