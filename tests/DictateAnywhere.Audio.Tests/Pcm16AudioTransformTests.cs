using DictateAnywhere.Audio;

namespace DictateAnywhere.Audio.Tests;

public sealed class Pcm16AudioTransformTests
{
  [Xunit.Fact]
  public void TrimSilenceEdgesPcm16_PreservesFullNegativeSamples()
  {
    short[] samples = [short.MinValue, short.MinValue, 0, short.MaxValue, short.MinValue, short.MinValue];
    Xunit.Assert.Equal(samples, ToSamples(Pcm16AudioTransform.TrimSilenceEdgesPcm16(ToBytes(samples), 350)));
  }

  [Xunit.Fact]
  public void ConvertTo16KhzMonoPcm16_PreservesMonoWhenAlreadyTargetFormat()
  {
    short[] samples = [1000, -1000, 500, -500];
    byte[] input = ToBytes(samples);

    byte[] output = Pcm16AudioTransform.ConvertTo16KhzMonoPcm16(
      input,
      sourceSampleRateHz: 16_000,
      sourceChannels: 1,
      sourceBitsPerSample: 16,
      sourceFormatIsFloat: false,
      targetSampleRateHz: 16_000,
      gainMultiplier: 1.0f);

    Xunit.Assert.Equal(input, output);
  }

  [Xunit.Fact]
  public void ConvertTo16KhzMonoPcm16_DownmixesStereoByAveraging()
  {
    short[] stereoFrames =
    [
      1000, -1000, // frame 1 -> 0
      2000, 2000,  // frame 2 -> 2000
    ];

    byte[] output = Pcm16AudioTransform.ConvertTo16KhzMonoPcm16(
      ToBytes(stereoFrames),
      sourceSampleRateHz: 16_000,
      sourceChannels: 2,
      sourceBitsPerSample: 16,
      sourceFormatIsFloat: false,
      targetSampleRateHz: 16_000,
      gainMultiplier: 1.0f);

    short[] mono = ToSamples(output);
    Xunit.Assert.Equal(new short[] { 0, 2000 }, mono);
  }

  [Xunit.Fact]
  public void TrimSilenceEdgesPcm16_RemovesLeadingAndTrailingSilence()
  {
    short[] samples = [0, 5, 10, 200, 300, 10, 5, 0];
    byte[] trimmed = Pcm16AudioTransform.TrimSilenceEdgesPcm16(ToBytes(samples), threshold: 20);
    short[] trimmedSamples = ToSamples(trimmed);

    Xunit.Assert.Equal(new short[] { 200, 300 }, trimmedSamples);
  }

  [Xunit.Fact]
  public void TrimSilenceEdgesPcm16_UsesAdaptiveThresholdToSuppressEdgeNoise()
  {
    short[] samples = [30, 35, 40, 800, 900, 40, 35, 30];
    byte[] trimmed = Pcm16AudioTransform.TrimSilenceEdgesPcm16(ToBytes(samples), threshold: 20);

    Xunit.Assert.Equal(new short[] { 800, 900 }, ToSamples(trimmed));
  }

  [Xunit.Fact]
  public void TrimSilenceEdgesPcm16_IgnoresIsolatedSpikes()
  {
    short[] samples = [0, 0, 200, 0, 500, 600, 0, 0];
    byte[] trimmed = Pcm16AudioTransform.TrimSilenceEdgesPcm16(ToBytes(samples), threshold: 20);

    Xunit.Assert.Equal(new short[] { 500, 600 }, ToSamples(trimmed));
  }

  [Xunit.Fact]
  public void TrimSilenceEdgesPcm16_DoesNotDropContinuousSpeechLikeAudio()
  {
    short[] samples = [420, 430, 410, 440, 435, 425];
    byte[] trimmed = Pcm16AudioTransform.TrimSilenceEdgesPcm16(ToBytes(samples), threshold: 350);

    Xunit.Assert.Equal(samples, ToSamples(trimmed));
  }

  [Xunit.Fact]
  public void TrimSilenceEdgesPcm16_ReturnsEmptyForTrueSilence()
  {
    short[] samples = [0, 0, 0, 0];
    byte[] trimmed = Pcm16AudioTransform.TrimSilenceEdgesPcm16(ToBytes(samples), threshold: 350);

    Xunit.Assert.Empty(trimmed);
  }

  [Xunit.Fact]
  public void ConvertTo16KhzMonoPcm16_ConvertsFloat32Input()
  {
    float[] floatSamples =
    [
      1.0f,   // max
      -1.0f,  // min
      0.5f,   // half
      -0.5f,
    ];

    byte[] output = Pcm16AudioTransform.ConvertTo16KhzMonoPcm16(
      ToBytes(floatSamples),
      sourceSampleRateHz: 16_000,
      sourceChannels: 1,
      sourceBitsPerSample: 32,
      sourceFormatIsFloat: true,
      targetSampleRateHz: 16_000,
      gainMultiplier: 1.0f);

    short[] mono = ToSamples(output);
    Xunit.Assert.Equal(4, mono.Length);
    Xunit.Assert.Equal(short.MaxValue, mono[0]);
    Xunit.Assert.Equal(short.MinValue + 1, mono[1]);
    Xunit.Assert.True(Math.Abs(mono[2] - 16384) <= 1);
    Xunit.Assert.True(Math.Abs(mono[3] + 16384) <= 1);
  }

  private static byte[] ToBytes(short[] samples)
  {
    byte[] bytes = new byte[samples.Length * 2];
    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
    return bytes;
  }

  private static short[] ToSamples(byte[] bytes)
  {
    short[] samples = new short[bytes.Length / 2];
    Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
    return samples;
  }

  private static byte[] ToBytes(float[] samples)
  {
    byte[] bytes = new byte[samples.Length * 4];
    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
    return bytes;
  }
}
