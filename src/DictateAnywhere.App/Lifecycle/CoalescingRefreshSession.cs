using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Lifecycle;

/// <summary>Coalesces refresh requests, prevents overlapping passes, and owns shutdown.</summary>
internal sealed class CoalescingRefreshSession : IAsyncDisposable
{
  private readonly object sync = new();
  private readonly Func<CancellationToken, Task> refreshAsync;
  private readonly CancellationTokenSource lifetimeCancellationSource = new();
  private TaskCompletionSource? activeRunCompletion;
  private Task activeRunnerTask = Task.CompletedTask;
  private Task? disposalTask;
  private bool refreshRequested;
  private bool disposed;

  public CoalescingRefreshSession(Func<CancellationToken, Task> refreshAsync)
  {
    this.refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
  }

  public bool IsRunning
  {
    get
    {
      lock (sync)
      {
        return activeRunCompletion is not null;
      }
    }
  }

  /// <summary>
  /// Requests a refresh pass. The caller that starts the run owns the returned task;
  /// requests coalesced into that run return <see langword="false" />.
  /// </summary>
  public bool TryRequestRefresh(out Task refreshRun)
  {
    TaskCompletionSource runCompletion;
    lock (sync)
    {
      if (disposed)
      {
        refreshRun = Task.CompletedTask;
        return false;
      }

      refreshRequested = true;
      if (activeRunCompletion is not null)
      {
        refreshRun = activeRunCompletion.Task;
        return false;
      }

      runCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      activeRunCompletion = runCompletion;
      refreshRun = runCompletion.Task;
    }

    Task runnerTask = RunAsync(runCompletion);
    lock (sync)
    {
      activeRunnerTask = runnerTask;
    }

    return true;
  }

  public ValueTask DisposeAsync()
  {
    Task activeRun;
    lock (sync)
    {
      if (disposalTask is not null)
      {
        return new ValueTask(disposalTask);
      }

      disposed = true;
      refreshRequested = false;
      activeRun = Task.WhenAll(
        activeRunCompletion?.Task ?? Task.CompletedTask,
        activeRunnerTask);
      disposalTask = CompleteDisposalAsync(activeRun);
    }

    TryCancel(lifetimeCancellationSource);
    return new ValueTask(disposalTask);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The session must settle after any refresh failure and return the first failure to the initiating boundary.")]
  private async Task RunAsync(TaskCompletionSource runCompletion)
  {
    Exception? firstFailure = null;
    while (TryBeginPass(runCompletion))
    {
      try
      {
        await refreshAsync(lifetimeCancellationSource.Token).ConfigureAwait(true);
      }
      catch (OperationCanceledException) when (lifetimeCancellationSource.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        firstFailure ??= ex;
      }
    }

    ReleaseRun(runCompletion);
    CompleteRun(runCompletion, firstFailure);
  }

  private bool TryBeginPass(TaskCompletionSource runCompletion)
  {
    lock (sync)
    {
      if (disposed || !ReferenceEquals(activeRunCompletion, runCompletion) || !refreshRequested)
      {
        if (ReferenceEquals(activeRunCompletion, runCompletion))
        {
          activeRunCompletion = null;
        }

        return false;
      }

      refreshRequested = false;
      return true;
    }
  }

  private static void CompleteRun(TaskCompletionSource runCompletion, Exception? failure)
  {
    if (failure is null)
    {
      runCompletion.TrySetResult();
    }
    else
    {
      runCompletion.TrySetException(failure);
    }
  }

  private void ReleaseRun(TaskCompletionSource runCompletion)
  {
    lock (sync)
    {
      if (ReferenceEquals(activeRunCompletion, runCompletion))
      {
        activeRunCompletion = null;
      }
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The initiating caller observes refresh failures; disposal must still settle and release lifecycle resources.")]
  private async Task CompleteDisposalAsync(Task activeRun)
  {
    try
    {
      await activeRun.ConfigureAwait(true);
    }
    catch (Exception)
    {
      // The caller that started the refresh run owns failure reporting.
    }
    finally
    {
      lifetimeCancellationSource.Dispose();
    }
  }

  private static void TryCancel(CancellationTokenSource cancellationSource)
  {
    try
    {
      cancellationSource.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // A repeated shutdown request can race with completed disposal.
    }
  }
}
