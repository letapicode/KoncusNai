using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Settings;

internal enum SettingsAutoSaveState
{
  Saving,
  Saved,
  Failed,
}

internal sealed record SettingsAutoSaveStatus(SettingsAutoSaveState State, string? ErrorMessage = null);

/// <summary>
/// Collapses rapid UI changes into one serialized settings write. A newer
/// snapshot always supersedes an older pending snapshot.
/// </summary>
internal sealed class SettingsAutoSaveCoordinator : IAsyncDisposable
{
  private readonly Func<AppSettings, CancellationToken, Task> saveAsync;
  private readonly TimeSpan delay;
  private readonly SemaphoreSlim saveGate = new(1, 1);
  private readonly object syncRoot = new();

  private CancellationTokenSource? pendingSaveSource;
  private Task pendingSaveTask = Task.CompletedTask;
  private Task? disposalTask;
  private long revision;
  private bool disposed;

  public SettingsAutoSaveCoordinator(
    Func<AppSettings, CancellationToken, Task> saveAsync,
    TimeSpan? delay = null)
  {
    this.saveAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));
    this.delay = delay ?? TimeSpan.FromMilliseconds(500);
    if (this.delay < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(delay), "Autosave delay cannot be negative.");
    }
  }

  public event EventHandler<SettingsAutoSaveStatus>? StatusChanged;

  public void Schedule(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    CancellationTokenSource source;
    CancellationTokenSource? previousSource;
    Task previousTask;
    long scheduledRevision;
    lock (syncRoot)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      previousSource = pendingSaveSource;
      previousTask = pendingSaveTask;
      source = new CancellationTokenSource();
      pendingSaveSource = source;
      scheduledRevision = ++revision;
      pendingSaveTask = SaveAfterDelayAsync(settings, scheduledRevision, source, previousTask);
    }

    TryCancel(previousSource);
  }

  public async Task<bool> FlushAsync(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    CancellationTokenSource? pendingSource;
    Task pendingTask;
    long flushRevision;
    lock (syncRoot)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      pendingSource = pendingSaveSource;
      pendingTask = pendingSaveTask;
      pendingSaveSource = null;
      pendingSaveTask = Task.CompletedTask;
      flushRevision = ++revision;
    }

    TryCancel(pendingSource);
    await pendingTask.ConfigureAwait(false);
    return await SaveSnapshotAsync(settings, flushRevision, CancellationToken.None).ConfigureAwait(false);
  }

  public void CancelPending()
  {
    lock (syncRoot)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      CancelPendingCore();
      revision++;
    }
  }

  public ValueTask DisposeAsync()
  {
    CancellationTokenSource? pendingSource;
    Task pendingTask;
    lock (syncRoot)
    {
      if (disposalTask is not null)
      {
        return new ValueTask(disposalTask);
      }

      disposed = true;
      revision++;
      pendingSource = pendingSaveSource;
      pendingTask = pendingSaveTask;
      pendingSaveSource = null;
      pendingSaveTask = Task.CompletedTask;
      disposalTask = CompleteDisposalAsync(pendingSource, pendingTask);
    }

    TryCancel(pendingSource);
    return new ValueTask(disposalTask);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Scheduled autosave is a background boundary; every failure is converted into observable status and the owned task must settle.")]
  private async Task SaveAfterDelayAsync(
    AppSettings settings,
    long scheduledRevision,
    CancellationTokenSource source,
    Task previousTask)
  {
    try
    {
      await Task.Yield();
      await previousTask.ConfigureAwait(false);
      await Task.Delay(delay, source.Token).ConfigureAwait(false);
      _ = await SaveSnapshotAsync(settings, scheduledRevision, source.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (source.IsCancellationRequested)
    {
      // A newer snapshot or an explicit flush superseded this pending write.
    }
    catch (Exception ex)
    {
      PublishIfCurrent(scheduledRevision, new SettingsAutoSaveStatus(SettingsAutoSaveState.Failed, ex.Message));
    }
    finally
    {
      lock (syncRoot)
      {
        if (ReferenceEquals(pendingSaveSource, source))
        {
          pendingSaveSource = null;
          pendingSaveTask = Task.CompletedTask;
        }
      }

      source.Dispose();
    }
  }

  private async Task<bool> SaveSnapshotAsync(
    AppSettings settings,
    long scheduledRevision,
    CancellationToken cancellationToken)
  {
    bool gateEntered = false;
    try
    {
      await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
      gateEntered = true;
      PublishIfCurrent(scheduledRevision, new SettingsAutoSaveStatus(SettingsAutoSaveState.Saving));
      await saveAsync(settings, cancellationToken).ConfigureAwait(false);
      PublishIfCurrent(scheduledRevision, new SettingsAutoSaveStatus(SettingsAutoSaveState.Saved));
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return false;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      PublishIfCurrent(scheduledRevision, new SettingsAutoSaveStatus(SettingsAutoSaveState.Failed, ex.Message));
      return false;
    }
    finally
    {
      if (gateEntered)
      {
        saveGate.Release();
      }
    }
  }

  private void PublishIfCurrent(long scheduledRevision, SettingsAutoSaveStatus status)
  {
    lock (syncRoot)
    {
      if (scheduledRevision != revision)
      {
        return;
      }
    }

    StatusChanged?.Invoke(this, status);
  }

  private void CancelPendingCore()
  {
    CancellationTokenSource? source = pendingSaveSource;
    pendingSaveSource = null;
    if (source is null)
    {
      return;
    }

    TryCancel(source);
  }

  private async Task CompleteDisposalAsync(CancellationTokenSource? source, Task pendingTask)
  {
    TryCancel(source);
    await pendingTask.ConfigureAwait(false);
    saveGate.Dispose();
  }

  private static void TryCancel(CancellationTokenSource? source)
  {
    if (source is null)
    {
      return;
    }

    try
    {
      source.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // The scheduled save completed while cancellation was being requested.
    }
  }
}
