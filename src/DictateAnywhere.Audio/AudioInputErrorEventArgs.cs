using System;

namespace DictateAnywhere.Audio;

public sealed class AudioInputErrorEventArgs : EventArgs
{
  public AudioInputErrorEventArgs(Exception exception)
  {
    Exception = exception ?? throw new ArgumentNullException(nameof(exception));
  }

  public Exception Exception { get; }
}
