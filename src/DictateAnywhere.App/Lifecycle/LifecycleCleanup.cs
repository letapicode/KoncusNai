using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Lifecycle;

internal sealed record CleanupStep(string Name, Func<Task> Run);

/// <summary>Continues independent cleanup after faults without abandoning in-flight dependencies.</summary>
internal static class LifecycleCleanup
{
  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Cleanup must finish independent stages before returning their aggregate failure.")]
  internal static async Task RunAsync(params CleanupStep[] steps)
  {
    List<Exception> failures = [];
    foreach (CleanupStep step in steps)
    {
      try { await step.Run().ConfigureAwait(true); }
      catch (Exception exception) { failures.Add(new InvalidOperationException(step.Name, exception)); }
    }
    if (failures.Count > 0) throw new AggregateException("Cleanup was incomplete.", failures);
  }

  internal static CleanupStep Sync(string name, Action action) => new(name, () =>
  {
    action();
    return Task.CompletedTask;
  });

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "The final lifecycle reporting boundary cannot throw, including through a trace listener.")]
  internal static void Report(Action<string, Exception> report, string name, Exception exception)
  {
    try { report(name, exception); }
    catch
    {
      try { Trace.TraceError("Lifecycle cleanup failed: {0}; {1}", name, exception.GetType().Name); }
      catch { /* A failing trace listener must not prevent cleanup. */ }
    }
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Synchronous window events cannot await; the lifetime owner separately retains this task.")]
  internal static async Task ObserveAsync(Task task, Action<string, Exception> report, string name)
  {
    try { await task.ConfigureAwait(true); }
    catch (Exception exception) { Report(report, name, exception); }
  }
}
