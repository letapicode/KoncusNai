using DictateAnywhere.App.Lifecycle;

namespace DictateAnywhere.App.Tests;

public sealed class CoalescingRefreshSessionTests
{
  [Xunit.Fact]
  public async Task TryRequestRefresh_RunsOnePassAndClearsState()
  {
    int refreshCount = 0;
    await using CoalescingRefreshSession session = new(_ =>
    {
      refreshCount++;
      return Task.CompletedTask;
    });

    Xunit.Assert.True(session.TryRequestRefresh(out Task refreshRun));
    await refreshRun;

    Xunit.Assert.Equal(1, refreshCount);
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task TryRequestRefresh_CoalescesPendingRequestsWithoutOverlappingPasses()
  {
    TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseSecond = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int refreshCount = 0;
    int concurrentCount = 0;
    int maximumConcurrentCount = 0;
    await using CoalescingRefreshSession session = new(async _ =>
    {
      int pass = Interlocked.Increment(ref refreshCount);
      int concurrent = Interlocked.Increment(ref concurrentCount);
      maximumConcurrentCount = Math.Max(maximumConcurrentCount, concurrent);
      try
      {
        if (pass == 1)
        {
          firstStarted.SetResult();
          await releaseFirst.Task;
        }
        else
        {
          secondStarted.SetResult();
          await releaseSecond.Task;
        }
      }
      finally
      {
        Interlocked.Decrement(ref concurrentCount);
      }
    });

    Xunit.Assert.True(session.TryRequestRefresh(out Task refreshRun));
    await firstStarted.Task;
    Xunit.Assert.False(session.TryRequestRefresh(out Task coalescedRun));
    Xunit.Assert.False(session.TryRequestRefresh(out Task secondCoalescedRun));
    Xunit.Assert.Same(refreshRun, coalescedRun);
    Xunit.Assert.Same(refreshRun, secondCoalescedRun);

    releaseFirst.SetResult();
    await secondStarted.Task;
    Xunit.Assert.Equal(2, refreshCount);
    releaseSecond.SetResult();
    await refreshRun;

    Xunit.Assert.Equal(2, refreshCount);
    Xunit.Assert.Equal(1, maximumConcurrentCount);
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task TryRequestRefresh_AfterCompletionStartsANewRun()
  {
    int refreshCount = 0;
    await using CoalescingRefreshSession session = new(_ =>
    {
      refreshCount++;
      return Task.CompletedTask;
    });

    Xunit.Assert.True(session.TryRequestRefresh(out Task firstRun));
    await firstRun;
    Xunit.Assert.True(session.TryRequestRefresh(out Task secondRun));
    await secondRun;

    Xunit.Assert.Equal(2, refreshCount);
    Xunit.Assert.NotSame(firstRun, secondRun);
  }

  [Xunit.Fact]
  public async Task TryRequestRefresh_PropagatesFailureAndAllowsANewRun()
  {
    bool shouldFail = true;
    await using CoalescingRefreshSession session = new(_ =>
    {
      if (shouldFail)
      {
        throw new InvalidOperationException("refresh failed");
      }

      return Task.CompletedTask;
    });

    Xunit.Assert.True(session.TryRequestRefresh(out Task failedRun));
    InvalidOperationException exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => failedRun);
    Xunit.Assert.Equal("refresh failed", exception.Message);
    Xunit.Assert.False(session.IsRunning);

    shouldFail = false;
    Xunit.Assert.True(session.TryRequestRefresh(out Task successfulRun));
    await successfulRun;
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task TryRequestRefresh_DoesNotDropACoalescedPassAfterFailure()
  {
    TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int refreshCount = 0;
    await using CoalescingRefreshSession session = new(async _ =>
    {
      int pass = Interlocked.Increment(ref refreshCount);
      if (pass == 1)
      {
        firstStarted.SetResult();
        await releaseFirst.Task;
        throw new InvalidOperationException("first pass failed");
      }
    });

    Xunit.Assert.True(session.TryRequestRefresh(out Task refreshRun));
    await firstStarted.Task;
    Xunit.Assert.False(session.TryRequestRefresh(out _));
    releaseFirst.SetResult();

    InvalidOperationException exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => refreshRun);
    Xunit.Assert.Equal("first pass failed", exception.Message);
    Xunit.Assert.Equal(2, refreshCount);
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsAndWaitsForTheActiveRun()
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken refreshToken = default;
    CoalescingRefreshSession session = new(async token =>
    {
      refreshToken = token;
      started.SetResult();
      await release.Task;
      token.ThrowIfCancellationRequested();
    });

    Xunit.Assert.True(session.TryRequestRefresh(out Task refreshRun));
    await started.Task;
    ValueTask firstDisposal = session.DisposeAsync();
    ValueTask secondDisposal = session.DisposeAsync();

    Xunit.Assert.True(refreshToken.IsCancellationRequested);
    Xunit.Assert.False(firstDisposal.IsCompleted);
    Xunit.Assert.False(secondDisposal.IsCompleted);
    Xunit.Assert.False(session.TryRequestRefresh(out Task rejectedRun));
    await rejectedRun;
    release.SetResult();
    await refreshRun;
    await firstDisposal;
    await secondDisposal;
    Xunit.Assert.False(session.IsRunning);
  }
}
