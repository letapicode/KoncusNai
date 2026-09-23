using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IAudioCaptureService
{
  bool IsCapturing { get; }

  Task StartAsync(CancellationToken cancellationToken = default);

  Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default);
}
