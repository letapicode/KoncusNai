using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IModelManager
{
  Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);

  Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default);

  Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default);

  Task DownloadModelAsync(
    TranscriptionModelSelection selection,
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default);

  Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default);
}
