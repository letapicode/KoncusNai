using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

public interface ITranscriptionModel
{
  string ProviderId { get; }

  Task<TranscriptionResult> TranscribeAsync(
    AudioCaptureResult audio,
    string modelId,
    CancellationToken cancellationToken = default);
}
