using DictateAnywhere.App.Lifecycle;

namespace DictateAnywhere.App.Tests;

public sealed class LatestOperationSessionTests
{
  [Xunit.Fact]
  public async Task RunLatestAsync_ReturnsValueAndClearsState()
  {
    await using LatestOperationSession session = new();
    CancellationToken receivedToken = default;

    LatestOperationResult<string> result = await session.RunLatestAsync(token =>
    {
      receivedToken = token;
      Xunit.Assert.True(session.IsRunning);
      return Task.FromResult("complete");
    });

    Xunit.Assert.True(result.Completed);
    Xunit.Assert.Equal("complete", result.Value);
    Xunit.Assert.True(receivedToken.CanBeCanceled);
    Xunit.Assert.False(receivedToken.IsCancellationRequested);
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task RunLatestAsync_CancelsAndWaitsForThePreviousOperation()
  {
    await using LatestOperationSession session = new();
    TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken firstToken = default;
    bool secondStarted = false;

    Task<bool> first = session.RunLatestAsync(async token =>
    {
      firstToken = token;
      firstStarted.SetResult();
      await releaseFirst.Task;
      token.ThrowIfCancellationRequested();
    });
    await firstStarted.Task;

    Task<bool> second = session.RunLatestAsync(token =>
    {
      secondStarted = true;
      return Task.CompletedTask;
    });

    Xunit.Assert.True(firstToken.IsCancellationRequested);
    Xunit.Assert.False(secondStarted);
    releaseFirst.SetResult();
    Xunit.Assert.False(await first);
    Xunit.Assert.True(await second);
    Xunit.Assert.True(secondStarted);
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task RunLatestAsync_PropagatesCallerCancellation()
  {
    await using LatestOperationSession session = new();
    using CancellationTokenSource cancellationSource = new();
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken operationToken = default;

    Task<bool> operation = session.RunLatestAsync(async token =>
    {
      operationToken = token;
      started.SetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }, cancellationSource.Token);
    await started.Task;
    cancellationSource.Cancel();

    Xunit.Assert.False(await operation);
    Xunit.Assert.True(operationToken.IsCancellationRequested);
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task CancelAndWaitAsync_CancelsAndWaitsForTheActiveOperation()
  {
    await using LatestOperationSession session = new();
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken operationToken = default;

    Task<bool> operation = session.RunLatestAsync(async token =>
    {
      operationToken = token;
      started.SetResult();
      await release.Task;
      token.ThrowIfCancellationRequested();
    });
    await started.Task;

    Task cancellation = session.CancelAndWaitAsync();

    Xunit.Assert.True(operationToken.IsCancellationRequested);
    Xunit.Assert.False(cancellation.IsCompleted);
    release.SetResult();
    Xunit.Assert.False(await operation);
    await cancellation;
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task Cancel_CancelsTheActiveOperation()
  {
    await using LatestOperationSession session = new();
    Task<bool> operation = session.RunLatestAsync(token => Task.Delay(Timeout.InfiniteTimeSpan, token));

    Xunit.Assert.True(session.Cancel());
    Xunit.Assert.False(session.Cancel());
    Xunit.Assert.False(await operation);
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task RunLatestAsync_PropagatesUnexpectedFailureAndClearsState()
  {
    await using LatestOperationSession session = new();

    InvalidOperationException exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
      session.RunLatestAsync(_ => throw new InvalidOperationException("operation failed")));

    Xunit.Assert.Equal("operation failed", exception.Message);
    Xunit.Assert.False(session.IsRunning);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsAndWaitsForTheActiveOperation()
  {
    LatestOperationSession session = new();
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken operationToken = default;

    Task<bool> operation = session.RunLatestAsync(async token =>
    {
      operationToken = token;
      started.SetResult();
      await release.Task;
      token.ThrowIfCancellationRequested();
    });
    await started.Task;

    ValueTask firstDisposal = session.DisposeAsync();
    ValueTask secondDisposal = session.DisposeAsync();

    Xunit.Assert.True(operationToken.IsCancellationRequested);
    Xunit.Assert.False(firstDisposal.IsCompleted);
    Xunit.Assert.False(secondDisposal.IsCompleted);
    await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(() =>
      session.RunLatestAsync(_ => Task.CompletedTask));
    release.SetResult();
    Xunit.Assert.False(await operation);
    await firstDisposal;
    await secondDisposal;
    Xunit.Assert.False(session.IsRunning);
  }
}
