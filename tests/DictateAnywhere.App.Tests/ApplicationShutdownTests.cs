using DictateAnywhere.App.Lifecycle;

namespace DictateAnywhere.App.Tests;

public sealed class ApplicationShutdownTests
{
  private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

  [Xunit.Fact]
  public async Task QuitWaitsForOwnedCleanupAndRepeatedRequestsShareCompletion()
  {
    TaskCompletionSource release = Gate();
    bool finalStage = false;
    ApplicationShutdown shutdown = new(TimeSpan.FromSeconds(2), (_, _) => { });
    Task<bool> first = shutdown.RunAsync(
      new CleanupStep("Owned work", () => release.Task),
      LifecycleCleanup.Sync("Dispatcher exit", () => finalStage = true));
    Task<bool> second = shutdown.RunAsync(LifecycleCleanup.Sync("Unexpected second quit", () => throw new InvalidOperationException()));
    Xunit.Assert.Same(first, second);
    Xunit.Assert.False(first.IsCompleted);
    Xunit.Assert.False(finalStage);
    release.SetResult();
    Xunit.Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(3)));
    Xunit.Assert.True(finalStage);
  }

  [Xunit.Fact]
  public async Task ReentrantQuitSeesPublishedCompletionAndRunsOnce()
  {
    Task<bool>? reentrant = null;
    int calls = 0;
    ApplicationShutdown shutdown = new(TimeSpan.FromSeconds(2), (_, _) => { });
    Task<bool> result = shutdown.RunAsync(LifecycleCleanup.Sync("Callback", () =>
    {
      calls++;
      reentrant = shutdown.RunAsync();
    }));
    Xunit.Assert.Same(result, reentrant);
    Xunit.Assert.True(await result);
    Xunit.Assert.Equal(1, calls);
  }

  [Xunit.Fact]
  public async Task FaultCancellationAndFailingReporterDoNotPreventOtherCleanup()
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
    Xunit.Assert.False(complete);
    Xunit.Assert.Equal(3, attempts);
    Xunit.Assert.True(finalStage);
  }

  [Xunit.Fact]
  public async Task DeadlineReportsIncompleteCleanupAndObservesItsLaterFault()
  {
    TaskCompletionSource deadline = Gate();
    TaskCompletionSource work = Gate();
    TaskCompletionSource lateFault = Gate();
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
    Xunit.Assert.False(result.IsCompleted);
    deadline.SetResult();
    Xunit.Assert.False(await result.WaitAsync(TimeSpan.FromSeconds(2)));
    Xunit.Assert.True(otherOwnerCleaned);
    Xunit.Assert.False(work.Task.IsCompleted);
    Xunit.Assert.Equal(1, reports);
    work.SetException(new IOException("Worker eventually failed"));
    await lateFault.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Xunit.Assert.Equal(2, reports);
  }

  [Xunit.Fact]
  public async Task CleanupAggregatesFaultsOnlyAfterIndependentOwnersFinish()
  {
    TaskCompletionSource release = Gate();
    Task result = LifecycleCleanup.RunAsync(
      new CleanupStep("First", () => throw new IOException()),
      new CleanupStep("Second", () => release.Task));
    Xunit.Assert.False(result.IsCompleted);
    release.SetResult();
    AggregateException exception = await Xunit.Assert.ThrowsAsync<AggregateException>(() => result);
    Xunit.Assert.Single(exception.InnerExceptions);
  }
}
