using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface ITranscriptionService
{
  Task<TranscriptionResult> TranscribeAsync(
    AudioCaptureResult audio,
    string modelId,
    CancellationToken cancellationToken = default);
}
