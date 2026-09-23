using System;

namespace DictateAnywhere.Audio;

public sealed class AudioCaptureException : InvalidOperationException
{
  public AudioCaptureException(string message)
    : base(message)
  {
  }

  public AudioCaptureException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}
