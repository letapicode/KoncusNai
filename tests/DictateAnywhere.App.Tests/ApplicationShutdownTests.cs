using DictateAnywhere.App.Lifecycle;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class ApplicationShutdownTests
{
  private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

  [Fact]
  public void QuitWaitsForOwnedCleanupAndRepeatedRequestsShareCompletion() => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource release = Gate();
    context.ReleaseOnTimeout(() => release.TrySetResult());
    bool finalStage = false;
    ApplicationShutdown shutdown = new(TimeSpan.FromSeconds(2), (_, _) => { }, _ => Gate().Task);
    Task<bool> first = shutdown.RunAsync(
      new CleanupStep("Owned work", () => release.Task),
      LifecycleCleanup.Sync("Dispatcher exit", () => finalStage = true));
    Task<bool> second = shutdown.RunAsync(LifecycleCleanup.Sync("Unexpected second quit", () => throw new InvalidOperationException()));
    try
    {
      Assert.Same(first, second);
      Assert.False(first.IsCompleted);
      Assert.False(finalStage);
      release.SetResult();
      Assert.True(await first);
      Assert.True(finalStage);
    }
    finally { release.TrySetResult(); await first; }
  });

  [Fact]
  public void ReentrantQuitSeesPublishedCompletionAndRunsOnce() => LifecycleTestContext.Run(async _ =>
  {
    Task<bool>? reentrant = null;
    int calls = 0;
    ApplicationShutdown shutdown = new(TimeSpan.FromSeconds(2), (_, _) => { });
    Task<bool> result = shutdown.RunAsync(LifecycleCleanup.Sync("Callback", () =>
    {
      calls++;
      reentrant = shutdown.RunAsync();
    }));
    Assert.Same(result, reentrant);
    Assert.True(await result);
    Assert.Equal(1, calls);
  });

  [Fact]
  public void FaultCancellationAndFailingReporterDoNotPreventOtherCleanup() => LifecycleTestContext.Run(async _ =>
  {
    int attempts = 0;
    bool finalStage = false;
    ApplicationShutdown shutdown = new(TimeSpan.FromSeconds(2), (_, _) =>
    {
      attempts++;
      throw new IOException("Reporting disk failure");
    });
    bool complete = await shutdown.RunAsync(
      new CleanupStep("Synchronous failure", () => throw new IOException()),
      new CleanupStep("Asynchronous failure", () => Task.FromException(new InvalidOperationException())),
      new CleanupStep("Cancelled", () => Task.FromCanceled(new CancellationToken(true))),
      LifecycleCleanup.Sync("Remaining owner", () => finalStage = true));
    Assert.False(complete);
    Assert.Equal(3, attempts);
    Assert.True(finalStage);
  });

  [Fact]
  public void DeadlineReportsIncompleteCleanupAndObservesItsLaterFault() => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource deadline = Gate();
    TaskCompletionSource work = Gate();
    TaskCompletionSource lateFault = Gate();
    context.ReleaseOnTimeout(() => { deadline.TrySetResult(); work.TrySetException(new IOException()); });
    int reports = 0;
    bool otherOwnerCleaned = false;
    ApplicationShutdown shutdown = new(TimeSpan.FromSeconds(5), (_, exception) =>
    {
      reports++;
      if (exception is IOException) lateFault.TrySetResult();
    }, _ => deadline.Task);
    Task<bool> result = shutdown.RunAsync(
      new CleanupStep("Hung worker", () => work.Task),
      LifecycleCleanup.Sync("Independent owner", () => otherOwnerCleaned = true));
    try
    {
      Assert.False(result.IsCompleted);
      deadline.SetResult();
      Assert.False(await result);
      Assert.True(otherOwnerCleaned);
      Assert.False(work.Task.IsCompleted);
      Assert.Equal(1, reports);
      work.SetException(new IOException("Worker eventually failed"));
      await lateFault.Task;
      Assert.Equal(2, reports);
    }
    finally
    {
      deadline.TrySetResult();
      await result;
      work.TrySetException(new IOException("Worker eventually failed"));
      await lateFault.Task;
    }
  });

  [Fact]
  public void CleanupAggregatesFaultsOnlyAfterIndependentOwnersFinish() => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource release = Gate();
    context.ReleaseOnTimeout(() => release.TrySetResult());
    Task result = LifecycleCleanup.RunAsync(
      new CleanupStep("First", () => throw new IOException()),
      new CleanupStep("Second", () => release.Task));
    try
    {
      Assert.False(result.IsCompleted);
      release.SetResult();
      AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => result);
      Assert.Single(exception.InnerExceptions);
    }
    finally { release.TrySetResult(); await Assert.ThrowsAsync<AggregateException>(() => result); }
  });
}
