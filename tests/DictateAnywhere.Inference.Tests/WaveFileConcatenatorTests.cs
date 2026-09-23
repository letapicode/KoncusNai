using System.Text;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class WaveFileConcatenatorTests
{
  [Xunit.Fact]
  public void Combine_StreamsNativePcmWithoutResamplingOrLoss()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-wave-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      string first = Path.Combine(directory, "first.wav");
      string second = Path.Combine(directory, "second.wav");
      string output = Path.Combine(directory, "audiobook.wav");
      short[] firstSamples = [100, 200, 300, 400];
      short[] secondSamples = [-100, -200, -300];
      WritePcm16Wave(first, firstSamples, 24_000);
      WritePcm16Wave(second, secondSamples, 24_000);

      TimeSpan duration = WaveFileConcatenator.Combine([first, second], output);

      byte[] bytes = File.ReadAllBytes(output);
      Xunit.Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
      Xunit.Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
      Xunit.Assert.Equal((firstSamples.Length + secondSamples.Length) * sizeof(short), BitConverter.ToInt32(bytes, 40));
      double expectedDuration = (firstSamples.Length + secondSamples.Length) / 24_000d;
      Xunit.Assert.InRange(duration.TotalSeconds, expectedDuration - 0.0000001d, expectedDuration + 0.0000001d);
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private static void WritePcm16Wave(string path, IReadOnlyList<short> samples, int sampleRate)
  {
    using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
    using BinaryWriter writer = new(stream);
    int dataLength = samples.Count * sizeof(short);
    writer.Write("RIFF"u8.ToArray());
    writer.Write(36 + dataLength);
    writer.Write("WAVEfmt "u8.ToArray());
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(sampleRate);
    writer.Write(sampleRate * sizeof(short));
    writer.Write((short)sizeof(short));
    writer.Write((short)16);
    writer.Write("data"u8.ToArray());
    writer.Write(dataLength);
    foreach (short sample in samples)
    {
      writer.Write(sample);
    }
  }
}
