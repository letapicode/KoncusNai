using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Audio;

public sealed class WasapiAudioCaptureService : IChunkedAudioCaptureService, IAsyncDisposable
{
  private static readonly TimeSpan MinimumUsableTrimmedAudioDuration = TimeSpan.FromMilliseconds(250);
  private const short LowSignalThresholdPcm16 = 16;
  private const int RequiredConsecutiveSignalSamples = 4;

  private readonly object sync = new();
  private readonly IAudioInputSource inputSource;
  private readonly AudioCaptureOptions options;
  private readonly AudioRingBuffer ringBuffer;

  private MemoryStream sessionBuffer = new();
  private MemoryStream chunkBuffer = new();
  private Stopwatch? captureClock;
  private Exception? captureError;
  private bool capturing;
  private bool streamingOnly;
  private long capturedBytes;
  private int nextChunkSequenceNumber;
  private int trailingSilentChunkBytes;
  private AudioCaptureMetrics lastMetrics = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0);

  public WasapiAudioCaptureService(IAudioInputSource inputSource, AudioCaptureOptions options)
  {
    this.inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
    this.options = options ?? AudioCaptureOptions.Default;

    int ringBufferBytes = this.options.TargetSampleRateHz * 2 * 10;
    ringBuffer = new AudioRingBuffer(ringBufferBytes);

    this.inputSource.DataAvailable += OnDataAvailable;
    this.inputSource.DeviceError += OnDeviceError;
  }

  public bool IsCapturing
  {
    get
    {
      lock (sync)
      {
        return capturing;
      }
    }
  }

  public event EventHandler<AudioCaptureChunkAvailableEventArgs>? ChunkAvailable;

  public AudioCaptureMetrics LastMetrics
  {
    get
    {
      lock (sync)
      {
        return lastMetrics;
      }
    }
  }

  public Task StartAsync(CancellationToken cancellationToken = default) => StartCaptureAsync(false, cancellationToken);

  public Task StartChunkedAsync(CancellationToken cancellationToken = default) => StartCaptureAsync(true, cancellationToken);

  private Task StartCaptureAsync(bool streaming, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      if (captureClock is not null)
      {
        throw new AudioCaptureException("Audio capture already in progress.");
      }

      captureError = null;
      streamingOnly = streaming;
      capturedBytes = 0;
      ringBuffer.Clear();
      sessionBuffer.Dispose();
      chunkBuffer.Dispose();
      sessionBuffer = new MemoryStream();
      chunkBuffer = new MemoryStream();
      captureClock = Stopwatch.StartNew();
      nextChunkSequenceNumber = 0;
      trailingSilentChunkBytes = 0;
    }

    Stopwatch startStopwatch = Stopwatch.StartNew();
    return StartCoreAsync(startStopwatch, cancellationToken);
  }

  public async Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
  {
    StopCaptureSnapshot snapshot = await StopCaptureCoreAsync(cancellationToken).ConfigureAwait(false);
    return snapshot.FullAudio;
  }

  public async Task<AudioCaptureChunk?> StopAndFlushChunkAsync(CancellationToken cancellationToken = default)
  {
    StopCaptureSnapshot snapshot = await StopCaptureCoreAsync(cancellationToken).ConfigureAwait(false);
    if (snapshot.RemainingAudio.Pcm16Mono.Length == 0)
    {
      return null;
    }

    return new AudioCaptureChunk(
      snapshot.FinalSequenceNumber,
      snapshot.RemainingAudio,
      IsFinal: true,
      CapturedAtUtc: DateTimeOffset.UtcNow);
  }

  public async ValueTask DisposeAsync()
  {
    inputSource.DataAvailable -= OnDataAvailable;
    inputSource.DeviceError -= OnDeviceError;

    sessionBuffer.Dispose();
    chunkBuffer.Dispose();
    await inputSource.DisposeAsync().ConfigureAwait(false);
  }

  private async Task StartCoreAsync(Stopwatch startStopwatch, CancellationToken cancellationToken)
  {
    try
    {
      await inputSource.StartAsync(options.PreferredInputDeviceId, cancellationToken).ConfigureAwait(false);
      startStopwatch.Stop();

      lock (sync)
      {
        capturing = true;
        lastMetrics = lastMetrics with
        {
          StartLatency = startStopwatch.Elapsed,
        };
      }
    }
    catch
    {
      lock (sync)
      {
        captureClock?.Stop();
        captureClock = null;
        capturing = false;
        sessionBuffer.SetLength(0);
      }

      throw;
    }
  }

  private void OnDataAvailable(object? sender, AudioRawDataEventArgs e)
  {
    byte[] converted;
    try
    {
      converted = Pcm16AudioTransform.ConvertTo16KhzMonoPcm16(
        e.Buffer.AsSpan(0, e.BytesRecorded),
        e.SampleRateHz,
        e.Channels,
        e.BitsPerSample,
        e.SourceFormatIsFloat,
        options.TargetSampleRateHz,
        options.GainMultiplier);
    }
#pragma warning disable CA1031 // Audio callbacks must not throw; convert failures are surfaced via DeviceError.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      // Audio callbacks must never throw into the capture thread.
      OnDeviceError(this, new AudioInputErrorEventArgs(ex));
      return;
    }

    if (converted.Length == 0)
    {
      return;
    }

    AudioCaptureChunk? chunk = null;
    lock (sync)
    {
      if (captureClock is null || captureError is not null) return;
      ringBuffer.Write(converted);
      capturedBytes += converted.Length;
      if (!streamingOnly) sessionBuffer.Write(converted, 0, converted.Length);
      if (streamingOnly || options.Chunking.Enabled)
      {
        chunk = TryAppendAndCreateChunkLocked(converted);
      }
    }

    if (chunk is not null)
    {
      ChunkAvailable?.Invoke(this, new AudioCaptureChunkAvailableEventArgs(chunk));
    }
  }

  private void OnDeviceError(object? sender, AudioInputErrorEventArgs e)
  {
    lock (sync)
    {
      captureError = e.Exception;
      capturing = false;
    }
  }

  private static TimeSpan CalculateAudioDuration(byte[] pcmBytes, int sampleRateHz)
  {
    if (pcmBytes.Length == 0 || sampleRateHz <= 0)
    {
      return TimeSpan.Zero;
    }

    int sampleCount = pcmBytes.Length / 2;
    return TimeSpan.FromSeconds(sampleCount / (double)sampleRateHz);
  }

  private async Task<StopCaptureSnapshot> StopCaptureCoreAsync(CancellationToken cancellationToken)
  {
    Stopwatch stopStopwatch = Stopwatch.StartNew();
    await inputSource.StopAsync(cancellationToken).ConfigureAwait(false);
    stopStopwatch.Stop();

    lock (sync)
    {
      if (!capturing && captureClock is null)
      {
        throw new AudioCaptureException("Audio capture was not started.");
      }

      captureClock?.Stop();
      TimeSpan wallClockDuration = captureClock?.Elapsed ?? TimeSpan.Zero;
      captureClock = null;
      capturing = false;
      Exception? error = captureError;
      captureError = null;

      byte[] fullPcmBytes = streamingOnly ? [] : NormalizeCapturedAudio(sessionBuffer.ToArray(), options.SilenceThresholdPcm16);
      byte[] remainingPcmBytes = NormalizeCapturedAudio(chunkBuffer.ToArray(), options.Chunking.SilenceThresholdPcm16);
      int finalSequenceNumber = nextChunkSequenceNumber++;

      chunkBuffer.SetLength(0);
      trailingSilentChunkBytes = 0;

      lastMetrics = new AudioCaptureMetrics(
        StartLatency: lastMetrics.StartLatency,
        StopLatency: stopStopwatch.Elapsed,
        CaptureWallClockDuration: wallClockDuration,
        BytesCaptured: streamingOnly ? (int)Math.Min(int.MaxValue, capturedBytes) : fullPcmBytes.Length);

      if (error is not null)
      {
        throw new AudioCaptureException("Capture ended due to audio device failure.", error);
      }

      return new StopCaptureSnapshot(
        new AudioCaptureResult(
          fullPcmBytes,
          options.TargetSampleRateHz,
          CalculateAudioDuration(fullPcmBytes, options.TargetSampleRateHz)),
        new AudioCaptureResult(
          remainingPcmBytes,
          options.TargetSampleRateHz,
          CalculateAudioDuration(remainingPcmBytes, options.TargetSampleRateHz)),
        finalSequenceNumber);
    }
  }

  private AudioCaptureChunk? TryAppendAndCreateChunkLocked(byte[] converted)
  {
    chunkBuffer.Write(converted, 0, converted.Length);
    UpdateTrailingSilenceLocked(converted);

    int preferredBytes = CalculateByteCount(options.Chunking.PreferredChunkDuration);
    int maxBytes = CalculateByteCount(options.Chunking.MaxChunkDuration);
    int boundaryBytes = CalculateByteCount(options.Chunking.SilenceBoundaryDuration);
    bool reachedBoundary =
      chunkBuffer.Length >= preferredBytes
      && trailingSilentChunkBytes >= boundaryBytes;
    bool reachedMaximum = chunkBuffer.Length >= maxBytes;

    if (!reachedBoundary && !reachedMaximum)
    {
      return null;
    }

    byte[] pcmBytes = NormalizeCapturedAudio(chunkBuffer.ToArray(), options.Chunking.SilenceThresholdPcm16);
    chunkBuffer.SetLength(0);
    trailingSilentChunkBytes = 0;
    if (pcmBytes.Length == 0)
    {
      return null;
    }

    return new AudioCaptureChunk(
      nextChunkSequenceNumber++,
      new AudioCaptureResult(
        pcmBytes,
        options.TargetSampleRateHz,
        CalculateAudioDuration(pcmBytes, options.TargetSampleRateHz)),
      IsFinal: false,
      CapturedAtUtc: DateTimeOffset.UtcNow);
  }

  private void UpdateTrailingSilenceLocked(byte[] converted)
  {
    TrailingSilenceAnalysis analysis = AnalyzeTrailingSilence(
      converted,
      options.Chunking.SilenceThresholdPcm16);
    trailingSilentChunkBytes = analysis.AllSilent
      ? trailingSilentChunkBytes + analysis.TrailingSilentBytes
      : analysis.TrailingSilentBytes;
  }

  private byte[] NormalizeCapturedAudio(byte[] pcmBytes, short silenceThreshold)
  {
    if (pcmBytes.Length == 0)
    {
      return Array.Empty<byte>();
    }

    if (!options.EnableSilenceTrimHook)
    {
      return pcmBytes;
    }

    byte[] trimmed = Pcm16AudioTransform.TrimSilenceEdgesPcm16(pcmBytes, silenceThreshold);
    int minimumUsableBytes = CalculateByteCount(MinimumUsableTrimmedAudioDuration);
    if (pcmBytes.Length >= minimumUsableBytes
        && trimmed.Length < minimumUsableBytes
        && ContainsSustainedSignal(pcmBytes, LowSignalThresholdPcm16))
    {
      return pcmBytes;
    }

    return trimmed;
  }

  private int CalculateByteCount(TimeSpan duration)
  {
    if (duration <= TimeSpan.Zero)
    {
      return 0;
    }

    double bytes = duration.TotalSeconds * options.TargetSampleRateHz * 2;
    return Math.Max(2, (int)Math.Ceiling(bytes / 2d) * 2);
  }

  private static TrailingSilenceAnalysis AnalyzeTrailingSilence(byte[] pcmBytes, short threshold)
  {
    if (pcmBytes.Length < 2)
    {
      return new TrailingSilenceAnalysis(AllSilent: true, TrailingSilentBytes: 0);
    }

    int trailingBytes = 0;
    bool allSilent = true;
    for (int offset = pcmBytes.Length - 2; offset >= 0; offset -= 2)
    {
      short sample = BitConverter.ToInt16(pcmBytes, offset);
      if (Math.Abs((int)sample) <= threshold)
      {
        trailingBytes += 2;
        continue;
      }

      allSilent = false;
      break;
    }

    return new TrailingSilenceAnalysis(allSilent, trailingBytes);
  }

  private static bool ContainsSustainedSignal(byte[] pcmBytes, short threshold)
  {
    int consecutive = 0;
    for (int offset = 0; offset + 1 < pcmBytes.Length; offset += 2)
    {
      short sample = BitConverter.ToInt16(pcmBytes, offset);
      if (Math.Abs((int)sample) > threshold)
      {
        consecutive++;
        if (consecutive >= RequiredConsecutiveSignalSamples)
        {
          return true;
        }

        continue;
      }

      consecutive = 0;
    }

    return false;
  }

  private sealed record StopCaptureSnapshot(
    AudioCaptureResult FullAudio,
    AudioCaptureResult RemainingAudio,
    int FinalSequenceNumber);

  private sealed record TrailingSilenceAnalysis(bool AllSilent, int TrailingSilentBytes);
}
