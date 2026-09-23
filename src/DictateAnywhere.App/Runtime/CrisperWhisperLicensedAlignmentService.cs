using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

/// <summary>
/// Prevents the Reading Studio alignment path from loading CrisperWhisper
/// weights until the current upstream non-commercial research terms have been
/// acknowledged. The inner service is not called when acknowledgement is absent.
/// </summary>
internal sealed class CrisperWhisperLicensedAlignmentService(
  ISettingsStore settingsStore,
  ISpeechAlignmentService inner) : ISpeechAlignmentService
{
  public async Task<SpeechAlignmentResult> AlignAsync(
    SpeechAlignmentRequest request,
    CancellationToken cancellationToken = default)
  {
    AppSettings settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
    if (!CrisperWhisperLicensePolicy.HasCurrentAcceptance(settings))
    {
      throw new InvalidOperationException(
        "Precise non-Hindi word timing uses the optional CrisperWhisper model under non-commercial research-only terms. " +
        "Review and accept those terms from Settings before preparing this narration.");
    }

    return await inner.AlignAsync(request, cancellationToken).ConfigureAwait(false);
  }

  public ValueTask DisposeAsync() => inner.DisposeAsync();
}
