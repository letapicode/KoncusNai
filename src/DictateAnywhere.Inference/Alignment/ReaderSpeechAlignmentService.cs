using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>Routes reader audio to the alignment engine trained for its language.</summary>
public sealed class ReaderSpeechAlignmentService : ISpeechAlignmentService
{
  private readonly ISpeechAlignmentService generalAlignment;
  private readonly ISpeechAlignmentService hindiAlignment;
  private bool disposed;

  public ReaderSpeechAlignmentService(
    ISpeechAlignmentService? generalAlignment = null,
    ISpeechAlignmentService? hindiAlignment = null)
  {
    this.generalAlignment = generalAlignment ?? new CrisperWhisperForcedAlignmentService();
    this.hindiAlignment = hindiAlignment ?? new HindiCtcForcedAlignmentService();
  }

  public Task<SpeechAlignmentResult> AlignAsync(SpeechAlignmentRequest request, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(request);
    return request.Normalize().Language.Split('-', 2)[0].Equals("hi", StringComparison.OrdinalIgnoreCase)
      ? hindiAlignment.AlignAsync(request, cancellationToken)
      : generalAlignment.AlignAsync(request, cancellationToken);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    await hindiAlignment.DisposeAsync().ConfigureAwait(false);
    await generalAlignment.DisposeAsync().ConfigureAwait(false);
  }
}
