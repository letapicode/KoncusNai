using DictateAnywhere.App.Lifecycle;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class WindowLifetimeRegistryTests
{
  private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

  [Fact]
  public void QuitRetainsAlreadyClosingWindowAlongsideReplacementAndIndependentReaders() => LifecycleTestContext.Run(async context =>
  {
    WindowLifetimeRegistry windows = new();
    object oldWorkbench = new();
    object newWorkbench = new();
    object firstReader = new();
    object secondReader = new();
    TaskCompletionSource releaseOld = Gate();
    context.ReleaseOnTimeout(() => releaseOld.TrySetResult());
    int oldCleanupCount = 0;
    List<object> closed = [];
    windows.Register(oldWorkbench, () => closed.Add(oldWorkbench), () =>
    {
      oldCleanupCount++;
      return releaseOld.Task;
    });
    try
    {
      Task closing = windows.CloseAsync(oldWorkbench);
      foreach (object window in new[] { newWorkbench, firstReader, secondReader })
        windows.Register(window, () => closed.Add(window), () => Task.CompletedTask);
      Task quit = windows.DisposeAsync();
      Assert.Same(quit, windows.DisposeAsync());
      Assert.Same(closing, windows.CloseAsync(oldWorkbench));
      Assert.False(quit.IsCompleted);
      Assert.Equal(4, closed.Count);
      Assert.Throws<ObjectDisposedException>(() => windows.Register(new object(), () => { }, () => Task.CompletedTask));
      releaseOld.SetResult();
      await quit;
      Assert.Equal(1, oldCleanupCount);
    }
    finally
    {
      releaseOld.TrySetResult();
      await windows.DisposeAsync();
    }
  });

  [Fact]
  public void CloseFailureStillDisposesThatWindowAndOtherWindows() => LifecycleTestContext.Run(async _ =>
  {
    WindowLifetimeRegistry windows = new();
    bool failedWindowDisposed = false;
    bool otherWindowDisposed = false;
    windows.Register(new object(), () => throw new IOException("Close failed"), () =>
    {
      failedWindowDisposed = true;
      return Task.CompletedTask;
    });
    windows.Register(new object(), () => { }, () =>
    {
      otherWindowDisposed = true;
      return Task.CompletedTask;
    });
    await Assert.ThrowsAsync<AggregateException>(() => windows.DisposeAsync());
    Assert.True(failedWindowDisposed);
    Assert.True(otherWindowDisposed);
  });

  [Fact]
  public void CloseCallbackCanReenterWithoutStartingAnotherDisposal() => LifecycleTestContext.Run(async _ =>
  {
    WindowLifetimeRegistry windows = new();
    object window = new();
    Task? reentrant = null;
    int disposals = 0;
    windows.Register(window, () => reentrant = windows.CloseAsync(window), () =>
    {
      disposals++;
      return Task.CompletedTask;
    });
    Task close = windows.CloseAsync(window);
    Assert.Same(close, reentrant);
    await close;
    windows.Register(window, () => { }, () => Task.CompletedTask);
    await windows.DisposeAsync();
    Assert.Equal(1, disposals);
  });

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  public void CrossWindowReentrantCloseDoesNotLoseSettledFailure(int outcome) => LifecycleTestContext.Run(async _ =>
  {
    WindowLifetimeRegistry windows = new();
    object second = new();
    Task? reentrant = null;
    windows.Register(new object(), () => reentrant = windows.CloseAsync(second, alreadyClosed: true), () => Task.CompletedTask);
    int cleanups = 0;
    windows.Register(second, () => throw new InvalidOperationException("Must not close twice"), () =>
    {
      cleanups++;
      return outcome switch
      {
        0 => throw new IOException("Cleanup failed"),
        1 => Task.FromException(new IOException("Cleanup failed")),
        2 => Task.FromCanceled(new CancellationToken(true)),
        _ => Task.CompletedTask
      };
    });
    Task quit = windows.DisposeAsync();
    if (outcome == 3) { await reentrant!; await quit; }
    else
    {
      await Assert.ThrowsAsync<AggregateException>(() => reentrant!);
      await Assert.ThrowsAsync<AggregateException>(() => quit);
    }
    Assert.Equal(1, cleanups);
  });

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ReentrantQuitRetainsOwnerAndAffinity(bool queued) => LifecycleTestContext.Run(async context =>
  {
    WindowLifetimeRegistry windows = new();
    object window = new();
    TaskCompletionSource release = new(queued ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);
    context.ReleaseOnTimeout(() => release.TrySetResult());
    Task? quit = null;
    int thread = Environment.CurrentManagedThreadId;
    windows.Register(window, () => quit = windows.DisposeAsync(), async () =>
    {
      await release.Task;
      Assert.Equal(thread, Environment.CurrentManagedThreadId);
    });
    Task close = windows.CloseAsync(window);
    try
    {
      Assert.Same(quit, windows.DisposeAsync());
      Assert.False(quit!.IsCompleted);
      Assert.Throws<TimeoutException>(() => context.Drain(quit, TimeSpan.Zero));
    }
    finally { release.TrySetResult(); await close; await quit!; }
  });

  [Fact]
  public void RunnerQueueBacklogCanDelayCompletedCleanup() => LifecycleTestContext.Run(async context =>
  {
    using Xunit.Sdk.MaxConcurrencySyncContext runner = new(1);
    using ManualResetEventSlim unblock = new();
    TaskCompletionSource<Task> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource entered = Gate();
    TaskCompletionSource release = Gate();
    context.ReleaseOnTimeout(() => { release.TrySetResult(); unblock.Set(); });
    runner.Post(_ =>
    {
      WindowLifetimeRegistry windows = new();
      windows.Register(new object(), () => { }, () => release.Task);
      started.SetResult(windows.DisposeAsync());
    }, null);
    runner.Post(_ =>
    {
      entered.SetResult();
      unblock.Wait(TimeSpan.FromSeconds(15));
    }, null);
    Task? quit = null;
    try
    {
      quit = await started.Task;
      await entered.Task;
      release.SetResult();
      Assert.True(release.Task.IsCompleted);
      Assert.False(quit.IsCompleted);
      await Assert.ThrowsAsync<TimeoutException>(() => quit.WaitAsync(TimeSpan.Zero));
    }
    finally
    {
      release.TrySetResult();
      unblock.Set();
      if (quit is not null) await quit;
    }
  });

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void FailureOrHarnessTimeoutReleasesWorkAndRestoresContext(bool timeout)
  {
    SynchronizationContext? previous = SynchronizationContext.Current;
    Task? closing = null;
    Exception failure = Assert.ThrowsAny<Exception>(() => LifecycleTestContext.Run(async context =>
    {
      WindowLifetimeRegistry windows = new();
      TaskCompletionSource release = Gate();
      context.ReleaseOnTimeout(() => release.TrySetResult());
      windows.Register(new object(), () => { }, () => release.Task);
      closing = windows.DisposeAsync();
      try
      {
        if (!timeout) throw new IOException("Injected assertion failure");
        await closing;
      }
      finally { release.TrySetResult(); await closing; }
    }, timeout ? TimeSpan.Zero : null));
    Assert.Equal(timeout ? typeof(TimeoutException) : typeof(IOException), failure.GetType());
    Assert.True(closing!.IsCompletedSuccessfully);
    Assert.Same(previous, SynchronizationContext.Current);
  }
}
