using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Runtime;

/// <summary>Retains direct launch handles and accepted users. Persisted PIDs never confer ownership.</summary>
[SuppressMessage("Design", "CA1031", Justification = "Independent cleanup continues after faults; accepted work retains its original failure.")]
internal sealed class OllamaProcessOwnership : IAsyncDisposable
{
  internal static OllamaProcessOwnership Default { get; } = new();
  private static readonly AsyncLocal<Operation?> Executing = new();
  private readonly object sync = new();
  private readonly List<OwnedProcess> processes = [];
  private readonly List<Operation> operations = [];
  private readonly List<Exception> cancellationFailures = [];
  private readonly List<Exception> completedProcessFailures = [];
  private Task? stopTask;
  private Task stopExecution = Task.CompletedTask;
  private bool closing;
  internal SemaphoreSlim SetupGate { get; } = new(1, 1);

  internal Task CleanupExecution => stopExecution;
  internal bool IsStopping { get { lock (sync) return closing; } }
  internal void ThrowIfStopping(CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    if (IsStopping) throw new OperationCanceledException(token);
  }

  internal Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> run, CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(run);
    token.ThrowIfCancellationRequested();
    Operation operation;
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(closing, this);
      operation = new(this);
      operations.Add(operation);
    }
    operation.Execution = ExecuteAsync(operation, run, token);
    return ResultAsync<T>(operation.Completion.Task);
  }

  private static async Task<T> ResultAsync<T>(Task<object?> task) => (T)(await task.ConfigureAwait(false))!;

  private async Task ExecuteAsync<T>(Operation operation, Func<CancellationToken, Task<T>> run, CancellationToken caller)
  {
    await Task.Yield();
    Operation? previous = Executing.Value;
    Executing.Value = operation;
    object? result = null;
    Exception? failure = null;
    using CancellationTokenRegistration registration = caller.UnsafeRegister(static state => ((Operation)state!).Cancel(), operation);
    try
    {
      ThrowIfStopping(operation.Token);
      result = await run(operation.Token).ConfigureAwait(false);
      operation.Token.ThrowIfCancellationRequested();
      if (IsStopping) throw new OperationCanceledException(operation.Token);
    }
    catch (Exception error) { failure = error; }
    finally
    {
      registration.Dispose();
      await operation.FinishCancellationAsync().ConfigureAwait(false);
      operation.Source.Dispose();
      Executing.Value = previous;
      if (failure is OperationCanceledException canceled) operation.Completion.TrySetCanceled(canceled.CancellationToken);
      else if (failure is not null) operation.Completion.TrySetException(failure);
      else operation.Completion.TrySetResult(result);
      // Completed entries can be removed only after their stable completion settles.
      lock (sync) operations.Remove(operation);
    }
  }

  internal OwnedProcess TrackProcess(IProcess process)
  {
    ArgumentNullException.ThrowIfNull(process);
    lock (sync)
    {
      // A launch already admitted by RunAsync can finish after stop closes admission.
      // Stop drains that operation before taking its final process snapshot.
      ObjectDisposedException.ThrowIf(closing && !(Executing.Value is { } operation
        && operation.Owner == this && !operation.Completion.Task.IsCompleted), this);
      OwnedProcess owned = new(this, process);
      processes.Add(owned);
      return owned;
    }
  }

  internal static IProcess Launch(ProcessStartInfo startInfo) => new SystemProcess(
    Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the Ollama local service."));
  internal static Task StopOwnedAsync() => Default.StopAsync();

  internal Task RequestStop()
  {
    lock (sync)
    {
      if (stopTask is not null) return stopTask;
      closing = true;
      TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
      stopTask = completion.Task;
      stopExecution = FinishStopAsync(completion, operations.ToArray());
      return stopTask;
    }
  }

  internal Task StopAsync(CancellationToken caller = default)
  {
    Task completion = RequestStop();
    if (Executing.Value is { } operation && operation.Owner == this && !operation.Completion.Task.IsCompleted)
      return Task.FromException(new InvalidOperationException("Request Ollama stop without awaiting it from an accepted runtime callback."));
    return caller.CanBeCanceled ? completion.WaitAsync(caller) : completion;
  }

  public ValueTask DisposeAsync() => new(StopAsync());

  private async Task FinishStopAsync(TaskCompletionSource completion, Operation[] pending)
  {
    await Task.Yield();
    List<Exception> failures = [];
    foreach (Operation operation in pending) operation.Cancel();
    foreach (Operation operation in pending)
    {
      try { await operation.Completion.Task.ConfigureAwait(false); }
      catch (OperationCanceledException) { }
      catch (Exception) { /* The operation's caller owns its original failure. */ }
      await operation.Execution.ConfigureAwait(false);
    }
    OwnedProcess[] owned;
    lock (sync)
    {
      owned = processes.ToArray();
      failures.AddRange(cancellationFailures);
      failures.AddRange(completedProcessFailures);
    }
    SetupGate.Dispose();
    // Initiate every independent stop before awaiting an uncooperative child.
    Task[] stops = Array.ConvertAll(owned, static process => process.StopAsync());
    for (int index = 0; index < stops.Length; index++)
    {
      try { await stops[index].ConfigureAwait(false); }
      catch (Exception error) { failures.Add(error); }
      await owned[index].CleanupExecution.ConfigureAwait(false);
    }
    if (failures.Count > 0) completion.TrySetException(new AggregateException("Ollama cleanup failed.", failures));
    else completion.TrySetResult();
  }

  internal sealed class OwnedProcess(OllamaProcessOwnership owner, IProcess process)
  {
    private readonly object sync = new();
    private Task? stop;
    private Task? exit;
    private Task execution = Task.CompletedTask;
    internal IProcess Process => process;
    internal Task CleanupExecution => execution;
    internal Task WaitForExitAsync()
    {
      lock (sync)
      {
        if (exit is not null) return exit;
        try { exit = process.WaitForExitAsync(); }
        catch (Exception error) { exit = Task.FromException(error); }
        return exit;
      }
    }
    internal Task StopAsync()
    {
      lock (sync)
      {
        if (stop is not null) return stop;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        stop = completion.Task;
        execution = Task.Run(() => StopCoreAsync(completion));
        return stop;
      }
    }
    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
      await Task.Yield();
      Exception? failure = null;
      try { await process.StopAsync().ConfigureAwait(false); }
      catch (Exception error) { failure = error; }
      try { await WaitForExitAsync().ConfigureAwait(false); }
      catch (Exception error)
      {
        // Unverifiable exit is incomplete cleanup; retain the handle.
        completion.TrySetException(failure is null ? error : new AggregateException(failure, error));
        return;
      }
      bool released = true;
      try { process.Dispose(); }
      catch (Exception error) { released = false; failure = failure is null ? error : new AggregateException(failure, error); }
      if (failure is null) completion.TrySetResult();
      else completion.TrySetException(failure);
      if (released)
      {
        lock (owner.sync)
        {
          if (failure is not null) owner.completedProcessFailures.Add(failure);
          owner.processes.Remove(this);
        }
      }
    }
  }

  internal interface IProcess : IDisposable
  {
    int Id { get; }
    DateTime StartTimeUtc { get; }
    string Name { get; }
    string ExecutablePath { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Task StopAsync();
    Task WaitForExitAsync();
  }

  private sealed class SystemProcess(Process process) : IProcess
  {
    public int Id => process.Id;
    public DateTime StartTimeUtc => process.StartTime.ToUniversalTime();
    public string Name => process.ProcessName;
    public string ExecutablePath => process.MainModule?.FileName ?? throw new InvalidOperationException("Ollama executable identity unavailable.");
    public bool HasExited => process.HasExited;
    public int ExitCode => process.ExitCode;
    public Task StopAsync()
    {
      try { if (!process.HasExited) process.Kill(); }
      catch (Exception error) when (error is Win32Exception or InvalidOperationException)
      {
        DiagnosticBoundary.RecordFailure();
        return Task.FromException(error);
      }
      // The launched Process retains its OS handle; never reopen a recorded PID.
      // No tree termination: installer-created servers/descendants are not proven owned.
      return Task.CompletedTask;
    }
    public Task WaitForExitAsync() => process.WaitForExitAsync();
    public void Dispose() => process.Dispose();
  }

  private sealed class Operation(OllamaProcessOwnership owner)
  {
    internal readonly OllamaProcessOwnership Owner = owner;
    internal readonly CancellationTokenSource Source = new();
    internal readonly TaskCompletionSource<object?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Execution = Task.CompletedTask;
    private readonly object sync = new();
    private Task cancellation = Task.CompletedTask;
    private Task cancellationExecution = Task.CompletedTask;
    private bool finishing;
    private bool cancelRequested;
    internal CancellationToken Token => Source.Token;
    internal void Cancel()
    {
      TaskCompletionSource completion;
      lock (sync)
      {
        if (finishing || cancelRequested) return;
        cancelRequested = true;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellation = completion.Task;
      }
      // CancelAsync publishes token cancellation synchronously, runs callbacks
      // asynchronously, and keeps their faults/completion in the returned task.
      Task callbacks;
      try { callbacks = Source.CancelAsync(); }
      catch (Exception failure) { callbacks = Task.FromException(failure); }
      cancellationExecution = CancelCoreAsync(callbacks, completion);
    }
    private async Task CancelCoreAsync(Task callbacks, TaskCompletionSource completion)
    {
      await Task.Yield();
      try { await callbacks.ConfigureAwait(false); }
      catch (Exception failure)
      {
        lock (Owner.sync) Owner.cancellationFailures.Add(failure);
        DiagnosticBoundary.RecordFailure();
      }
      finally { completion.TrySetResult(); }
    }
    internal async Task FinishCancellationAsync()
    {
      Task pending;
      lock (sync) { finishing = true; pending = cancellation; }
      await pending.ConfigureAwait(false);
      await cancellationExecution.ConfigureAwait(false);
    }
  }
}
