using System.Diagnostics.CodeAnalysis;

namespace DictateAnywhere.Audio.WASAPI;

/// <summary>Owns native capture calls through their acknowledgment and disposal.</summary>
internal sealed class NativeCaptureLifetime(Func<Task> requestStop, Func<Task> disposeCapture)
{
  private readonly object sync = new();
  private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private TaskCompletionSource? stop;
  private TaskCompletionSource? disposal;
  private Task? stopDriver;
  private Task? disposalDriver;

  internal void RecordingStopped() => stopped.TrySetResult();

  internal Task StopAsync()
  {
    TaskCompletionSource source;
    lock (sync)
    {
      if (stop is not null) return stop.Task;
      source = stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    stopDriver = StopCoreAsync(source);
    return source.Task;
  }

  internal Task DisposeAsync()
  {
    TaskCompletionSource source;
    lock (sync)
    {
      if (disposal is not null) return disposal.Task;
      source = disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    disposalDriver = DisposeCoreAsync(source);
    return source.Task;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "The retained native call reports failure through its shared completion.")]
  private async Task StopCoreAsync(TaskCompletionSource source)
  {
    try
    {
      await requestStop().ConfigureAwait(false);
      // NAudio's StopRecording only requests stop. All capture callbacks have
      // finished once the RecordingStopped event acknowledges it.
      await stopped.Task.ConfigureAwait(false);
      source.TrySetResult();
    }
    catch (Exception exception) { source.TrySetException(exception); _ = source.Task.Exception; }
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Disposal waits for the actual stop call, attempts independent cleanup after faults, and reports failures after native calls finish.")]
  private async Task DisposeCoreAsync(TaskCompletionSource source)
  {
    List<Exception> failures = [];
    try { await StopAsync().ConfigureAwait(false); }
    catch (Exception exception) { failures.Add(exception); }
    try { await disposeCapture().ConfigureAwait(false); }
    catch (Exception exception) { failures.Add(exception); }
    if (failures.Count == 0) source.TrySetResult();
    else { source.TrySetException(new AggregateException(failures)); _ = source.Task.Exception; }
  }
}
