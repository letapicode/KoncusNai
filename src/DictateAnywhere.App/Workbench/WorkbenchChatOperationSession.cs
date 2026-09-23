using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchChatOperationKind
{
  ModelSetup,
  Completion,
  LocalReply,
}

/// <summary>Owns exclusive Workbench chat-operation state, cancellation, and ordered shutdown.</summary>
internal sealed class WorkbenchChatOperationSession : IAsyncDisposable
{
  private readonly object sync = new();
  private ActiveOperation? activeOperation;
  private Task? disposalTask;
  private long nextOperationId;
  private bool disposed;

  public bool IsBusy
  {
    get
    {
      lock (sync)
      {
        return activeOperation is not null;
      }
    }
  }

  public bool CanCancel
  {
    get
    {
      lock (sync)
      {
        return activeOperation?.CancellationSource is { IsCancellationRequested: false };
      }
    }
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The active operation owns the cancellation source and disposes it when its lease completes.")]
  public WorkbenchChatOperation? TryBegin(WorkbenchChatOperationKind kind)
  {
    if (!Enum.IsDefined(kind))
    {
      throw new ArgumentOutOfRangeException(nameof(kind));
    }

    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (activeOperation is not null)
      {
        return null;
      }

      CancellationTokenSource? cancellationSource = kind is WorkbenchChatOperationKind.ModelSetup or WorkbenchChatOperationKind.Completion
        ? new CancellationTokenSource()
        : null;
      long operationId = ++nextOperationId;
      activeOperation = new ActiveOperation(
        operationId,
        kind,
        cancellationSource,
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

      return new WorkbenchChatOperation(
        this,
        operationId,
        kind,
        cancellationSource?.Token ?? CancellationToken.None);
    }
  }

  public WorkbenchChatOperationKind? CancelActive()
  {
    ActiveOperation? operation;
    lock (sync)
    {
      operation = activeOperation;
      if (operation?.CancellationSource is not { IsCancellationRequested: false })
      {
        return null;
      }
    }

    return TryCancel(operation.CancellationSource)
      ? operation.Kind
      : null;
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

    _ = cancellationSource is not null && TryCancel(cancellationSource);
    return new ValueTask(completion);
  }

  internal void Complete(long operationId)
  {
    CancellationTokenSource? cancellationSource;
    TaskCompletionSource completion;
    lock (sync)
    {
      if (activeOperation is null || activeOperation.Id != operationId)
      {
        return;
      }

      cancellationSource = activeOperation.CancellationSource;
      completion = activeOperation.Completion;
      activeOperation = null;
    }

    cancellationSource?.Dispose();
    completion.TrySetResult();
  }

  private static bool TryCancel(CancellationTokenSource cancellationSource)
  {
    try
    {
      cancellationSource.Cancel();
      return true;
    }
    catch (ObjectDisposedException)
    {
      return false;
    }
  }

  private sealed record ActiveOperation(
    long Id,
    WorkbenchChatOperationKind Kind,
    CancellationTokenSource? CancellationSource,
    TaskCompletionSource Completion);
}

internal sealed class WorkbenchChatOperation : IDisposable
{
  private WorkbenchChatOperationSession? owner;
  private readonly long operationId;

  internal WorkbenchChatOperation(
    WorkbenchChatOperationSession owner,
    long operationId,
    WorkbenchChatOperationKind kind,
    CancellationToken cancellationToken)
  {
    this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    this.operationId = operationId;
    Kind = kind;
    CancellationToken = cancellationToken;
  }

  public WorkbenchChatOperationKind Kind { get; }

  public CancellationToken CancellationToken { get; }

  public void Dispose()
  {
    Interlocked.Exchange(ref owner, null)?.Complete(operationId);
  }
}
