using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Inference;

public interface ITranscriptionModelWarmup
{
  Task WarmUpAsync(string modelId, CancellationToken cancellationToken = default);
}
