using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Settings;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class SettingsAutosaveOwnershipTests
{
  [Fact]
  public void ShutdownDeadline_ReportsIncompleteSave_AndRetainsItUntilItSettles() => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource deadline = new(TaskCreationOptions.RunContinuationsAsynchronously);
    context.ReleaseOnTimeout(() => { release.TrySetResult(); deadline.TrySetResult(); });
    bool written = false;
    bool independentCleanup = false;
    List<Exception> reports = new();
    await using SettingsAutoSaveCoordinator coordinator = new(async (_, _) =>
    { started.TrySetResult(); await release.Task.ConfigureAwait(false); written = true; }, TimeSpan.Zero);
    Task<bool> flush = coordinator.FlushAsync(AppSettings.Default);
    await started.Task;
    Task disposal = coordinator.DisposeAsync().AsTask();
    ApplicationShutdown shutdown = new(TimeSpan.FromSeconds(5), (_, error) => reports.Add(error), _ => deadline.Task);
    Task<bool> quit = shutdown.RunAsync(new CleanupStep("Settings", () => disposal),
      LifecycleCleanup.Sync("Independent cleanup", () => independentCleanup = true));
    try
    {
      deadline.TrySetResult();
      Assert.False(await quit);
      Assert.IsType<TimeoutException>(Assert.Single(reports));
      Assert.True(independentCleanup);
      Assert.False(written);
      Assert.False(disposal.IsCompleted);
      Assert.False(coordinator.CleanupExecution.IsCompleted);
      Assert.Same(disposal, coordinator.DisposeAsync().AsTask());
    }
    finally
    {
      release.TrySetResult();
      await flush;
      await disposal;
      await quit;
    }
    Assert.True(written);
    await coordinator.CleanupExecution;
  });

  [Fact]
  public async Task ReentrantSaving_SupersedesBeforeWriterAdmission_AndThrowingSubscribersDoNotStopOthers()
  {
    List<int> writes = new();
    List<int> commits = new();
    await using SettingsAutoSaveCoordinator coordinator = new((settings, _) =>
    { writes.Add(settings.ChatOutputFontSize); return Task.CompletedTask; }, TimeSpan.FromMinutes(1));
    EventHandler<SettingsAutoSaveStatus>? oneShot = null;
    oneShot = (_, status) =>
    {
      if (status.State == SettingsAutoSaveState.Saving)
      {
        coordinator.StatusChanged -= oneShot;
        coordinator.Schedule(AppSettings.Default with { ChatOutputFontSize = 22 });
      }
    };
    coordinator.StatusChanged += oneShot;
    coordinator.StatusChanged += (_, _) => throw new InvalidOperationException("subscriber fault");
    coordinator.StatusChanged += (_, status) =>
    { if (status.State == SettingsAutoSaveState.Saved) commits.Add(status.Snapshot!.ChatOutputFontSize); };
    Assert.False(await coordinator.FlushAsync(AppSettings.Default with { ChatOutputFontSize = 18 }).WaitAsync(TimeSpan.FromSeconds(30)));
    await coordinator.DisposeAsync();
    Assert.Equal(new[] { 22 }, writes);
    Assert.Equal(new[] { 22 }, commits);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CallerCancel_DoesNotOrphanFlush_AndDisposeFlushesDebounce(bool inline)
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(inline ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously);
    int writes = 0;
    SettingsAutoSaveCoordinator coordinator = new(async (_, _) =>
    { started.TrySetResult(); await release.Task.ConfigureAwait(false); writes++; }, TimeSpan.FromMinutes(1));
    using CancellationTokenSource caller = new();
    Task<bool> wait = coordinator.FlushAsync(AppSettings.Default, cancellationToken: caller.Token);
    Task? disposal = null;
    try
    {
      await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      caller.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
      disposal = coordinator.DisposeAsync().AsTask();
      Assert.Same(disposal, coordinator.DisposeAsync().AsTask());
      Assert.False(disposal.IsCompleted);
      Assert.Throws<ObjectDisposedException>(() => coordinator.Schedule(AppSettings.Default));
    }
    finally
    {
      release.TrySetResult();
      await (disposal ?? coordinator.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(30));
    }
    Assert.Equal(1, writes);
    await using SettingsAutoSaveCoordinator delayed = new((_, _) => { writes++; return Task.CompletedTask; }, TimeSpan.FromDays(1));
    delayed.Schedule(AppSettings.Default);
    await delayed.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
    Assert.Equal(2, writes);
  }

  [Fact]
  public async Task ReentrantShutdownRequest_IsOwned_AndMistakenSelfAwaitFailsPromptly()
  {
    await using SettingsAutoSaveCoordinator coordinator = new((_, _) => Task.CompletedTask, TimeSpan.Zero);
    Task? initiatingWait = null;
    coordinator.StatusChanged += (_, status) =>
    { if (status.State == SettingsAutoSaveState.Saving) initiatingWait = coordinator.DisposeAsync().AsTask(); };
    Task<bool> flush = coordinator.FlushAsync(AppSettings.Default);
    await flush.WaitAsync(TimeSpan.FromSeconds(30));
    Assert.NotNull(initiatingWait);
    await Assert.ThrowsAsync<InvalidOperationException>(() => initiatingWait);
    await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
  }

  [Fact]
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Finally drains the writer and explicitly observes the expected faulting disposal; a using statement would rethrow the deliberate failure.")]
  public async Task ThrowingCancellationCallback_DoesNotLoseNextSave_OrDisposeItsTokenEarly()
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    List<int> writes = new();
    bool retainedCancellation = false;
    SettingsAutoSaveCoordinator coordinator = new(async (settings, token) =>
    {
      if (settings.ChatOutputFontSize == 18)
      {
        using CancellationTokenRegistration throwing = token.Register(() => throw new InvalidOperationException("cancel callback"));
        started.TrySetResult();
        await release.Task.ConfigureAwait(false);
        using CancellationTokenRegistration late = token.Register(() => { });
        retainedCancellation = token.IsCancellationRequested;
      }
      writes.Add(settings.ChatOutputFontSize);
    }, TimeSpan.Zero);
    Task<bool> first = coordinator.FlushAsync(AppSettings.Default with { ChatOutputFontSize = 18 });
    Task<bool>? next = null;
    try
    {
      await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      next = coordinator.FlushAsync(AppSettings.Default with { ChatOutputFontSize = 22 });
      Assert.False(next.IsCompleted);
      release.TrySetResult();
      await Task.WhenAll(first, next).WaitAsync(TimeSpan.FromSeconds(30));
      Assert.Equal(new[] { 18, 22 }, writes);
      Assert.True(retainedCancellation);
    }
    finally
    {
      release.TrySetResult();
      await first.WaitAsync(TimeSpan.FromSeconds(30));
      if (next is not null) await next.WaitAsync(TimeSpan.FromSeconds(30));
      try { await coordinator.DisposeAsync(); Assert.Fail("Cancellation failure must remain observable during teardown."); }
      catch (AggregateException error) { Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message == "cancel callback"); }
    }
  }

  [Fact]
  public async Task CancellationCallbackStillRunning_IsDrainedBeforeTokenDisposal()
  {
    using ManualResetEventSlim callbackRelease = new(false);
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource callbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource writerRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource writerFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await using SettingsAutoSaveCoordinator coordinator = new(async (_, token) =>
    {
      using CancellationTokenRegistration registration = token.Register(() =>
      {
        callbackStarted.TrySetResult();
        if (!callbackRelease.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Owned callback barrier not released.");
      });
      started.TrySetResult();
      await writerRelease.Task.ConfigureAwait(false);
      writerFinished.TrySetResult();
    }, TimeSpan.Zero);
    Task<bool> flush = coordinator.FlushAsync(AppSettings.Default);
    Task? cancel = null;
    Task? disposal = null;
    try
    {
      await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      cancel = Task.Run(() => coordinator.CancelPending());
      await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
      writerRelease.TrySetResult();
      await writerFinished.Task.WaitAsync(TimeSpan.FromSeconds(30));
      disposal = coordinator.DisposeAsync().AsTask();
      Assert.False(disposal.IsCompleted);
      Assert.False(flush.IsCompleted);
    }
    finally
    {
      callbackRelease.Set();
      writerRelease.TrySetResult();
      if (cancel is not null) await cancel.WaitAsync(TimeSpan.FromSeconds(30));
      await flush.WaitAsync(TimeSpan.FromSeconds(30));
      await (disposal ?? coordinator.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(30));
    }
  }

  [Fact]
  public async Task AlreadyCanceledFlush_IsNotAccepted_AndSnapshotCopiesMutableInput()
  {
    using CancellationTokenSource canceled = new();
    canceled.Cancel();
    List<string> names = new() { "original" };
    string? observed = null;
    await using SettingsAutoSaveCoordinator coordinator = new((settings, _) =>
    { observed = settings.InsertionBlockedProcessNames[0]; return Task.CompletedTask; }, TimeSpan.FromDays(1));
    Assert.Throws<OperationCanceledException>(() =>
    { _ = coordinator.FlushAsync(AppSettings.Default, cancellationToken: canceled.Token); });
    coordinator.Schedule(AppSettings.Default with { InsertionBlockedProcessNames = names });
    names[0] = "changed";
    await coordinator.DisposeAsync();
    Assert.Equal("original", observed);
  }
}
