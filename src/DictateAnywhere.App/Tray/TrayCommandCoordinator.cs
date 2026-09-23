using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DictateAnywhere.App.Runtime;
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
  private readonly DispatcherTimer statusTimer;
  private Task? disposalTask;
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

  public void SetStatus(DictationSessionState state) => trayHost.SetStatus(state);

  public void SetModelReadiness(ModelReadinessSnapshot snapshot) => trayHost.SetModelReadiness(snapshot);

  public void SetStartOnLoginEnabled(bool enabled) => trayHost.SetStartOnLoginEnabled(enabled);

  public void SetModelMenu(IReadOnlyList<ModelInfo> models, TranscriptionModelSelection selection) =>
    trayHost.SetModelMenu(models, selection);

  public void ShowNotification(string title, string message, Forms.ToolTipIcon icon) =>
    trayHost.ShowNotification(title, message, icon);

  public Task RunAsync(Func<Task> action)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(action);
    return RunCoreAsync(action, waitForTurn: true);
  }

  public async Task<bool> TryRunAsync(Func<Task> action)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(action);
    return await RunCoreAsync(action, waitForTurn: false).ConfigureAwait(true);
  }

  public ValueTask DisposeAsync()
  {
    disposalTask ??= DisposeCoreAsync();
    return new ValueTask(disposalTask);
  }

  private async Task DisposeCoreAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    statusTimer.Stop();
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
      commandLock.Dispose();
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Tray event commands are an application boundary; all failures must be observed and presented without crashing the dispatcher.")]
  private async Task<bool> RunCoreAsync(Func<Task> action, bool waitForTurn)
  {
    bool entered = waitForTurn
      ? await WaitForTurnAsync().ConfigureAwait(true)
      : await commandLock.WaitAsync(0).ConfigureAwait(true);
    if (!entered)
    {
      return false;
    }

    try
    {
      await action().ConfigureAwait(true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ModelManagementException)
    {
      handlers.ReportFailure("Tray action", "Tray action failed.", ex);
    }
    catch (Exception ex)
    {
      handlers.ReportFailure("Tray action", "Tray action failed unexpectedly.", ex);
    }
    finally
    {
      commandLock.Release();
    }

    return true;
  }

  private async Task<bool> WaitForTurnAsync()
  {
    await commandLock.WaitAsync().ConfigureAwait(true);
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
  private void OnStatusTimerTick(object? sender, EventArgs e) => trayHost.SetStatus(getRuntimeState());
}
