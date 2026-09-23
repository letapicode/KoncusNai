using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;

namespace DictateAnywhere.Insertion;

/// <summary>One outstanding native clipboard call; timeout never abandons a committed mutation.</summary>
internal sealed class ClipboardStaOperation
{
  private readonly SemaphoreSlim admission = new(1, 1);

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "The native worker boundary transfers every exception to its observed completion task.")]
  public T Run<T>(Func<CommitBoundary, T> callback, TimeSpan timeout)
  {
    if (!admission.Wait(0)) throw new ClipboardOperationException("A previous clipboard operation is still finishing.");
    CommitBoundary boundary = new();
    TaskCompletionSource<(T? Value, Exception? Error)> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Thread worker = new(() =>
    {
      T result;
      try { result = callback(boundary); }
      catch (Exception ex)
      {
        admission.Release();
        completion.TrySetResult((default, ex));
        return;
      }
      admission.Release();
      completion.TrySetResult((result, null));
    }) { IsBackground = true, Name = "Notype.Clipboard" };
    try
    {
      worker.SetApartmentState(ApartmentState.STA);
      worker.Start();
    }
    catch { admission.Release(); throw; }

    if (!Task.WhenAny(completion.Task, Task.Delay(timeout)).GetAwaiter().GetResult().Equals(completion.Task) && boundary.TryAbandon())
    {
      throw new ClipboardOperationException("Clipboard operation timed out before mutation.");
    }
    var outcome = completion.Task.GetAwaiter().GetResult();
    if (outcome.Error is not null) ExceptionDispatchInfo.Capture(outcome.Error).Throw();
    return outcome.Value!;
  }

  internal sealed class CommitBoundary
  {
    private readonly object sync = new();
    private bool abandoned;
    private bool committed;

    public void Enter()
    {
      lock (sync)
      {
        if (abandoned) throw new ClipboardOperationException("Clipboard operation expired before mutation.");
        committed = true;
      }
    }

    public bool TryAbandon()
    {
      lock (sync)
      {
        if (committed) return false;
        abandoned = true;
        return true;
      }
    }
  }
}
