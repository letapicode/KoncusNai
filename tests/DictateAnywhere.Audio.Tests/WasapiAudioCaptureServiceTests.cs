using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Audio;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Audio.Tests;

public sealed class WasapiAudioCaptureServiceTests
{
  [Xunit.Fact]
  public async Task StreamingCapture_DoesNotRetainTheFullRecording()
  {
    await using FakeAudioInputSource source = new();
    await using WasapiAudioCaptureService service = new(source, CreateShortChunkOptions());
    long emittedBytes = 0;
    service.ChunkAvailable += (_, e) => emittedBytes += e.Chunk.Audio.Pcm16Mono.Length;
    await service.StartChunkedAsync();
    for (int i = 0; i < 1_000; i++)
    {
      source.EmitPcm16Mono(CreateSamples(1_000, 320), 16_000);
      source.EmitPcm16Mono(CreateSamples(0, 160), 16_000);
    }
    AudioCaptureChunk? final = await service.StopAndFlushChunkAsync();
    Xunit.Assert.Null(final);
    Xunit.Assert.Equal(960_000, emittedBytes);
    Xunit.Assert.Equal(960_000, service.LastMetrics.BytesCaptured);
    var field = typeof(WasapiAudioCaptureService).GetField("sessionBuffer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    Xunit.Assert.Equal(0, ((System.IO.MemoryStream)field.GetValue(service)!).Capacity);
  }

  [Xunit.Fact]
  public async Task ClippedNegativeAudio_SurvivesChunkAnalysisAndFinalFlush()
  {
    await using FakeAudioInputSource source = new();
    await using WasapiAudioCaptureService service = new(source, CreateShortChunkOptions() with
    {
      EnableSilenceTrimHook = true,
      GainMultiplier = 2f,
    });
    List<AudioCaptureChunk> chunks = [];
    service.ChunkAvailable += (_, e) => chunks.Add(e.Chunk);
    await service.StartAsync();
    source.EmitPcm16Mono(CreateSamples(-20_000, 320), 16_000);
    source.EmitPcm16Mono(CreateSamples(0, 160), 16_000);
    source.EmitPcm16Mono(CreateSamples(short.MinValue, 320), 16_000);
    AudioCaptureChunk? final = await service.StopAndFlushChunkAsync();
    Xunit.Assert.Single(chunks);
    Xunit.Assert.NotNull(final);
    Xunit.Assert.Equal(640, final!.Audio.Pcm16Mono.Length);
    Xunit.Assert.Equal(short.MinValue, BitConverter.ToInt16(final.Audio.Pcm16Mono, 0));
  }

  [Xunit.Fact]
  public async Task StopAsync_ReturnsCapturedAudioAndMetrics()
  {
    await using FakeAudioInputSource source = new();
    AudioCaptureOptions options = AudioCaptureOptions.Default with
    {
      TargetSampleRateHz = 16_000,
      EnableSilenceTrimHook = false,
    };

    await using WasapiAudioCaptureService service = new(source, options);
    await service.StartAsync();

    source.EmitPcm16Mono(new short[] { 100, -100, 200, -200 }, sampleRateHz: 16_000);

    AudioCaptureResult result = await service.StopAsync();
    AudioCaptureMetrics metrics = service.LastMetrics;

    Xunit.Assert.Equal(8, result.Pcm16Mono.Length);
    Xunit.Assert.True(result.Duration > TimeSpan.Zero);
    Xunit.Assert.True(metrics.StartLatency >= TimeSpan.Zero);
    Xunit.Assert.True(metrics.StopLatency >= TimeSpan.Zero);
    Xunit.Assert.Equal(result.Pcm16Mono.Length, metrics.BytesCaptured);
  }

  [Xunit.Fact]
  public async Task StopAsync_DoesNotTrimContinuousSpeechLikeAudioToEmpty()
  {
    await using FakeAudioInputSource source = new();
    AudioCaptureOptions options = AudioCaptureOptions.Default with
    {
      TargetSampleRateHz = 16_000,
      EnableSilenceTrimHook = true,
      SilenceThresholdPcm16 = 350,
    };

    await using WasapiAudioCaptureService service = new(source, options);
    await service.StartAsync();

    source.EmitPcm16Mono(CreateSamples(value: 420, count: 320), sampleRateHz: 16_000);

    AudioCaptureResult result = await service.StopAsync();

    Xunit.Assert.Equal(640, result.Pcm16Mono.Length);
    Xunit.Assert.True(result.Duration > TimeSpan.Zero);
  }

  [Xunit.Fact]
  public async Task StopAsync_FallsBackToRawAudioWhenTrimmingLeavesTinyFragment()
  {
    await using FakeAudioInputSource source = new();
    AudioCaptureOptions options = AudioCaptureOptions.Default with
    {
      TargetSampleRateHz = 16_000,
      EnableSilenceTrimHook = true,
      SilenceThresholdPcm16 = 350,
    };

    await using WasapiAudioCaptureService service = new(source, options);
    await service.StartAsync();

    source.EmitPcm16Mono(CreateSamples(value: 80, count: 8_000), sampleRateHz: 16_000);

    AudioCaptureResult result = await service.StopAsync();

    Xunit.Assert.Equal(16_000, result.Pcm16Mono.Length);
    Xunit.Assert.True(result.Duration >= TimeSpan.FromMilliseconds(490));
  }

  [Xunit.Fact]
  public async Task StopAsync_ReturnsEmptyForTrueSilence()
  {
    await using FakeAudioInputSource source = new();
    AudioCaptureOptions options = AudioCaptureOptions.Default with
    {
      TargetSampleRateHz = 16_000,
      EnableSilenceTrimHook = true,
      SilenceThresholdPcm16 = 350,
    };

    await using WasapiAudioCaptureService service = new(source, options);
    await service.StartAsync();

    source.EmitPcm16Mono(CreateSamples(value: 0, count: 8_000), sampleRateHz: 16_000);

    AudioCaptureResult result = await service.StopAsync();

    Xunit.Assert.Empty(result.Pcm16Mono);
    Xunit.Assert.Equal(TimeSpan.Zero, result.Duration);
  }

  [Xunit.Fact]
  public async Task StopAsync_ThrowsWhenDeviceErrorOccurs()
  {
    await using FakeAudioInputSource source = new();
    await using WasapiAudioCaptureService service = new(source, AudioCaptureOptions.Default);
    await service.StartAsync();

    source.EmitDeviceError(new InvalidOperationException("device disconnected"));

    AudioCaptureException exception = await Xunit.Assert.ThrowsAsync<AudioCaptureException>(() => service.StopAsync());
    Xunit.Assert.Contains("device failure", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task StartAsync_ThrowsWhenAlreadyCapturing()
  {
    await using FakeAudioInputSource source = new();
    await using WasapiAudioCaptureService service = new(source, AudioCaptureOptions.Default);
    await service.StartAsync();

    await Xunit.Assert.ThrowsAsync<AudioCaptureException>(() => service.StartAsync());
  }

  [Xunit.Fact]
  public async Task StopAsync_ThrowsWhenDataCallbackConversionFails()
  {
    await using FakeAudioInputSource source = new();
    await using WasapiAudioCaptureService service = new(source, AudioCaptureOptions.Default);
    await service.StartAsync();

    source.EmitRaw(
      bytes: new byte[3],
      sampleRateHz: 48_000,
      channels: 1,
      bitsPerSample: 24,
      sourceFormatIsFloat: false);

    AudioCaptureException exception = await Xunit.Assert.ThrowsAsync<AudioCaptureException>(() => service.StopAsync());
    Xunit.Assert.Contains("device failure", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task ChunkAvailable_FiresAfterPreferredDurationAndSilenceBoundary()
  {
    await using FakeAudioInputSource source = new();
    AudioCaptureOptions options = CreateShortChunkOptions();
    await using WasapiAudioCaptureService service = new(source, options);
    List<AudioCaptureChunk> chunks = [];
    service.ChunkAvailable += (_, e) => chunks.Add(e.Chunk);

    await service.StartAsync();
    source.EmitPcm16Mono(CreateSamples(value: 1_000, count: 320), sampleRateHz: 16_000);
    source.EmitPcm16Mono(CreateSamples(value: 0, count: 160), sampleRateHz: 16_000);

    Xunit.Assert.Single(chunks);
    Xunit.Assert.Equal(0, chunks[0].SequenceNumber);
    Xunit.Assert.False(chunks[0].IsFinal);
    Xunit.Assert.Equal((320 + 160) * 2, chunks[0].Audio.Pcm16Mono.Length);
    Xunit.Assert.True(chunks[0].Audio.Duration >= TimeSpan.FromMilliseconds(29));

    _ = await service.StopAndFlushChunkAsync();
  }

  [Xunit.Fact]
  public async Task StopAndFlushChunkAsync_ReturnsOnlyUnemittedRemainder()
  {
    await using FakeAudioInputSource source = new();
    AudioCaptureOptions options = CreateShortChunkOptions();
    await using WasapiAudioCaptureService service = new(source, options);
    List<AudioCaptureChunk> chunks = [];
    service.ChunkAvailable += (_, e) => chunks.Add(e.Chunk);

    await service.StartAsync();
    source.EmitPcm16Mono(CreateSamples(value: 1_000, count: 320), sampleRateHz: 16_000);
    source.EmitPcm16Mono(CreateSamples(value: 0, count: 160), sampleRateHz: 16_000);
    source.EmitPcm16Mono(new short[] { 300, -300, 250, -250 }, sampleRateHz: 16_000);

    AudioCaptureChunk? finalChunk = await service.StopAndFlushChunkAsync();

    Xunit.Assert.Single(chunks);
    Xunit.Assert.NotNull(finalChunk);
    Xunit.Assert.Equal(1, finalChunk!.SequenceNumber);
    Xunit.Assert.True(finalChunk.IsFinal);
    Xunit.Assert.Equal(8, finalChunk.Audio.Pcm16Mono.Length);
  }

  private static AudioCaptureOptions CreateShortChunkOptions()
  {
    return AudioCaptureOptions.Default with
    {
      TargetSampleRateHz = 16_000,
      EnableSilenceTrimHook = false,
      Chunking = AudioChunkingOptions.Default with
      {
        PreferredChunkDuration = TimeSpan.FromMilliseconds(20),
        MaxChunkDuration = TimeSpan.FromSeconds(5),
        SilenceBoundaryDuration = TimeSpan.FromMilliseconds(10),
        SilenceThresholdPcm16 = 50,
      },
    };
  }

  private static short[] CreateSamples(short value, int count)
  {
    short[] samples = new short[count];
    Array.Fill(samples, value);
    return samples;
  }

  private sealed class FakeAudioInputSource : IAudioInputSource
  {
    public event EventHandler<AudioRawDataEventArgs>? DataAvailable;
    public event EventHandler<AudioInputErrorEventArgs>? DeviceError;

    public bool Started { get; private set; }

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
      return new[]
      {
        new AudioInputDevice("default-mic", "Default Mic", true),
      };
    }

    public string? GetDefaultInputDeviceId()
    {
      return "default-mic";
    }

    public Task StartAsync(string? preferredDeviceId, CancellationToken cancellationToken = default)
    {
      Started = true;
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      Started = false;
      return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }

    public void EmitPcm16Mono(short[] samples, int sampleRateHz)
    {
      byte[] bytes = new byte[samples.Length * 2];
      Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
      EmitRaw(
        bytes,
        sampleRateHz,
        channels: 1,
        bitsPerSample: 16,
        sourceFormatIsFloat: false);
    }

    public void EmitRaw(byte[] bytes, int sampleRateHz, int channels, int bitsPerSample, bool sourceFormatIsFloat)
    {
      DataAvailable?.Invoke(this, new AudioRawDataEventArgs(
        bytes,
        bytes.Length,
        sampleRateHz,
        channels,
        bitsPerSample,
        sourceFormatIsFloat));
    }

    public void EmitDeviceError(Exception exception)
    {
      DeviceError?.Invoke(this, new AudioInputErrorEventArgs(exception));
    }
  }
}
