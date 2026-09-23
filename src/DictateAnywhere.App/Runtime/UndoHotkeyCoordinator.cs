using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Runtime;

internal sealed class UndoHotkeyCoordinator : IAsyncDisposable
{
  private readonly IHotkeyService hotkeyService;
  private readonly IUndoInsertionService undoInsertionService;
  private readonly IDiagnostics diagnostics;
  private readonly HotkeyBinding undoHotkeyBinding;
  private readonly object operationSync = new();

  private Task activeUndoOperation = Task.CompletedTask;
  private bool started;
  private bool disposed;

  public UndoHotkeyCoordinator(
    IHotkeyService hotkeyService,
    IUndoInsertionService undoInsertionService,
    IDiagnostics diagnostics,
    HotkeyBinding undoHotkeyBinding)
  {
    this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
    this.undoInsertionService = undoInsertionService ?? throw new ArgumentNullException(nameof(undoInsertionService));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.undoHotkeyBinding = undoHotkeyBinding;
  }

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (started)
    {
      return;
    }

    HotkeyRegistrationResult registration = await hotkeyService.RegisterAsync(undoHotkeyBinding, cancellationToken).ConfigureAwait(false);
    if (!registration.Success)
    {
      string message = registration.ErrorMessage
        ?? $"Unable to register undo hotkey '{HotkeyFormatter.ToDisplayString(undoHotkeyBinding)}'.";
      throw new InvalidOperationException(message);
    }

    lock (operationSync)
    {
      hotkeyService.HotkeyPressed += OnUndoHotkeyPressed;
      started = true;
    }
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    Task operationToAwait;
    lock (operationSync)
    {
      if (!started)
      {
        return;
      }

      hotkeyService.HotkeyPressed -= OnUndoHotkeyPressed;
      started = false;
      operationToAwait = activeUndoOperation;
    }

    await hotkeyService.UnregisterAsync(cancellationToken).ConfigureAwait(false);
    await operationToAwait.WaitAsync(cancellationToken).ConfigureAwait(false);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    Task operationToAwait;
    lock (operationSync)
    {
      disposed = true;
      started = false;
      hotkeyService.HotkeyPressed -= OnUndoHotkeyPressed;
      operationToAwait = activeUndoOperation;
    }

    try
    {
      await hotkeyService.UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
    }
    catch (InvalidOperationException)
    {
      // Ignore hotkey teardown failures during app shutdown.
    }

    await operationToAwait.ConfigureAwait(false);
  }

  private void OnUndoHotkeyPressed(object? sender, HotkeyEventArgs e)
  {
    lock (operationSync)
    {
      if (!started || disposed || !activeUndoOperation.IsCompleted)
      {
        return;
      }

      activeUndoOperation = HandleUndoRequestedAsync();
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Undo hotkey handler is an event boundary; failures are logged and must not crash the app.")]
  private async Task HandleUndoRequestedAsync()
  {
    try
    {
      UndoInsertionResult undoResult = await undoInsertionService.UndoLastInsertionAsync().ConfigureAwait(false);
      if (undoResult.Success)
      {
        diagnostics.Info("Undo hotkey succeeded for last insertion.");
      }
      else
      {
        diagnostics.Warning($"Undo hotkey ignored: {undoResult.ErrorMessage}");
      }
    }
    catch (OperationCanceledException)
    {
      diagnostics.Warning("Undo hotkey canceled.");
    }
    catch (Exception ex)
    {
      diagnostics.Warning($"Undo hotkey failed: {ex.Message}");
    }
  }
}
