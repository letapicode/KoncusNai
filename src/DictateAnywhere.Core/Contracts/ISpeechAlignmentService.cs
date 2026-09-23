using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

/// <summary>Produces a time map for known text against a completed local audio file.</summary>
public interface ISpeechAlignmentService : IAsyncDisposable
{
  Task<SpeechAlignmentResult> AlignAsync(SpeechAlignmentRequest request, CancellationToken cancellationToken = default);
}

public sealed record SpeechAlignmentRequest(string AudioPath, string Transcript, string Language = "en")
{
  public SpeechAlignmentRequest Normalize() => new(
    AudioPath?.Trim() ?? string.Empty,
    Transcript?.Trim() ?? string.Empty,
    string.IsNullOrWhiteSpace(Language) ? "en" : Language.Trim().ToLowerInvariant());
}

public sealed record SpeechWordTiming(string Text, TimeSpan Start, TimeSpan End);

public sealed record SpeechAlignmentResult(IReadOnlyList<SpeechWordTiming> Words, string ProviderId, TimeSpan ProcessingTime);
