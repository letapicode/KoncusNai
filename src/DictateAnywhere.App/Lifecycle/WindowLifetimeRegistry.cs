using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Lifecycle;

/// <summary>Retains open and already-closing windows until their asynchronous cleanup settles.</summary>
internal sealed class WindowLifetimeRegistry
{
  private readonly Dictionary<object, Entry> entries = new();
  private bool stopping;
  private Task? disposal;

  internal void ThrowIfStopping() => ObjectDisposedException.ThrowIf(stopping, this);

  internal void Register(object window, Action close, Func<Task> cleanup)
  {
    ThrowIfStopping();
    entries.Add(window, new Entry(close, cleanup));
  }

  internal Task CloseAsync(object window, bool alreadyClosed = false)
  {
    if (!entries.TryGetValue(window, out Entry? entry)) return Task.CompletedTask;
    if (entry.Completion is not null) return entry.Completion;
    TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
    entry.Completion = source.Task;
    _ = CloseCoreAsync(window, entry, source, alreadyClosed);
    return source.Task;
  }

  internal Task DisposeAsync()
  {
    if (disposal is not null) return disposal;
    stopping = true;
    TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
    disposal = source.Task;
    _ = DisposeCoreAsync(source);
    return disposal;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "The shared task retains all closing owners and their aggregate failure.")]
  private async Task DisposeCoreAsync(TaskCompletionSource source)
  {
    try
    {
      await Task.WhenAll(entries.Keys.ToArray().Select(window => CloseAsync(window))).ConfigureAwait(true);
      source.TrySetResult();
    }
    catch (Exception exception) { source.TrySetException(exception); }
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "The retained task carries close/cleanup faults to the application shutdown owner.")]
  private async Task CloseCoreAsync(object window, Entry entry, TaskCompletionSource source, bool alreadyClosed)
  {
    try
    {
      await LifecycleCleanup.RunAsync(
        LifecycleCleanup.Sync("Close window", () => { if (!alreadyClosed) entry.Close(); }),
        new CleanupStep("Dispose window", entry.Cleanup)).ConfigureAwait(true);
      source.TrySetResult();
    }
    catch (Exception exception) { source.TrySetException(exception); }
    finally { entries.Remove(window); }
  }

  private sealed class Entry(Action close, Func<Task> cleanup)
  {
    internal Action Close { get; } = close;
    internal Func<Task> Cleanup { get; } = cleanup;
    internal Task? Completion { get; set; }
  }
}
