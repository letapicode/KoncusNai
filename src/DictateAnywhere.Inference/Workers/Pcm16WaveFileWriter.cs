using System;
using System.IO;
using System.Text;

namespace DictateAnywhere.Inference;

public static class Pcm16WaveFileWriter
{
  public static void WriteMonoPcm16(string path, byte[] pcmData, int sampleRateHz)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new ArgumentException("Path must not be empty.", nameof(path));
    }

    ArgumentNullException.ThrowIfNull(pcmData);

    if (sampleRateHz <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be greater than zero.");
    }

    const short channels = 1;
    const short bitsPerSample = 16;
    short blockAlign = (short)(channels * (bitsPerSample / 8));
    int byteRate = sampleRateHz * blockAlign;

    using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
    using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: false);

    writer.Write(Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + pcmData.Length);
    writer.Write(Encoding.ASCII.GetBytes("WAVE"));
    writer.Write(Encoding.ASCII.GetBytes("fmt "));
    writer.Write(16);
    writer.Write((short)1); // PCM format
    writer.Write(channels);
    writer.Write(sampleRateHz);
    writer.Write(byteRate);
    writer.Write(blockAlign);
    writer.Write(bitsPerSample);
    writer.Write(Encoding.ASCII.GetBytes("data"));
    writer.Write(pcmData.Length);
    writer.Write(pcmData);
  }
}
