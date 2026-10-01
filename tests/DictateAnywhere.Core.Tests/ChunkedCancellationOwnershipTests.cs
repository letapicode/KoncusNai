using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class ChunkedCancellationOwnershipTests
{
  [Fact]
  public Task ThrowingChunkCancellationReportsFailureAfterWorkerDrain() => Task.Run(async () =>
  {
    Capture capture = new();
    Provider provider = new() { ThrowOnCancellation = true };
    ChunkedTranscriptionSession session = new(capture, provider, "test", new Diagnostics(), CancellationToken.None);
    Task? dispose = null;
    try
    {
      session.Start();
      capture.Emit();
      await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
      dispose = session.DisposeAsync().AsTask();
      Assert.False(dispose.IsCompleted);
      Assert.Same(dispose, session.DisposeAsync().AsTask());
      Assert.Equal(0, capture.Subscribers);
    }
    finally
    {
      provider.Release.TrySetResult();
      await Assert.ThrowsAsync<AggregateException>(() => (dispose ?? session.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
    }
  });

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public Task DisposalPublicationClosesAdmissionBeforeItsContinuationRuns(bool completing) => Task.Run(async () =>
  {
    Capture capture = new();
    Provider provider = new();
    ChunkedTranscriptionSession session = new(capture, provider, "test", new Diagnostics(), CancellationToken.None);
    HeldContext held = new();
    SynchronizationContext? previous = SynchronizationContext.Current;
    Task? complete = null;
    Task? dispose = null;
    try
    {
      session.Start();
      Action saved = capture.Snapshot();
      capture.Emit();
      await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
      if (completing) complete = session.CompleteAsync(CancellationToken.None);
      SynchronizationContext.SetSynchronizationContext(held);
      try { dispose = session.DisposeAsync().AsTask(); }
      finally { SynchronizationContext.SetSynchronizationContext(previous); }
      Assert.False(dispose.IsCompleted);
      saved();
      Assert.Equal(TimeSpan.FromMilliseconds(1), session.TotalDuration);
      provider.Release.TrySetResult();
      if (complete is not null)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => complete.WaitAsync(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
      else
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.CompleteAsync(CancellationToken.None)).ConfigureAwait(false);
    }
    finally
    {
      SynchronizationContext.SetSynchronizationContext(previous);
      held.Resume();
      provider.Release.TrySetResult();
      if (complete is not null) { try { await complete.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); } catch (OperationCanceledException) { } }
      await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }
  });

  private sealed class HeldContext : SynchronizationContext
  {
    private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = new();
    public override void Post(SendOrPostCallback d, object? state) => callbacks.Enqueue((d, state));
    internal void Resume() { while (callbacks.TryDequeue(out var work)) work.Callback(work.State); }
  }

  [Fact]
  public Task CompletionAfterDisposalIsRejectedInsteadOfReturningClearedResults() => Task.Run(async () =>
  {
    Capture capture = new();
    Provider provider = new();
    ChunkedTranscriptionSession session = new(capture, provider, "test", new Diagnostics(), CancellationToken.None);
    await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    await Assert.ThrowsAsync<ObjectDisposedException>(() => session.CompleteAsync(CancellationToken.None)).ConfigureAwait(false);
  });

  [Fact]
  public Task CompletionAndRepeatedDisposalRetainAnUncooperativeChunkWorker() => Task.Run(async () =>
  {
    Capture capture = new();
    Provider provider = new();
    ChunkedTranscriptionSession session = new(capture, provider, "test", new Diagnostics(), CancellationToken.None);
    Task? complete = null;
    Task? dispose = null;
    try
    {
      session.Start();
      Action lateCallback = capture.Snapshot();
      capture.Emit();
      await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
      complete = session.CompleteAsync(CancellationToken.None);
      dispose = session.DisposeAsync().AsTask();
      Assert.Same(dispose, session.DisposeAsync().AsTask());
      Assert.False(complete.IsCompleted);
      Assert.False(dispose.IsCompleted);
      lateCallback();
      Assert.Equal(TimeSpan.FromMilliseconds(1), session.TotalDuration);
      provider.Release.TrySetResult();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => complete.WaitAsync(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
      await dispose.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
      Assert.Equal(1, provider.Calls);
      Assert.Equal(0, capture.Subscribers);
    }
    finally
    {
      provider.Release.TrySetResult();
      if (complete is not null)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => complete.WaitAsync(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
      await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }
  });

  private sealed class Capture : IChunkedAudioCaptureService
  {
    public event EventHandler<AudioCaptureChunkAvailableEventArgs>? ChunkAvailable;
    internal int Subscribers => ChunkAvailable?.GetInvocationList().Length ?? 0;
    public bool IsCapturing => false;
    internal Action Snapshot()
    {
      EventHandler<AudioCaptureChunkAvailableEventArgs>? callback = ChunkAvailable;
      return () => callback?.Invoke(this, new(new(0, new([1, 0], 16000, TimeSpan.FromMilliseconds(1)), false, DateTimeOffset.UtcNow)));
    }
    internal void Emit() => Snapshot()();
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StartChunkedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AudioCaptureResult([], 16000, TimeSpan.Zero));
    public Task<AudioCaptureChunk?> StopAndFlushChunkAsync(CancellationToken cancellationToken = default) => Task.FromResult<AudioCaptureChunk?>(null);
  }

  private sealed class Provider : ITranscriptionService
  {
    internal int Calls { get; private set; }
    internal bool ThrowOnCancellation { get; init; }
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<TranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string modelId, CancellationToken cancellationToken = default)
    {
      Calls++;
      using CancellationTokenRegistration registration = cancellationToken.Register(() =>
      {
        if (ThrowOnCancellation) throw new IOException("controlled chunk cancellation failure");
      });
      Entered.TrySetResult();
      await Release.Task.ConfigureAwait(false);
      return new("test", modelId, TimeSpan.Zero);
    }
  }

  private sealed class Diagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
