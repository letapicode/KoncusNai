using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Settings;

internal enum SettingsAutoSaveState { Saving, Saved, Failed }
internal sealed record SettingsAutoSaveStatus(SettingsAutoSaveState State, string? ErrorMessage = null,
  AppSettings? Snapshot = null, AppSettings? Submitted = null, long Revision = 0, long OwnerRevision = 0, bool Replacement = false);

/// <summary>Owns a serial chain of every accepted save, including flushes and their callbacks.</summary>
[SuppressMessage("Design", "CA1031:Do not catch general exception types",
  Justification = "Background writes settle into explicit results; callback faults cannot change a completed commit or abandon ownership.")]
internal sealed class SettingsAutoSaveCoordinator : IAsyncDisposable
{
  private static readonly AsyncLocal<SaveOperation?> Executing = new();
  private readonly Func<AppSettings?, AppSettings, CancellationToken, Task<AppSettings>> saveAsync;
  private readonly TimeSpan delay;
  private readonly object sync = new();
  private readonly List<Exception> cancellationFailures = new();
  private SaveOperation? latest;
  private Task tail = Task.CompletedTask;
  private Task? disposalTask;
  private Task disposalExecution = Task.CompletedTask;
  private long revision;
  private long lastOwnerRevision;
  private long rejectedOwnerRevision;
  private bool closing;

  public SettingsAutoSaveCoordinator(Func<AppSettings, CancellationToken, Task> saveAsync, TimeSpan? delay = null)
    : this(Adapt(saveAsync), delay) { }

