using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Core.Tests;

public sealed class FaultTolerantOverlayServiceTests
{
  [Xunit.Fact]
  public async Task ShowStateAsync_WhenPresenterFails_LogsAndReturns()
  {
    ThrowingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    await using FaultTolerantOverlayService service = new(
      inner,
      diagnostics,
      TimeSpan.FromMilliseconds(100));

    await service.ShowStateAsync(DictationSessionState.Inserting);

    string warning = Xunit.Assert.Single(diagnostics.Warnings);
    Xunit.Assert.Contains("show Inserting", warning, StringComparison.Ordinal);
    Xunit.Assert.Contains("dictation will continue", warning, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_WhenPresenterDoesNotComplete_TimesOutAndReturns()
  {
    HangingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    ManualTimeProvider timeProvider = new();
    FaultTolerantOverlayService service = new(
      inner,
      diagnostics,
      TimeSpan.FromSeconds(2),
      timeProvider);

    Task show = service.ShowStateAsync(DictationSessionState.Inserting);
    Xunit.Assert.False(show.IsCompleted);
    timeProvider.FireTimers();
    await show.WaitAsync(TimeSpan.FromSeconds(10));

    string warning = Xunit.Assert.Single(diagnostics.Warnings);
    Xunit.Assert.Contains("timed out", warning, StringComparison.OrdinalIgnoreCase);

    inner.Complete();
    await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_WhenCallerCancelsInFlight_PropagatesCancellation()
  {
    HangingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    ManualTimeProvider timeProvider = new();
    FaultTolerantOverlayService service = new(inner, diagnostics, TimeSpan.FromSeconds(2), timeProvider);
    using CancellationTokenSource cancellation = new();

    Task show = service.ShowStateAsync(DictationSessionState.Recording, cancellationToken: cancellation.Token);
    cancellation.Cancel();
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => show.WaitAsync(TimeSpan.FromSeconds(10)));
    Xunit.Assert.Empty(diagnostics.Warnings);

    inner.Complete();
    await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
  }

  [Xunit.Fact]
  public async Task DisposeAsync_WhenTimedOutPresenterNeverSettles_ReturnsWithWarning()
  {
    HangingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    ManualTimeProvider timeProvider = new();
    FaultTolerantOverlayService service = new(inner, diagnostics, TimeSpan.FromSeconds(2), timeProvider);

    Task show = service.ShowStateAsync(DictationSessionState.Inserting);
    timeProvider.FireTimers();
    await show.WaitAsync(TimeSpan.FromSeconds(10));

    Task dispose = service.DisposeAsync().AsTask();
    Xunit.Assert.False(dispose.IsCompleted);
    timeProvider.FireTimers();
    await dispose.WaitAsync(TimeSpan.FromSeconds(10));

    Xunit.Assert.Contains(diagnostics.Warnings, warning =>
      warning.Contains("did not settle", StringComparison.OrdinalIgnoreCase));
    inner.Complete();
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_WhenTimedOutPresenterLaterFails_ObservesFailure()
  {
    HangingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    ManualTimeProvider timeProvider = new();
    FaultTolerantOverlayService service = new(inner, diagnostics, TimeSpan.FromSeconds(2), timeProvider);

    Task show = service.ShowStateAsync(DictationSessionState.Inserting);
    timeProvider.FireTimers();
    await show.WaitAsync(TimeSpan.FromSeconds(10));
    inner.Fail(new InvalidOperationException("late presenter failure"));
    await diagnostics.LateFailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
    await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    Xunit.Assert.Contains(diagnostics.Warnings, warning =>
      warning.Contains("late presenter failure", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task DisposeAsync_WhenInnerDisposalNeverCompletes_ReturnsWithWarning()
  {
    await using HangingDisposalOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    ManualTimeProvider timeProvider = new();
    FaultTolerantOverlayService service = new(inner, diagnostics, TimeSpan.FromSeconds(2), timeProvider);

    Task dispose = service.DisposeAsync().AsTask();
    Xunit.Assert.False(dispose.IsCompleted);
    timeProvider.FireTimers();
    await dispose.WaitAsync(TimeSpan.FromSeconds(10));

    Xunit.Assert.Contains(diagnostics.Warnings, warning =>
      warning.Contains("disposal timed out", StringComparison.OrdinalIgnoreCase));
    inner.CompleteDisposal();
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_WhenCallerCancels_PropagatesCancellation()
  {
    HangingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    await using FaultTolerantOverlayService service = new(
      inner,
      diagnostics,
      TimeSpan.FromSeconds(1));
    using CancellationTokenSource cancellation = new();
    cancellation.Cancel();

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => service.ShowStateAsync(DictationSessionState.Recording, cancellationToken: cancellation.Token));

    Xunit.Assert.Empty(diagnostics.Warnings);
  }

  private sealed class ThrowingOverlayService : IOverlayService
  {
    public Task ShowStateAsync(
      DictationSessionState state,
      string? message = null,
      TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null,
      CancellationToken cancellationToken = default)
    {
      throw new InvalidOperationException("presenter failed");
    }

    public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class HangingOverlayService : IOverlayService
  {
    private readonly TaskCompletionSource<bool> completion =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ShowStateAsync(
      DictationSessionState state,
      string? message = null,
      TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null,
      CancellationToken cancellationToken = default)
    {
      return completion.Task;
    }

    public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Complete() => completion.TrySetResult(true);

    public void Fail(Exception exception) => completion.TrySetException(exception);
  }

  private sealed class HangingDisposalOverlayService : IOverlayService, IAsyncDisposable
  {
    private readonly TaskCompletionSource<bool> disposal =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ShowStateAsync(
      DictationSessionState state,
      string? message = null,
      TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null,
      CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => new(disposal.Task);

    public void CompleteDisposal() => disposal.TrySetResult(true);
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<string> Warnings { get; } = new();
    public TaskCompletionSource<bool> LateFailureObserved { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
      Warnings.Add(message);
      if (message.Contains("later failed", StringComparison.Ordinal))
      {
        LateFailureObserved.TrySetResult(true);
      }
    }

    public void Error(string message, Exception? exception = null)
    {
    }
  }

  private sealed class ManualTimeProvider : TimeProvider
  {
    private readonly List<ManualTimer> timers = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
      ManualTimer timer = new(callback, state);
      timers.Add(timer);
      return timer;
    }

    public void FireTimers()
    {
      foreach (ManualTimer timer in timers.ToArray())
      {
        timer.Fire();
      }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
      private bool active = true;

      public bool Change(TimeSpan dueTime, TimeSpan period)
      {
        active = true;
        return true;
      }

      public void Fire()
      {
        if (!active)
        {
          return;
        }

        active = false;
        callback(state);
      }

      public void Dispose() => active = false;

      public ValueTask DisposeAsync()
      {
        Dispose();
        return ValueTask.CompletedTask;
      }
    }
  }
}
