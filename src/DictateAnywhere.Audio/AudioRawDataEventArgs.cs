using System;

namespace DictateAnywhere.Audio;

public sealed class AudioRawDataEventArgs : EventArgs
{
  public AudioRawDataEventArgs(
    byte[] buffer,
    int bytesRecorded,
    int sampleRateHz,
    int channels,
    int bitsPerSample,
    bool sourceFormatIsFloat)
  {
    Buffer = buffer;
    BytesRecorded = bytesRecorded;
    SampleRateHz = sampleRateHz;
    Channels = channels;
    BitsPerSample = bitsPerSample;
    SourceFormatIsFloat = sourceFormatIsFloat;
  }

  public byte[] Buffer { get; }
  public int BytesRecorded { get; }
  public int SampleRateHz { get; }
  public int Channels { get; }
  public int BitsPerSample { get; }
  public bool SourceFormatIsFloat { get; }
}
