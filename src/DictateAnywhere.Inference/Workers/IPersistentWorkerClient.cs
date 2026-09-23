using System;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Inference;

internal interface IPersistentWorkerClient : IAsyncDisposable
{
  Task StartAsync(CancellationToken cancellationToken = default);

  Task<TResponse> InvokeAsync<TResponse>(
    object request,
    TimeSpan requestTimeout,
    CancellationToken cancellationToken = default);
}
