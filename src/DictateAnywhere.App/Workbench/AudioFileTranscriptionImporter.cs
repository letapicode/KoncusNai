using System;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal static class AudioFileTranscriptionImporter
{
  private static readonly string[] SupportedExtensions =
  [
    ".wav",
    ".mp3",
    ".m4a",
    ".mp4",
    ".aac",
    ".wma",
    ".flac",
  ];

  public const string SupportedFileDialogFilter =
    "Audio files (*.wav;*.mp3;*.m4a;*.mp4;*.aac;*.wma;*.flac)|*.wav;*.mp3;*.m4a;*.mp4;*.aac;*.wma;*.flac|All files (*.*)|*.*";

  public static bool IsSupportedAudioFile(string filePath)
  {
    if (string.IsNullOrWhiteSpace(filePath))
    {
      return false;
    }

    string extension = Path.GetExtension(filePath);
    return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
  }

  public static AudioCaptureResult DecodeToCaptureResult(string filePath)
  {
    if (string.IsNullOrWhiteSpace(filePath))
    {
      throw new ArgumentException("Audio file path must not be empty.", nameof(filePath));
    }

    if (!File.Exists(filePath))
    {
      throw new FileNotFoundException("Audio file was not found.", filePath);
    }

    if (!IsSupportedAudioFile(filePath))
    {
      throw new NotSupportedException($"Unsupported audio file type: {Path.GetExtension(filePath)}");
    }

    using MediaFoundationReader reader = new(filePath);
    ISampleProvider sampleProvider = reader.ToSampleProvider();
    ISampleProvider mono = sampleProvider.WaveFormat.Channels == 1
      ? sampleProvider
      : new StereoToMonoSampleProvider(sampleProvider)
      {
        LeftVolume = 0.5f,
        RightVolume = 0.5f,
      };
    WdlResamplingSampleProvider resampled = new(mono, 16_000);

    using MemoryStream pcmStream = new();
    float[] sampleBuffer = new float[16_000];
    byte[] byteBuffer = new byte[sampleBuffer.Length * 2];
    int samplesRead;
    while ((samplesRead = resampled.Read(sampleBuffer, 0, sampleBuffer.Length)) > 0)
    {
      for (int index = 0; index < samplesRead; index++)
      {
        float sample = Math.Clamp(sampleBuffer[index], -1.0f, 1.0f);
        short pcm = (short)Math.Round(sample * short.MaxValue);
        byteBuffer[index * 2] = (byte)(pcm & 0xFF);
        byteBuffer[index * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
      }

      pcmStream.Write(byteBuffer, 0, samplesRead * 2);
    }

    byte[] pcmBytes = pcmStream.ToArray();
    TimeSpan duration = TimeSpan.FromSeconds(pcmBytes.Length / 2d / 16_000d);
    return new AudioCaptureResult(pcmBytes, 16_000, duration);
  }
}
