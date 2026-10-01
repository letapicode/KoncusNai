using DictateAnywhere.Audio.WASAPI;
using Xunit;

namespace DictateAnywhere.Audio.Tests;

public sealed class NativeCaptureLifetimeTests
{
  [Fact]
  public Task RequestReturningDoesNotFinishStopBeforeWorkerAcknowledgment() => RunAsync(false, async rig =>
  {
    Task stop = rig.Owner.StopAsync();
    await rig.Requested.Task.ConfigureAwait(false);
    Assert.Same(stop, rig.Owner.StopAsync());
    Assert.False(stop.IsCompleted);
    Task dispose = rig.Owner.DisposeAsync();
    Assert.Same(dispose, rig.Owner.DisposeAsync());
    Assert.Equal(0, rig.Disposals);
    using CancellationTokenSource wait = new();
    Task canceledWait = stop.WaitAsync(wait.Token);
    wait.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait).ConfigureAwait(false);
    Assert.False(stop.IsCompleted);
    rig.Acknowledge();
    await stop.ConfigureAwait(false);
    await rig.Disposing.Task.ConfigureAwait(false);
    Assert.False(dispose.IsCompleted);
    rig.DisposeRelease.TrySetResult();
    await dispose.ConfigureAwait(false);
    Assert.Equal(1, rig.Requests);
    Assert.Equal(1, rig.Disposals);
  });

  [Fact]
  public Task ReentrantDisposalRequestDoesNotAwaitItsOwnStopCall() => RunAsync(false, async rig =>
  {
    rig.RequestDisposalInline = true;
    Task stop = rig.Owner.StopAsync();
    await rig.Requested.Task.ConfigureAwait(false);
    Assert.Same(rig.InlineDisposal, rig.Owner.DisposeAsync());
    Assert.False(stop.IsCompleted);
    rig.Acknowledge();
    rig.DisposeRelease.TrySetResult();
    await rig.Owner.DisposeAsync().ConfigureAwait(false);
    Assert.Equal(1, rig.Disposals);
  });

  [Fact]
  public Task StopCallFaultStillAttemptsDisposalAfterTheCallDrains() => RunAsync(true, async rig =>
  {
    rig.StopFailure = new IOException("controlled stop failure");
    rig.DisposeFailure = new IOException("controlled dispose failure");
    rig.StopRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task stop = rig.Owner.StopAsync();
    await rig.Requested.Task.ConfigureAwait(false);
    Task disposal = rig.Owner.DisposeAsync();
    Assert.False(disposal.IsCompleted);
    Assert.Equal(0, rig.Disposals);
    rig.StopRelease.TrySetResult();
    await Assert.ThrowsAsync<IOException>(() => stop).ConfigureAwait(false);
    await rig.Disposing.Task.ConfigureAwait(false);
    rig.DisposeRelease.TrySetResult();
    AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => disposal).ConfigureAwait(false);
    Assert.Equal(2, failure.InnerExceptions.Count);
    Assert.Equal(1, rig.Requests);
    Assert.Equal(1, rig.Disposals);
  });

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Recovery observes the completed original test fault without replacing it, then drains all owned fake native calls.")]
  private static async Task RunAsync(bool cleanupFails, Func<Rig, Task> body)
  {
    Rig rig = new();
    Task test = Task.Run(() => body(rig));
    try { await test.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
    finally
    {
      rig.StopRelease.TrySetResult();
      rig.DisposeRelease.TrySetResult();
      rig.Acknowledge();
      try { await test.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
      catch (Exception) when (test.IsCompleted) { }
      try { await rig.Owner.DisposeAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
      catch (AggregateException) when (cleanupFails) { }
    }
  }

  private sealed class Rig
  {
    internal NativeCaptureLifetime Owner { get; }
    internal TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Disposing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource StopRelease { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource DisposeRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool RequestDisposalInline { get; set; }
    internal Task? InlineDisposal { get; private set; }
    internal Exception? StopFailure { get; set; }
    internal Exception? DisposeFailure { get; set; }
    internal int Requests { get; private set; }
    internal int Disposals { get; private set; }
    internal Rig() { StopRelease.TrySetResult(); Owner = new(RequestStop, DisposeCapture); }
    internal void Acknowledge() => Owner.RecordingStopped();
    private async Task RequestStop()
    {
      Requests++;
      if (RequestDisposalInline) InlineDisposal = Owner.DisposeAsync();
      Requested.TrySetResult();
      await StopRelease.Task.ConfigureAwait(false);
      if (StopFailure is not null) throw StopFailure;
    }
    private async Task DisposeCapture()
    {
      Disposals++;
      Disposing.TrySetResult();
      await DisposeRelease.Task.ConfigureAwait(false);
      if (DisposeFailure is not null) throw DisposeFailure;
    }
  }
}
