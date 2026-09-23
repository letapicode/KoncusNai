using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DictateAnywhere.Inference;

public static class WaveFileConcatenator
{
  public static TimeSpan Combine(IReadOnlyList<string> sourcePaths, string outputPath, int silenceBetweenSegmentsMilliseconds = 0)
  {
    ArgumentNullException.ThrowIfNull(sourcePaths);
    if (sourcePaths.Count == 0)
    {
      throw new ArgumentException("At least one WAV segment is required.", nameof(sourcePaths));
    }

    if (silenceBetweenSegmentsMilliseconds is < 0 or > 5_000)
    {
      throw new ArgumentOutOfRangeException(
        nameof(silenceBetweenSegmentsMilliseconds),
        "Inter-segment silence must be between 0 and 5000 milliseconds.");
    }

    List<WaveData> waves = sourcePaths.Select(Read).ToList();
    byte[] format = waves[0].Format;
    if (waves.Any(wave => !wave.Format.SequenceEqual(format)))
    {
      throw new InvalidOperationException("The speech provider returned WAV segments with incompatible formats.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Output path has no directory."));
    uint bytesPerSecond = ResolveBytesPerSecond(format);
    ushort blockAlignment = ResolveBlockAlignment(format);
    long rawSilenceLength = bytesPerSecond * (long)silenceBetweenSegmentsMilliseconds / 1_000L;
    long silenceLength = rawSilenceLength - (rawSilenceLength % blockAlignment);
    long dataLength = waves.Sum(wave => wave.DataLength) + (silenceLength * Math.Max(0, waves.Count - 1));
    long riffPayloadLength = 20L + format.Length + dataLength;
    if (riffPayloadLength > uint.MaxValue)
    {
      throw new InvalidOperationException("Combined speech WAV is too large.");
    }

    using FileStream stream = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
    using BinaryWriter writer = new(stream);
    writer.Write("RIFF"u8.ToArray());
    writer.Write((uint)riffPayloadLength);
    writer.Write("WAVE"u8.ToArray());
    writer.Write("fmt "u8.ToArray());
    writer.Write((uint)format.Length);
    writer.Write(format);
    writer.Write("data"u8.ToArray());
    writer.Write((uint)dataLength);
    byte[] copyBuffer = new byte[1024 * 128];
    byte[] silenceBuffer = new byte[Math.Min(1024 * 128, checked((int)Math.Max(1, silenceLength)))];
    for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
    {
      WaveData wave = waves[waveIndex];
      using FileStream source = new(wave.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
      source.Position = wave.DataOffset;
      long remaining = wave.DataLength;
      while (remaining > 0)
      {
        int read = source.Read(copyBuffer, 0, (int)Math.Min(copyBuffer.Length, remaining));
        if (read <= 0)
        {
          throw new InvalidOperationException($"Speech output '{wave.Path}' ended before its WAV data was complete.");
        }

        writer.Write(copyBuffer, 0, read);
        remaining -= read;
      }

      if (waveIndex < waves.Count - 1)
      {
        long remainingSilence = silenceLength;
        while (remainingSilence > 0)
        {
          int count = (int)Math.Min(silenceBuffer.Length, remainingSilence);
          writer.Write(silenceBuffer, 0, count);
          remainingSilence -= count;
        }
      }
    }

    return TimeSpan.FromSeconds(dataLength / (double)bytesPerSecond);
  }

  private static WaveData Read(string path)
  {
    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    using BinaryReader reader = new(stream);
    if (new string(reader.ReadChars(4)) != "RIFF" || reader.ReadUInt32() < 36 || new string(reader.ReadChars(4)) != "WAVE")
    {
      throw new InvalidOperationException($"Speech output '{path}' is not a valid WAV file.");
    }

    byte[]? format = null;
    long dataOffset = -1;
    long dataLength = -1;
    while (stream.Position + 8 <= stream.Length)
    {
      string chunkName = new(reader.ReadChars(4));
      uint length = reader.ReadUInt32();
      if (length > stream.Length - stream.Position)
      {
        throw new InvalidOperationException($"Speech output '{path}' has an invalid WAV chunk.");
      }

      if (chunkName == "fmt ")
      {
        format = reader.ReadBytes(checked((int)length));
      }
      else if (chunkName == "data")
      {
        dataOffset = stream.Position;
        dataLength = length;
        stream.Position += length;
      }
      else
      {
        stream.Position += length;
      }

      if ((length & 1) == 1 && stream.Position < stream.Length)
      {
        stream.Position++;
      }
    }

    if (format is null || dataOffset < 0 || dataLength < 0)
    {
      throw new InvalidOperationException($"Speech output '{path}' is missing WAV audio data.");
    }

    _ = ResolveBytesPerSecond(format);
    return new WaveData(path, format, dataOffset, dataLength);
  }

  private static uint ResolveBytesPerSecond(byte[] format)
  {
    if (format.Length < 12)
    {
      throw new InvalidOperationException("Speech output has an invalid WAV format block.");
    }

    uint bytesPerSecond = BitConverter.ToUInt32(format, 8);
    return bytesPerSecond == 0
      ? throw new InvalidOperationException("Speech output has a zero WAV byte rate.")
      : bytesPerSecond;
  }

  private static ushort ResolveBlockAlignment(byte[] format)
  {
    if (format.Length < 14)
    {
      throw new InvalidOperationException("Speech output has an invalid WAV format block.");
    }

    ushort blockAlignment = BitConverter.ToUInt16(format, 12);
    return blockAlignment == 0
      ? throw new InvalidOperationException("Speech output has a zero WAV block alignment.")
      : blockAlignment;
  }

  private sealed record WaveData(string Path, byte[] Format, long DataOffset, long DataLength);
}
