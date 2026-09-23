using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchOperationKind
{
  SettingsApply,
  FileImport,
  RecordingStart,
  Transcription,
}

/// <summary>Owns exclusive non-chat Workbench operation state, cancellation, and ordered shutdown.</summary>
internal sealed class WorkbenchOperationSession : IAsyncDisposable
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

  public bool IsImportingFiles
  {
    get
    {
      lock (sync)
      {
        return activeOperation?.Kind == WorkbenchOperationKind.FileImport;
      }
    }
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The active operation owns the cancellation source and disposes it when its lease completes.")]
  public WorkbenchOperation? TryBegin(
    WorkbenchOperationKind kind,
    CancellationToken cancellationToken = default)
  {
    if (!Enum.IsDefined(kind))
    {
      throw new ArgumentOutOfRangeException(nameof(kind));
    }

    cancellationToken.ThrowIfCancellationRequested();
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (activeOperation is not null)
      {
        return null;
      }

      CancellationTokenSource cancellationSource = cancellationToken.CanBeCanceled
        ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        : new CancellationTokenSource();
      long operationId = ++nextOperationId;
      activeOperation = new ActiveOperation(
        operationId,
        kind,
        cancellationSource,
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

      return new WorkbenchOperation(this, operationId, kind, cancellationSource.Token);
    }
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

  internal void Complete(long operationId)
  {
    CancellationTokenSource cancellationSource;
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

    cancellationSource.Dispose();
    completion.TrySetResult();
  }

  private static void TryCancel(CancellationTokenSource cancellationSource)
  {
    try
    {
      cancellationSource.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // The operation completed between the disposal snapshot and cancellation.
    }
  }

  private sealed record ActiveOperation(
    long Id,
    WorkbenchOperationKind Kind,
    CancellationTokenSource CancellationSource,
    TaskCompletionSource Completion);
}

internal sealed class WorkbenchOperation : IDisposable
{
  private WorkbenchOperationSession? owner;
  private readonly long operationId;

  internal WorkbenchOperation(
    WorkbenchOperationSession owner,
    long operationId,
    WorkbenchOperationKind kind,
    CancellationToken cancellationToken)
  {
    this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    this.operationId = operationId;
    Kind = kind;
    CancellationToken = cancellationToken;
  }

  public WorkbenchOperationKind Kind { get; }

  public CancellationToken CancellationToken { get; }

  public void Dispose()
  {
    Interlocked.Exchange(ref owner, null)?.Complete(operationId);
  }
}
