using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Audio;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed class WorkbenchAudioCaptureServiceAdapter : IAudioCaptureService, IAsyncDisposable
{
  private readonly IAudioCaptureService inner;

  public WorkbenchAudioCaptureServiceAdapter(IAudioCaptureService inner)
  {
    this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
  }

  public bool IsCapturing => inner.IsCapturing;

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      await inner.StartAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (AudioCaptureException ex)
    {
      throw new InvalidOperationException(ex.Message, ex);
    }
  }

  public async Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await inner.StopAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (AudioCaptureException ex)
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
