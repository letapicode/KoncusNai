using System;

namespace DictateAnywhere.Core.Contracts;

public sealed class AudioCaptureChunkAvailableEventArgs : EventArgs
{
  public AudioCaptureChunkAvailableEventArgs(AudioCaptureChunk chunk)
  {
    Chunk = chunk ?? throw new ArgumentNullException(nameof(chunk));
  }

  public AudioCaptureChunk Chunk { get; }
}
