using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.Core.Services;

/// <summary>
/// Keeps presentation failures from blocking the dictation pipeline. Overlay feedback is important,
/// but it must never prevent captured speech from reaching insertion and history.
/// </summary>
public sealed class FaultTolerantOverlayService : IOverlayService, IAsyncDisposable
{
  public static TimeSpan DefaultOperationTimeout { get; } = TimeSpan.FromSeconds(2);

  private readonly IOverlayService inner;
  private readonly IDiagnostics diagnostics;
  private readonly TimeSpan operationTimeout;
  private readonly object lateOperationSync = new();
  private Task lateOperationObservation = Task.CompletedTask;
  private bool disposed;

  public FaultTolerantOverlayService(
    IOverlayService inner,
    IDiagnostics diagnostics,
    TimeSpan? operationTimeout = null)
  {
    this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.operationTimeout = operationTimeout ?? DefaultOperationTimeout;
    if (this.operationTimeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(operationTimeout), "Overlay operation timeout must be greater than zero.");
    }
  }

  public Task ShowStateAsync(
    DictationSessionState state,
    string? message = null,
    TimeSpan? elapsed = null,
    OverlayDisplayOptions? display = null,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    return ExecuteAsync(
      token => inner.ShowStateAsync(state, message, elapsed, display, token),
      $"show {state}",
      cancellationToken);
  }

  public Task HideAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    return ExecuteAsync(inner.HideAsync, "hide", cancellationToken);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    Task pendingLateOperations;
    lock (lateOperationSync)
    {
      pendingLateOperations = lateOperationObservation;
    }

    try
    {
      await pendingLateOperations.WaitAsync(operationTimeout).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
      diagnostics.Warning("Timed-out overlay work did not settle before overlay shutdown; teardown will continue.");
    }

    if (inner is IAsyncDisposable asyncDisposable)
    {
      await asyncDisposable.DisposeAsync().ConfigureAwait(false);
    }
    else if (inner is IDisposable disposable)
    {
      disposable.Dispose();
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Overlay presentation is a non-critical boundary and must not interrupt dictation processing.")]
  private async Task ExecuteAsync(
    Func<CancellationToken, Task> operationFactory,
    string operationName,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(operationTimeout);

    Task? operation = null;
    try
    {
      operation = operationFactory(timeoutCts.Token);
      await operation.WaitAsync(operationTimeout, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      TrackLateOperation(operation, operationName);
      diagnostics.Warning($"Overlay operation '{operationName}' timed out after {operationTimeout.TotalMilliseconds:F0} ms; dictation will continue.");
    }
    catch (TimeoutException)
    {
      TrackLateOperation(operation, operationName);
      diagnostics.Warning($"Overlay operation '{operationName}' timed out after {operationTimeout.TotalMilliseconds:F0} ms; dictation will continue.");
    }
    catch (Exception ex)
    {
      diagnostics.Warning($"Overlay operation '{operationName}' failed; dictation will continue. {ex.Message}");
    }
  }

  private void TrackLateOperation(Task? operation, string operationName)
  {
    if (operation is null)
    {
      return;
    }

    Task observer = ObserveLateOperationAsync(operation, operationName);
    lock (lateOperationSync)
    {
      Task previous = lateOperationObservation.IsCompleted
        ? Task.CompletedTask
        : lateOperationObservation;
      lateOperationObservation = Task.WhenAll(previous, observer);
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This observer is the terminal boundary for work that already exceeded its user-facing timeout.")]
  private async Task ObserveLateOperationAsync(Task operation, string operationName)
  {
    try
    {
      await operation.ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      diagnostics.Warning($"Timed-out overlay operation '{operationName}' later failed: {ex.Message}");
    }
  }
}
