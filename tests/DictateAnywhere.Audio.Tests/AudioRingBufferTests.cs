using DictateAnywhere.Audio;

namespace DictateAnywhere.Audio.Tests;

public sealed class AudioRingBufferTests
{
  [Xunit.Fact]
  public void WriteAndSnapshot_PreservesData_WhenWithinCapacity()
  {
    AudioRingBuffer buffer = new(capacityBytes: 8);
    buffer.Write([1, 2, 3, 4]);

    byte[] snapshot = buffer.Snapshot();

    Xunit.Assert.Equal(new byte[] { 1, 2, 3, 4 }, snapshot);
  }

  [Xunit.Fact]
  public void WriteAndSnapshot_KeepsMostRecentBytes_WhenOverflowing()
  {
    AudioRingBuffer buffer = new(capacityBytes: 4);
    buffer.Write([1, 2, 3, 4, 5, 6]);

    byte[] snapshot = buffer.Snapshot();

    Xunit.Assert.Equal(new byte[] { 3, 4, 5, 6 }, snapshot);
  }
}
