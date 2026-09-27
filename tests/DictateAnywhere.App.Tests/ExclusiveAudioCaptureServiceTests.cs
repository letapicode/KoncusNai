using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using Xunit;
using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

[SuppressMessage("Reliability", "CA2000", Justification = "The awaited capture wrapper owns and disposes its inner fake.")]
public sealed class ExclusiveAudioCaptureServiceTests
{
  [Fact]
  public async Task CompetingRecordingIsRejectedAndStopReleasesOwnership()
  {
    await using ExclusiveAudioCaptureService first = new(new FakeCapture());
    await using ExclusiveAudioCaptureService second = new(new FakeCapture());
    await first.StartAsync();
    await Assert.ThrowsAsync<InvalidOperationException>(() => second.StartChunkedAsync());
    Assert.False(second.IsCapturing);
    await first.StopAsync();
    await second.StartChunkedAsync();
    await second.StopAndFlushChunkAsync();
    await first.StartAsync();
  }

  [Fact]
  public async Task FailedStartupReleasesOwnership()
  {
    await using ExclusiveAudioCaptureService broken = new(new FakeCapture { FailStart = true });
    await using ExclusiveAudioCaptureService working = new(new FakeCapture());
    await Assert.ThrowsAsync<InvalidOperationException>(() => broken.StartAsync());
    await working.StartAsync();
    Assert.True(working.IsCapturing);
  }

  [Fact]
  public async Task PartiallyAcquiredStartupRetainsOwnershipUntilStopped()
  {
    await using ExclusiveAudioCaptureService broken = new(new FakeCapture { FailAfterAcquisition = true });
    await using ExclusiveAudioCaptureService other = new(new FakeCapture());
    await Assert.ThrowsAsync<InvalidOperationException>(() => broken.StartAsync());
    await Assert.ThrowsAsync<InvalidOperationException>(() => other.StartAsync());
    await broken.StopAsync();
    await other.StartAsync();
  }

  [Fact]
  public async Task ChunkedCaptureCanStartDuringOtherTranscriptionWithoutCancellingIt()
  {
    TaskCompletionSource<TranscriptionResult> inference = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await using WorkbenchDictationController microphone = new(new Diagnostics(),
      _ => new ExclusiveAudioCaptureService(new FakeCapture { CaptureData = [1, 2] }),
      (_, _) => new PendingTranscription(inference.Task), () => throw new InvalidOperationException("hotkeys not used"));
    await using ExclusiveAudioCaptureService hotkey = new(new FakeCapture());
    await microphone.ConfigureAsync(AppSettings.Default, false);
    await microphone.StartRecordingAsync();
    Task<WorkbenchTranscriptionOutcome> transcription = microphone.StopAndTranscribeAsync();
    Assert.False(transcription.IsCompleted);
    await hotkey.StartChunkedAsync();
    Assert.True(hotkey.IsCapturing);
    inference.SetResult(new TranscriptionResult("first transcript", "model", TimeSpan.Zero));
    Assert.Equal("first transcript", (await transcription).Text);
    Assert.True(hotkey.IsCapturing);
    await hotkey.StopAndFlushChunkAsync();
  }

  private sealed class PendingTranscription(Task<TranscriptionResult> result) : ITranscriptionService
  {
    public Task<TranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string modelId,
      CancellationToken cancellationToken = default) => result.WaitAsync(cancellationToken);
  }

  private sealed class Diagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }

  private sealed class FakeCapture : IChunkedAudioCaptureService, IAsyncDisposable
  {
    public bool FailStart { get; init; }
    public bool FailAfterAcquisition { get; init; }
    public byte[] CaptureData { get; init; } = [];
    public bool IsCapturing { get; private set; }
    public event EventHandler<AudioCaptureChunkAvailableEventArgs>? ChunkAvailable { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      if (FailStart) throw new InvalidOperationException("device unavailable");
      IsCapturing = true;
      if (FailAfterAcquisition) throw new InvalidOperationException("startup failed after acquisition");
      return Task.CompletedTask;
    }
    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = false;
      return Task.FromResult(new AudioCaptureResult(CaptureData, 16000, TimeSpan.Zero));
    }
    public Task<AudioCaptureChunk?> StopAndFlushChunkAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = false;
      return Task.FromResult<AudioCaptureChunk?>(null);
    }
    public ValueTask DisposeAsync()
    {
      IsCapturing = false;
      return ValueTask.CompletedTask;
    }
  }
}
