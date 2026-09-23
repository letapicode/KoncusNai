using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

internal sealed class UnavailableLocalTranscriptionService : ITranscriptionService
{
  private readonly string providerId;
  private readonly string reason;
  private readonly IReadOnlyList<string> requiredResources;

  public UnavailableLocalTranscriptionService(
    string providerId,
    string reason,
    IReadOnlyList<string> requiredResources)
  {
    this.providerId = providerId ?? throw new ArgumentNullException(nameof(providerId));
    this.reason = reason ?? throw new ArgumentNullException(nameof(reason));
    this.requiredResources = requiredResources ?? throw new ArgumentNullException(nameof(requiredResources));
  }

  public Task<TranscriptionResult> TranscribeAsync(
    AudioCaptureResult audio,
    string modelId,
    CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    string resources = requiredResources.Count == 0
      ? "no additional resource details were provided"
      : string.Join(", ", requiredResources);

    throw new InvalidOperationException(
      $"Local speech-to-text provider '{providerId}' is configured but unavailable in this build. {reason} Required resources: {resources}.");
  }
}
