using System;

namespace DictateAnywhere.Audio;

public sealed class AudioRingBuffer
{
  private readonly byte[] buffer;
  private int writeIndex;
  private int length;

  public AudioRingBuffer(int capacityBytes)
  {
    if (capacityBytes <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(capacityBytes), "Capacity must be greater than zero.");
    }

    buffer = new byte[capacityBytes];
  }

  public int CapacityBytes => buffer.Length;
  public int LengthBytes => length;

  public void Clear()
  {
    writeIndex = 0;
    length = 0;
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    if (data.Length == 0)
    {
      return;
    }

    for (int i = 0; i < data.Length; i++)
    {
      buffer[writeIndex] = data[i];
      writeIndex = (writeIndex + 1) % buffer.Length;
      if (length < buffer.Length)
      {
        length++;
      }
    }
  }

  public byte[] Snapshot()
  {
    byte[] snapshot = new byte[length];
    if (length == 0)
    {
      return snapshot;
    }

    int startIndex = (writeIndex - length + buffer.Length) % buffer.Length;
    if (startIndex + length <= buffer.Length)
    {
      Array.Copy(buffer, startIndex, snapshot, 0, length);
      return snapshot;
    }

    int firstSegmentLength = buffer.Length - startIndex;
    Array.Copy(buffer, startIndex, snapshot, 0, firstSegmentLength);
    Array.Copy(buffer, 0, snapshot, firstSegmentLength, length - firstSegmentLength);
    return snapshot;
  }
}
