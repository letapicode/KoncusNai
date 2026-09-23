using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Workbench;

internal sealed class WorkbenchTranscriptionServiceAdapter : ITranscriptionService, IAsyncDisposable
{
  private readonly ITranscriptionService inner;

  public WorkbenchTranscriptionServiceAdapter(ITranscriptionService inner)
  {
    this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
  }

  public async Task<TranscriptionResult> TranscribeAsync(
    AudioCaptureResult audio,
    string modelId,
    CancellationToken cancellationToken = default)
  {
    try
    {
      return await inner
        .TranscribeAsync(audio, modelId, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (InferenceException ex)
    {
      throw new InvalidOperationException(ex.Message, ex);
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (inner is IAsyncDisposable disposable)
    {
      await disposable.DisposeAsync().ConfigureAwait(false);
    }
  }
}
