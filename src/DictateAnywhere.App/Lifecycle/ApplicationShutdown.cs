using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Lifecycle;

/// <summary>One cooperative quit operation, completed before stopping the dispatcher.</summary>
internal sealed class ApplicationShutdown
{
  private readonly TimeSpan stepTimeout;
  private readonly Action<string, Exception> report;
  private readonly Func<TimeSpan, Task> delay;
  private Task<bool>? completion;

  internal ApplicationShutdown(TimeSpan stepTimeout, Action<string, Exception> report,
    Func<TimeSpan, Task>? delay = null)
  {
    this.stepTimeout = stepTimeout;
    this.report = report;
    this.delay = delay ?? Task.Delay;
  }

  // Called on the owning dispatcher. Publish before invoking any reentrant stage.
  internal Task<bool> RunAsync(params CleanupStep[] steps)
  {
    if (completion is not null) return completion;
    TaskCompletionSource<bool> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
    completion = source.Task;
    _ = RunCoreAsync(source, steps);
    return completion;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Quit reports every cleanup fault and timeout, then completes its shared result.")]
  private async Task RunCoreAsync(TaskCompletionSource<bool> source, CleanupStep[] steps)
  {
    bool complete = true;
    foreach (CleanupStep step in steps)
    {
      try
      {
        Task work = step.Run();
        if (!work.IsCompleted && await Task.WhenAny(work, delay(stepTimeout)).ConfigureAwait(true) != work)
        {
          complete = false;
          LifecycleCleanup.Report(report, step.Name, new TimeoutException("Cleanup deadline elapsed; owned work is still running."));
          _ = LifecycleCleanup.ObserveAsync(work, report, step.Name);
          continue;
        }
        await work.ConfigureAwait(true);
      }
      catch (Exception exception)
      {
        complete = false;
        LifecycleCleanup.Report(report, step.Name, exception);
      }
    }
    source.TrySetResult(complete);
  }
}
