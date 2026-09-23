using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Lifecycle;

internal readonly record struct LatestOperationResult<T>(bool Completed, T? Value)
{
  public static LatestOperationResult<T> Succeeded(T value) => new(true, value);

  public static LatestOperationResult<T> Canceled() => new(false, default);
}

/// <summary>Serializes replaceable operations and owns cancellation and ordered shutdown.</summary>
internal sealed class LatestOperationSession : IAsyncDisposable
{
  private readonly object sync = new();
  private ActiveOperation? activeOperation;
  private Task? disposalTask;
  private bool disposed;

  public bool IsRunning
  {
    get
    {
      lock (sync)
      {
        return activeOperation is not null;
      }
    }
  }

  public async Task<bool> RunLatestAsync(
    Func<CancellationToken, Task> operation,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(operation);

    LatestOperationResult<bool> result = await RunLatestAsync(
      async token =>
      {
        await operation(token).ConfigureAwait(true);
        return true;
      },
      cancellationToken).ConfigureAwait(true);
    return result.Completed;
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The active operation owns the cancellation source and disposes it when the operation completes.")]
  public Task<LatestOperationResult<T>> RunLatestAsync<T>(
    Func<CancellationToken, Task<T>> operation,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(operation);
    cancellationToken.ThrowIfCancellationRequested();

    ActiveOperation current;
    ActiveOperation? predecessor;
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      predecessor = activeOperation;
      CancellationTokenSource cancellationSource = cancellationToken.CanBeCanceled
        ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        : new CancellationTokenSource();
      current = new ActiveOperation(
        cancellationSource,
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
      activeOperation = current;
    }

    if (predecessor is not null)
    {
      TryCancel(predecessor.CancellationSource);
    }

    return RunAsync(current, predecessor?.Completion.Task ?? Task.CompletedTask, operation);
  }

  public bool Cancel()
  {
    CancellationTokenSource? cancellationSource;
    lock (sync)
    {
      cancellationSource = activeOperation?.CancellationSource;
    }

    return cancellationSource is not null && TryCancel(cancellationSource);
  }

  public Task CancelAndWaitAsync()
  {
    CancellationTokenSource? cancellationSource;
    Task completion;
    lock (sync)
    {
      cancellationSource = activeOperation?.CancellationSource;
      completion = activeOperation?.Completion.Task ?? Task.CompletedTask;
    }

    if (cancellationSource is not null)
    {
      TryCancel(cancellationSource);
    }

    return completion;
  }

  public ValueTask DisposeAsync()
  {
    CancellationTokenSource? cancellationSource;
    Task completion;
    lock (sync)
    {
      if (disposalTask is not null)
      {
        return new ValueTask(disposalTask);
      }

      disposed = true;
      cancellationSource = activeOperation?.CancellationSource;
      completion = activeOperation?.Completion.Task ?? Task.CompletedTask;
      disposalTask = completion;
    }

    if (cancellationSource is not null)
    {
      TryCancel(cancellationSource);
    }

    return new ValueTask(completion);
  }

  private async Task<LatestOperationResult<T>> RunAsync<T>(
    ActiveOperation current,
    Task predecessorCompletion,
    Func<CancellationToken, Task<T>> operation)
  {
    try
    {
      await predecessorCompletion.ConfigureAwait(true);
      current.CancellationSource.Token.ThrowIfCancellationRequested();
      T value = await operation(current.CancellationSource.Token).ConfigureAwait(true);
      return LatestOperationResult<T>.Succeeded(value);
    }
    catch (OperationCanceledException) when (current.CancellationSource.IsCancellationRequested)
    {
      return LatestOperationResult<T>.Canceled();
    }
    finally
    {
      Complete(current);
    }
  }

  private void Complete(ActiveOperation operation)
  {
    lock (sync)
    {
      if (ReferenceEquals(activeOperation, operation))
      {
        activeOperation = null;
      }

      operation.CancellationSource.Dispose();
      operation.Completion.TrySetResult();
    }
  }

  private static bool TryCancel(CancellationTokenSource cancellationSource)
  {
    try
    {
      if (cancellationSource.IsCancellationRequested)
      {
        return false;
      }

      cancellationSource.Cancel();
      return true;
    }
    catch (ObjectDisposedException)
    {
      return false;
    }
  }

  private sealed record ActiveOperation(
    CancellationTokenSource CancellationSource,
    TaskCompletionSource Completion);
}
