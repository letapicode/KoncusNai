using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Tests explicitly drain shared StopAsync, including its expected failure; unverifiable fake handles remain retained by design.")]
public sealed class OllamaProcessOwnershipTests
{
  [Fact]
  public async Task EmptyOwnerAndIndependentOwnersStopOnlyTheirDirectHandles()
  {
    await using OllamaProcessOwnership empty = new();
    await empty.StopAsync();
    await using OllamaProcessOwnership first = new();
    await using OllamaProcessOwnership second = new();
    using Child a = new();
    using Child b = new();
    first.TrackProcess(a);
    second.TrackProcess(b);
    await second.StopAsync();
    Assert.Equal(0, a.Stops);
    Assert.Equal(0, a.Disposals);
    Assert.Equal(1, b.Stops);
    Assert.Equal(1, b.Disposals);
    await first.StopAsync();
    Assert.Equal(1, a.Stops);
  }

  [Fact]
  public async Task StopClosesAdmissionDrainsAllUsersAndRejectsLateResults()
  {
    OllamaProcessOwnership owner = new();
    using Child child = new();
    owner.TrackProcess(child);
    TaskCompletionSource entered = Signal();
    TaskCompletionSource release = Signal();
    Task<int> user = owner.RunAsync(async _ => { entered.SetResult(); await release.Task; return 1; });
    await entered.Task;
    Task stop = owner.StopAsync();
    try
    {
      Assert.True(owner.IsStopping);
      Assert.False(stop.IsCompleted);
      Assert.Equal(0, child.Stops);
      await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.RunAsync(_ => Task.FromResult(1)));
      Assert.Same(stop, owner.StopAsync());
      Assert.Same(stop, owner.DisposeAsync().AsTask());
    }
    finally
    {
      release.TrySetResult();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => user);
      await stop;
    }
    Assert.Equal(1, child.Stops);
    Assert.Equal(1, child.Disposals);
  }

  [Fact]
  public async Task CallerCancelBoundsStopWaitWithoutAbandoningExitOrDisposingHandle()
  {
    OllamaProcessOwnership owner = new();
    TaskCompletionSource exit = Signal();
    using Child child = new() { Exit = exit.Task };
    owner.TrackProcess(child);
    Task stop = owner.StopAsync();
    using CancellationTokenSource caller = new();
    caller.Cancel();
    try
    {
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.StopAsync(caller.Token));
      Assert.False(stop.IsCompleted);
      Assert.Equal(0, child.Disposals);
    }
    finally { exit.TrySetResult(); await stop; }
    Assert.Equal(1, child.Disposals);
  }

  [Fact]
  public async Task EveryStopIsInitiatedBeforeAwaitingBlockedExit()
  {
    OllamaProcessOwnership owner = new();
    TaskCompletionSource exit = Signal();
    using Child blocked = new() { Exit = exit.Task };
    using Child independent = new();
    owner.TrackProcess(blocked);
    owner.TrackProcess(independent);
    Task stop = owner.StopAsync();
    try
    {
      await independent.Stopping.Task;
      Assert.False(stop.IsCompleted);
      Assert.Equal(0, blocked.Disposals);
      Assert.Equal(1, independent.Stops);
    }
    finally { exit.TrySetResult(); await stop; }
  }

  [Fact]
  public async Task FailedKillRetainsHandleUntilExitAndIndependentDisposalContinues()
  {
    OllamaProcessOwnership owner = new();
    TaskCompletionSource exit = Signal();
    InvalidOperationException denied = new("termination denied");
    using Child failed = new() { Exit = exit.Task, StopFailure = denied };
    using Child independent = new() { DisposeFailure = new IOException("dispose failed") };
    owner.TrackProcess(failed);
    owner.TrackProcess(independent);
    Task stop = owner.StopAsync();
    try
    {
      await independent.Stopping.Task;
      Assert.False(stop.IsCompleted);
      Assert.Equal(0, failed.Disposals);
    }
    finally { exit.TrySetResult(); }
    AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => stop);
    Assert.Contains(denied, error.Flatten().InnerExceptions);
    Assert.Equal(1, failed.Disposals);
    Assert.Equal(1, independent.Disposals);
    Assert.Same(stop, owner.StopAsync());
  }

  [Fact]
  public async Task UnverifiableExitReportsIncompleteAndRetainsHandle()
  {
    OllamaProcessOwnership owner = new();
    IOException unavailable = new("exit identity unavailable");
    using Child child = new() { Exit = Task.FromException(unavailable) };
    owner.TrackProcess(child);
    AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => owner.StopAsync());
    Assert.Contains(unavailable, error.Flatten().InnerExceptions);
    Assert.Equal(0, child.Disposals);
  }

  [Fact]
  public async Task CancellationCallbacksAreRetainedAndFaultsDoNotPreventIndependentStops()
  {
    OllamaProcessOwnership owner = new();
    using Child child = new();
    owner.TrackProcess(child);
    TaskCompletionSource entered = Signal();
    TaskCompletionSource canceled = Signal();
    TaskCompletionSource release = Signal();
    Task<int> user = owner.RunAsync(async token =>
    {
      using CancellationTokenRegistration callback = token.Register(() => { canceled.TrySetResult(); throw new IOException("callback"); });
      entered.SetResult();
      await release.Task;
      return 1;
    });
    await entered.Task;
    Task stop = owner.StopAsync();
    try
    {
      await canceled.Task;
      Assert.False(stop.IsCompleted);
      Assert.Equal(0, child.Disposals);
    }
    finally { release.TrySetResult(); }
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => user);
    await Assert.ThrowsAsync<AggregateException>(() => stop);
    Assert.Equal(1, child.Stops);
  }

  [Fact]
  public async Task InitiatingCallbackCannotSelfAwaitButSharedStopStillDrains()
  {
    OllamaProcessOwnership owner = new();
    using Child child = new();
    owner.TrackProcess(child);
    Task? stop = null;
    Exception? mistakenAwait = null;
    Task<int> user = owner.RunAsync(async _ =>
    {
      stop = owner.RequestStop();
      mistakenAwait = await Record.ExceptionAsync(() => owner.StopAsync());
      return 1;
    });
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => user);
    Assert.IsType<InvalidOperationException>(mistakenAwait);
    await stop!;
    Assert.Equal(1, child.Stops);
  }

  [Fact]
  public async Task CopiedExecutionContextFromCompletedUseCanStopLater()
  {
    OllamaProcessOwnership owner = new();
    TaskCompletionSource release = Signal();
    Task? delayed = null;
    await owner.RunAsync(_ =>
    {
      delayed = Task.Run(async () => { await release.Task; await owner.StopAsync(); });
      return Task.FromResult(1);
    });
    release.TrySetResult();
    await delayed!;
  }

  [Fact]
  public async Task WindowBorrowedChatUsersShareRuntimeUntilApplicationStop()
  {
    OllamaProcessOwnership owner = new();
    using Child child = new();
    owner.TrackProcess(child);
    TaskCompletionSource release = Signal();
    Chat inner = new(release.Task);
    LocalChatProviderRegistry.OwnedOllamaChatService a = new(inner, owner);
    LocalChatProviderRegistry.OwnedOllamaChatService b = new(inner, owner);
    Task<ChatCompletionResult> first = a.CompleteAsync(Request());
    Task<ChatCompletionResult> second = b.CompleteAsync(Request());
    await inner.BothEntered.Task;
    Task stop = owner.StopAsync();
    try { Assert.Equal(0, child.Stops); Assert.False(stop.IsCompleted); }
    finally { release.TrySetResult(); }
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    await stop;
    Assert.Equal(1, child.Stops);
  }

  private static ChatCompletionRequest Request() => new([new(ChatMessageRoles.User, "fixture", DateTimeOffset.UtcNow)],
    new(ChatProviderIds.OllamaLocal, "gemma4:e4b"));
  internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

  [Fact]
  public async Task BlockedCancellationCallbackKeepsUsersAndSourcesOwnedUntilCallbackReturns()
  {
    OllamaProcessOwnership owner = new();
    using Child child = new();
    owner.TrackProcess(child);
    using ManualResetEventSlim releaseCallback = new();
    TaskCompletionSource entered = Signal();
    TaskCompletionSource callbackEntered = Signal();
    TaskCompletionSource releaseUser = Signal();
    Task<int> user = owner.RunAsync(async token =>
    {
      using CancellationTokenRegistration callback = token.Register(() =>
      {
        callbackEntered.TrySetResult();
        releaseCallback.Wait();
      });
      entered.TrySetResult();
      await releaseUser.Task;
      return 1;
    });
    await entered.Task;
    Task stop = owner.StopAsync();
    try
    {
      await callbackEntered.Task;
      releaseUser.TrySetResult();
      Assert.False(user.IsCompleted);
      Assert.False(stop.IsCompleted);
      Assert.Equal(0, child.Disposals);
    }
    finally
    {
      releaseCallback.Set();
      releaseUser.TrySetResult();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => user);
      await stop;
    }
  }

  private sealed class Chat(Task release) : IChatCompletionService
  {
    private int entered;
    public TaskCompletionSource BothEntered { get; } = Signal();
    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      if (Interlocked.Increment(ref entered) == 2) BothEntered.SetResult();
      await release;
      return new("fixture", ChatProviderIds.OllamaLocal, "gemma4:e4b", TimeSpan.Zero);
    }
  }

  internal sealed class Child : OllamaProcessOwnership.IProcess
  {
    public int Id => 42;
    public DateTime StartTimeUtc => new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    public string Name => "ollama";
    public string ExecutablePath => @"C:\Owned\ollama.exe";
    public bool HasExited { get; init; }
    public int ExitCode { get; init; }
    public Task Exit { get; init; } = Task.CompletedTask;
    public Exception? StopFailure { get; init; }
    public Exception? DisposeFailure { get; init; }
    public TaskCompletionSource Stopping { get; } = Signal();
    public int Stops { get; private set; }
    public int Disposals { get; private set; }
    public Task StopAsync()
    {
      Stops++;
      Stopping.TrySetResult();
      return StopFailure is null ? Task.CompletedTask : Task.FromException(StopFailure);
    }
    public Task WaitForExitAsync() => Exit;
    public void Dispose()
    {
      Disposals++;
      // Outer fixture disposal is safe and should not obscure the asserted owned-disposal fault.
      if (Disposals == 1 && DisposeFailure is not null) throw DisposeFailure;
    }
  }
}
