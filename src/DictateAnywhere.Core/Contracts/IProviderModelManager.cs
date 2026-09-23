using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IProviderModelManager
{
  string ProviderId { get; }

  Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);

  Task<ModelInfo?> GetActiveModelAsync(CancellationToken cancellationToken = default);

  Task SetActiveModelAsync(string modelId, CancellationToken cancellationToken = default);

  Task DownloadModelAsync(string modelId, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

  Task DeleteModelAsync(string modelId, CancellationToken cancellationToken = default);
}
