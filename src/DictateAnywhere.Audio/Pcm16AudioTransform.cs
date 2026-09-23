using System;
using System.Buffers.Binary;

namespace DictateAnywhere.Audio;

public static class Pcm16AudioTransform
{
  public static byte[] ConvertTo16KhzMonoPcm16(
    ReadOnlySpan<byte> sourceBytes,
    int sourceSampleRateHz,
    int sourceChannels,
    int sourceBitsPerSample,
    bool sourceFormatIsFloat,
    int targetSampleRateHz,
    float gainMultiplier)
  {
    if (sourceChannels <= 0)
    {
      throw new AudioCaptureException("Audio channel count must be greater than zero.");
    }

    if (sourceSampleRateHz <= 0 || targetSampleRateHz <= 0)
    {
      throw new AudioCaptureException("Sample rates must be greater than zero.");
    }

    float[] sourceSamples = ConvertSourceToPcmScale(sourceBytes, sourceBitsPerSample, sourceFormatIsFloat);
    int totalSamples = sourceSamples.Length;

    int monoSamplesCount = totalSamples / sourceChannels;
    if (monoSamplesCount == 0)
    {
      return Array.Empty<byte>();
    }

    float[] mono = new float[monoSamplesCount];
    int sampleIndex = 0;
    for (int frame = 0; frame < monoSamplesCount; frame++)
    {
      int frameOffset = frame * sourceChannels;
      float mixed = 0;
      for (int channel = 0; channel < sourceChannels; channel++)
      {
        mixed += sourceSamples[frameOffset + channel];
      }

      mono[sampleIndex++] = (mixed / (float)sourceChannels) * gainMultiplier;
    }

    float[] resampled = ResampleLinear(mono, sourceSampleRateHz, targetSampleRateHz);
    byte[] output = new byte[resampled.Length * 2];
    for (int i = 0; i < resampled.Length; i++)
    {
      int clamped = (int)Math.Round(resampled[i]);
      if (clamped > short.MaxValue)
      {
        clamped = short.MaxValue;
      }
      else if (clamped < short.MinValue)
      {
        clamped = short.MinValue;
      }

      short value = (short)clamped;
      output[i * 2] = (byte)(value & 0xFF);
      output[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
    }

    return output;
  }

  private static float[] ConvertSourceToPcmScale(ReadOnlySpan<byte> sourceBytes, int sourceBitsPerSample, bool sourceFormatIsFloat)
  {
    if (sourceBitsPerSample == 16)
    {
      int sampleCount = sourceBytes.Length / 2;
      if (sampleCount == 0)
      {
        return Array.Empty<float>();
      }

      float[] samples = new float[sampleCount];
      for (int i = 0; i < sampleCount; i++)
      {
        short value = BinaryPrimitives.ReadInt16LittleEndian(sourceBytes.Slice(i * 2, 2));
        samples[i] = value;
      }

      return samples;
    }

    if (sourceBitsPerSample == 32)
    {
      int sampleCount = sourceBytes.Length / 4;
      if (sampleCount == 0)
      {
        return Array.Empty<float>();
      }

      float[] samples = new float[sampleCount];
      for (int i = 0; i < sampleCount; i++)
      {
        ReadOnlySpan<byte> sampleBytes = sourceBytes.Slice(i * 4, 4);
        if (sourceFormatIsFloat)
        {
          int raw = BinaryPrimitives.ReadInt32LittleEndian(sampleBytes);
          float normalized = Math.Clamp(BitConverter.Int32BitsToSingle(raw), -1.0f, 1.0f);
          samples[i] = normalized * short.MaxValue;
        }
        else
        {
          int raw = BinaryPrimitives.ReadInt32LittleEndian(sampleBytes);
          float pcmScale = raw / 65536f;
          samples[i] = Math.Clamp(pcmScale, short.MinValue, short.MaxValue);
        }
      }

      return samples;
    }

    throw new AudioCaptureException(
      $"Unsupported bit depth: {sourceBitsPerSample}. Expected 16-bit PCM or 32-bit float/PCM.");
  }

  public static byte[] TrimSilenceEdgesPcm16(ReadOnlySpan<byte> sourceBytes, short threshold)
  {
    int sampleCount = sourceBytes.Length / 2;
    if (sampleCount == 0)
    {
      return Array.Empty<byte>();
    }

    short[] samples = new short[sampleCount];
    Buffer.BlockCopy(sourceBytes.ToArray(), 0, samples, 0, sourceBytes.Length);

    short effectiveThreshold = CalculateAdaptiveThreshold(samples, threshold);

    int start = FindFirstSignalSample(samples, effectiveThreshold, searchForward: true);
    if (start < 0)
    {
      return Array.Empty<byte>();
    }

    int end = FindFirstSignalSample(samples, effectiveThreshold, searchForward: false);
    if (end < start)
    {
      return Array.Empty<byte>();
    }

    int trimmedSampleCount = end - start + 1;
    byte[] trimmed = new byte[trimmedSampleCount * 2];
    Buffer.BlockCopy(samples, start * 2, trimmed, 0, trimmed.Length);
    return trimmed;
  }

  private static short CalculateAdaptiveThreshold(short[] samples, short baseThreshold)
  {
    int edgeWindow = Math.Min(samples.Length, 1_600);
    if (edgeWindow == 0)
    {
      return baseThreshold;
    }

    long absoluteSum = 0;
    int count = 0;
    int peak = 0;

    for (int i = 0; i < edgeWindow; i++)
    {
      int absolute = Math.Abs((int)samples[i]);
      peak = Math.Max(peak, absolute);
      absoluteSum += absolute;
      count++;
    }

    for (int i = samples.Length - edgeWindow; i < samples.Length; i++)
    {
      if (i < 0 || i >= samples.Length)
      {
        continue;
      }

      int absolute = Math.Abs((int)samples[i]);
      peak = Math.Max(peak, absolute);
      absoluteSum += absolute;
      count++;
    }

    if (count == 0)
    {
      return baseThreshold;
    }

    int average = (int)(absoluteSum / count);
    int adaptiveThreshold = average * 3;

    int effective = Math.Max(baseThreshold, adaptiveThreshold);
    if (peak > 0 && effective >= peak)
    {
      effective = Math.Min(baseThreshold, peak - 1);
    }

    return (short)Math.Clamp(effective, 0, short.MaxValue);
  }

  private static int FindFirstSignalSample(short[] samples, short threshold, bool searchForward)
  {
    const int requiredConsecutiveSamples = 2;

    int index = searchForward ? 0 : samples.Length - 1;
    int step = searchForward ? 1 : -1;

    while (index >= 0 && index < samples.Length)
    {
      if (Math.Abs((int)samples[index]) > threshold)
      {
        bool hasRun = true;
        for (int offset = 1; offset < requiredConsecutiveSamples; offset++)
        {
          int nextIndex = index + (offset * step);
          if (nextIndex < 0 || nextIndex >= samples.Length)
          {
            break;
          }

          if (Math.Abs((int)samples[nextIndex]) <= threshold)
          {
            hasRun = false;
            break;
          }
        }

        if (hasRun)
        {
          return index;
        }
      }

      index += step;
    }

    return -1;
  }

  private static float[] ResampleLinear(float[] source, int sourceRate, int targetRate)
  {
    if (sourceRate == targetRate)
    {
      float[] copy = new float[source.Length];
      Array.Copy(source, copy, source.Length);
      return copy;
    }

    int targetLength = (int)Math.Max(1, Math.Round(source.Length * (targetRate / (double)sourceRate)));
    float[] output = new float[targetLength];
    double step = (source.Length - 1d) / Math.Max(1, targetLength - 1d);

    for (int i = 0; i < targetLength; i++)
    {
      double position = i * step;
      int left = (int)Math.Floor(position);
      int right = Math.Min(left + 1, source.Length - 1);
      double fraction = position - left;
      output[i] = (float)((source[left] * (1 - fraction)) + (source[right] * fraction));
    }

    return output;
  }
}
