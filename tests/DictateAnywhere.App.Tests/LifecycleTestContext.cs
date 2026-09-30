using System.Diagnostics;

namespace DictateAnywhere.App.Tests;

/// <summary>Serial task pump for dispatcher-owned logic, without WPF or the runner's shared queue.</summary>
internal sealed class LifecycleTestContext : SynchronizationContext
{
  private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = new();
  private readonly List<Action> releases = [];

  internal void ReleaseOnTimeout(Action release) => releases.Add(release);

  public override SynchronizationContext CreateCopy() => this;

  public override void Post(SendOrPostCallback d, object? state)
  {
    lock (callbacks)
    {
      callbacks.Enqueue((d, state));
      Monitor.PulseAll(callbacks);
    }
  }

  internal static void Run(Func<LifecycleTestContext, Task> test, TimeSpan? budget = null)
  {
    SynchronizationContext? previous = Current;
    LifecycleTestContext context = new();
    SetSynchronizationContext(context);
    try
    {
      Task operation = test(context);
      try { context.Drain(operation, budget); }
      catch (TimeoutException timeout) when (!operation.IsCompleted)
      {
        foreach (Action release in context.releases) release();
        try { context.Drain(operation); }
        catch (Exception cleanup) { throw new AggregateException(timeout, cleanup); }
        throw;
      }
    }
    finally { SetSynchronizationContext(previous); }
  }

  internal void Drain(Task task, TimeSpan? budget = null)
  {
    TimeSpan limit = budget ?? TimeSpan.FromSeconds(15);
    Stopwatch elapsed = Stopwatch.StartNew();
    // Wake a pump even when the task completes without posting a continuation.
    _ = task.ContinueWith(_ =>
    {
      lock (callbacks) Monitor.PulseAll(callbacks);
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    while (!task.IsCompleted)
    {
      (SendOrPostCallback Callback, object? State) callback;
      lock (callbacks)
      {
        if (task.IsCompleted) break;
        if (elapsed.Elapsed >= limit) throw new TimeoutException("Lifecycle test pump exhausted its harness budget.");
        if (!callbacks.TryDequeue(out callback))
        {
          TimeSpan remaining = limit - elapsed.Elapsed;
          if (!task.IsCompleted && (remaining <= TimeSpan.Zero || !Monitor.Wait(callbacks, remaining)))
            throw new TimeoutException("Lifecycle test pump did not complete within its harness budget.");
          continue;
        }
      }
      callback.Callback(callback.State);
    }
    task.GetAwaiter().GetResult();
  }
}
