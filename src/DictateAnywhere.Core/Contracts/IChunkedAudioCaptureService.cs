using System;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IChunkedAudioCaptureService : IAudioCaptureService
{
  event EventHandler<AudioCaptureChunkAvailableEventArgs>? ChunkAvailable;

  Task StartChunkedAsync(CancellationToken cancellationToken = default) => StartAsync(cancellationToken);

  Task<AudioCaptureChunk?> StopAndFlushChunkAsync(CancellationToken cancellationToken = default);
}