  public SettingsAutoSaveCoordinator(
    Func<AppSettings?, AppSettings, CancellationToken, Task<AppSettings>> saveAsync, TimeSpan? delay = null)
  {
    this.saveAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));
    this.delay = delay ?? TimeSpan.FromMilliseconds(500);
    if (this.delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
  }

  public event EventHandler<SettingsAutoSaveStatus>? StatusChanged;
  internal Task CleanupExecution => disposalExecution;
  internal bool IsExecuting => Executing.Value is SaveOperation operation
    && operation.Owner == this && !operation.Completion.Task.IsCompleted;

  public void Schedule(AppSettings settings, AppSettings? baseline = null, long ownerRevision = 0) =>
    _ = Accept(settings, baseline, immediate: false, ownerRevision);

  public Task<bool> FlushAsync(AppSettings settings, AppSettings? baseline = null,
    CancellationToken cancellationToken = default, long ownerRevision = 0)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (IsExecuting)
      throw new InvalidOperationException("A settings save callback cannot await a flush of its own save chain.");
    SaveOperation operation = Accept(settings, baseline, immediate: true, ownerRevision);
    // Cancellation bounds this caller's wait, not the lifetime of the owned write.
    return operation.Completion.Task.WaitAsync(cancellationToken);
  }

  private SaveOperation Accept(AppSettings settings, AppSettings? baseline, bool immediate, long ownerRevision)
  {
    settings = SettingsSnapshot.Capture(settings);
    baseline = baseline is null ? null : SettingsSnapshot.Capture(baseline);
    SaveOperation operation;
    SaveOperation? previous;
    Task predecessor;
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(closing, this);
      if (ownerRevision > 0 && (ownerRevision < lastOwnerRevision || ownerRevision <= rejectedOwnerRevision))
      {
        SaveOperation superseded = new(this, settings, baseline, revision, ownerRevision);
        superseded.Source.Dispose();
        superseded.Completion.SetResult(false);
        return superseded;
      }
      lastOwnerRevision = Math.Max(lastOwnerRevision, ownerRevision);
      previous = latest;
      predecessor = tail;
      operation = new SaveOperation(this, settings, baseline, ++revision, ownerRevision);
      latest = operation;
      tail = operation.Completion.Task;
    }
    Cancel(previous);
    if (immediate) operation.Ready.TrySetResult();
    operation.Execution = ExecuteAsync(operation, predecessor);
    return operation;
  }

  public void CancelPending(long rejectedRevision = 0)
  {
    SaveOperation? operation;
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(closing, this);
      rejectedOwnerRevision = Math.Max(rejectedOwnerRevision, rejectedRevision);
      operation = latest;
      latest = null;
      revision++;
    }
    Cancel(operation);
  }

  public ValueTask DisposeAsync()
  {
    Task task = RequestShutdown();
    return IsExecuting
      ? ValueTask.FromException(new InvalidOperationException("Request settings shutdown without awaiting it inside a save callback."))
      : new ValueTask(task);
  }

  // A retained supervisor may drain this task even when its initiating callback's
  // execution context flowed into it. The callback itself must never self-await.
  internal Task RequestShutdown()
  {
    Task task;
    SaveOperation? operation;
    Task pending;
    lock (sync)
    {
      if (disposalTask is not null) task = disposalTask;
      else
      {
        closing = true;
        operation = latest;
        pending = tail;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        disposalTask = task = completion.Task;
        // No await, cancellation, sink or subscriber executes under sync.
        operation?.Ready.TrySetResult();
        disposalExecution = FinishDisposalAsync(pending, operation, completion);
      }
    }
    // Teardown is still retained; an initiating callback must request it without
    // awaiting itself. Fail a mistaken awaited call promptly rather than deadlock.
    return task;
  }

  private async Task FinishDisposalAsync(Task pending, SaveOperation? operation, TaskCompletionSource completion)
  {
    await Task.Yield();
    try
    {
      await pending.ConfigureAwait(false);
      if (operation is not null) await operation.Execution.ConfigureAwait(false);
      List<Exception> failures;
      lock (sync) failures = new(cancellationFailures);
      if (operation?.Failure is Exception error) failures.Add(error);
      if (failures.Count == 1) throw failures[0];
      if (failures.Count > 1) throw new AggregateException("Settings cleanup failed.", failures);
      completion.TrySetResult();
    }
    catch (Exception error) { completion.TrySetException(error); }
  }

  private async Task ExecuteAsync(SaveOperation operation, Task predecessor)
  {
    await Task.Yield();
    bool saved = false;
    try
    {
      await predecessor.ConfigureAwait(false);
      using CancellationTokenSource delaySource = CancellationTokenSource.CreateLinkedTokenSource(operation.Source.Token);
      Task delayed = Task.Delay(delay, delaySource.Token);
      await Task.WhenAny(delayed, operation.Ready.Task).ConfigureAwait(false);
      delaySource.Cancel();
      try { await delayed.ConfigureAwait(false); }
      catch (OperationCanceledException) when (delaySource.IsCancellationRequested) { }
      // Observe a canceled delay even if Ready won; no gate or resource is disposed
      // while its accepted operation is still executing.
      operation.Source.Token.ThrowIfCancellationRequested();
      lock (sync) { if (operation.Revision != revision) return; }
      SaveOperation? prior = Executing.Value;
      Executing.Value = operation;
      try
      {
        Publish(operation, new(SettingsAutoSaveState.Saving, Revision: operation.Revision));
        operation.Source.Token.ThrowIfCancellationRequested();
        lock (sync) { if (operation.Revision != revision) return; }
        AppSettings committed = SettingsSnapshot.Capture(await saveAsync(operation.Baseline, operation.Settings, operation.Source.Token).ConfigureAwait(false));
        // An uncooperative writer may have committed despite cancellation. Report
        // that exact commit, never a newer requested snapshot or fabricated undo.
        saved = true;
        Publish(operation, new(SettingsAutoSaveState.Saved, Snapshot: committed,
          Submitted: operation.Settings, Revision: operation.Revision, OwnerRevision: operation.OwnerRevision, Replacement: operation.Baseline is null));
      }
      finally { Executing.Value = prior; }
    }
    catch (OperationCanceledException) when (operation.Source.IsCancellationRequested) { }
    catch (Exception error)
    {
      operation.Failure = error;
      string message;
      try { message = error.Message; }
      catch (Exception) { DiagnosticBoundary.RecordFailure(); message = "Settings persistence failed."; }
      Publish(operation, new(SettingsAutoSaveState.Failed, message, Revision: operation.Revision));
    }
    finally
    {
      await operation.FinishCancellationAsync().ConfigureAwait(false);
      operation.Source.Dispose();
      operation.Completion.TrySetResult(saved);
    }
  }

  private void Publish(SaveOperation operation, SettingsAutoSaveStatus status)
  {
    if (status.State != SettingsAutoSaveState.Saved)
      lock (sync) { if (operation.Revision != revision) return; }
    EventHandler<SettingsAutoSaveStatus>? subscribers = StatusChanged;
    if (subscribers is null) return;
    SaveOperation? prior = Executing.Value;
    Executing.Value = operation;
    try
    {
      foreach (EventHandler<SettingsAutoSaveStatus> subscriber in subscribers.GetInvocationList())
      {
        try { subscriber(this, status); }
        catch (Exception) { DiagnosticBoundary.RecordFailure(); }
      }
    }
    finally { Executing.Value = prior; }
  }

  private void Cancel(SaveOperation? operation)
  {
    if (operation is null) return;
    TaskCompletionSource? cancellation = operation.BeginCancellation();
    if (cancellation is null) return;
    SaveOperation? prior = Executing.Value;
    Executing.Value = operation;
    try { operation.Source.Cancel(); }
    catch (ObjectDisposedException) { }
    catch (AggregateException error)
    {
      lock (sync) cancellationFailures.Add(error);
      DiagnosticBoundary.RecordFailure();
    }
    finally { cancellation.TrySetResult(); Executing.Value = prior; }
  }

  private static Func<AppSettings?, AppSettings, CancellationToken, Task<AppSettings>> Adapt(
    Func<AppSettings, CancellationToken, Task> writer)
  {
    ArgumentNullException.ThrowIfNull(writer);
    return async (_, settings, token) => { await writer(settings, token).ConfigureAwait(false); return settings; };
  }

  private sealed class SaveOperation(SettingsAutoSaveCoordinator owner, AppSettings settings, AppSettings? baseline, long revision, long ownerRevision)
  {
    internal readonly SettingsAutoSaveCoordinator Owner = owner;
    internal readonly AppSettings Settings = settings;
    internal readonly AppSettings? Baseline = baseline;
    internal readonly long Revision = revision;
    internal readonly long OwnerRevision = ownerRevision;
    internal readonly CancellationTokenSource Source = new();
    internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Execution = Task.CompletedTask;
    internal Exception? Failure;
    private readonly object cancellationSync = new();
    private readonly List<Task> cancellations = new();
    private bool finishing;
    internal TaskCompletionSource? BeginCancellation()
    {
      lock (cancellationSync)
      {
        if (finishing) return null;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellations.Add(completion.Task);
        return completion;
      }
    }
    internal Task FinishCancellationAsync()
    {
      lock (cancellationSync)
      {
        finishing = true;
        return Task.WhenAll(cancellations);
      }
    }
  }
}
