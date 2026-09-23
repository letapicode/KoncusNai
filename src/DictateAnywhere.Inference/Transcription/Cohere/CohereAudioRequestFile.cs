using System;
using System.Diagnostics;
using System.IO;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

internal sealed class CohereAudioRequestFile : IDisposable
{
  private CohereAudioRequestFile(
    string filePath,
    long wavFileBytes,
    TimeSpan preprocessingDuration)
  {
    FilePath = filePath;
    WavFileBytes = wavFileBytes;
    PreprocessingDuration = preprocessingDuration;
  }

  public string FilePath { get; }

  public long WavFileBytes { get; }

  public TimeSpan PreprocessingDuration { get; }

  public static CohereAudioRequestFile Create(
    AudioCaptureResult audio,
    string tempDirectory)
  {
    ArgumentNullException.ThrowIfNull(audio);
    Directory.CreateDirectory(tempDirectory);

    string audioPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.wav");
    Stopwatch stopwatch = Stopwatch.StartNew();
    Pcm16WaveFileWriter.WriteMonoPcm16(audioPath, audio.Pcm16Mono, audio.SampleRateHz);
    stopwatch.Stop();

    return new CohereAudioRequestFile(
      audioPath,
      new FileInfo(audioPath).Length,
      stopwatch.Elapsed);
  }

  public void Dispose()
  {
    if (!File.Exists(FilePath))
    {
      return;
    }

    try
    {
      File.Delete(FilePath);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }
}
