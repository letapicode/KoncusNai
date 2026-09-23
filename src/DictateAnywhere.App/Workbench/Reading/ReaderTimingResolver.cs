using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Resolves the best complete display-token timeline available for synthesized speech.</summary>
internal sealed class ReaderTimingResolver : IAsyncDisposable
{
  private readonly ISpeechAlignmentService alignmentService;

  public ReaderTimingResolver(ISpeechAlignmentService alignmentService)
  {
    this.alignmentService = alignmentService ?? throw new ArgumentNullException(nameof(alignmentService));
  }

  public async Task<ReaderWordTimingMap> ResolveAsync(
    ReadingSection section,
    string narrationText,
    TextToSpeechResult speech,
    string languageCode,
    ReaderWordTimingStrategy strategy,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(section);
    ArgumentException.ThrowIfNullOrWhiteSpace(narrationText);
    ArgumentNullException.ThrowIfNull(speech);
    ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);

    if (strategy == ReaderWordTimingStrategy.NativeWithDeterministicFallback
        && speech.WordTimings is { Count: > 0 }
        && ReaderWordTimingMap.TryCreate(
          section.Words,
          speech.WordTimings,
          speech.Duration,
          ReaderWordTimingSource.Native,
          out ReaderWordTimingMap? nativeTimingMap)
        && nativeTimingMap is not null)
    {
      return nativeTimingMap;
    }

    if (strategy == ReaderWordTimingStrategy.NativeWithDeterministicFallback)
    {
      if (ReaderWordTimingMap.TryCreate(
          section.Words,
          CreateDeterministicWordTimings(section.Words, speech.Duration),
          speech.Duration,
          ReaderWordTimingSource.DeterministicEstimate,
          out ReaderWordTimingMap? estimatedTimingMap)
        && estimatedTimingMap is not null)
      {
        return estimatedTimingMap;
      }

      throw CreateIncompleteTimingException();
    }

    if (strategy != ReaderWordTimingStrategy.LocalForcedAlignment)
    {
      throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown reader word-timing strategy.");
    }

    SpeechAlignmentResult alignment = await alignmentService.AlignAsync(
      new SpeechAlignmentRequest(speech.AudioPath, narrationText, languageCode),
      cancellationToken).ConfigureAwait(false);
    if (!ReaderWordTimingMap.TryCreate(
        section.Words,
        alignment.Words,
        speech.Duration,
        ReaderWordTimingSource.ForcedAlignment,
        out ReaderWordTimingMap? alignedTimingMap)
      || alignedTimingMap is null)
    {
      throw CreateIncompleteTimingException();
    }

    return alignedTimingMap;
  }

  internal static IReadOnlyList<SpeechWordTiming> CreateDeterministicWordTimings(
    IReadOnlyList<string> words,
    TimeSpan duration)
  {
    ArgumentNullException.ThrowIfNull(words);

    List<SpeechWordTiming> timings = new(words.Count);
    for (int index = 0; index < words.Count; index++)
    {
      timings.Add(new SpeechWordTiming(
        words[index],
        ReadingPlaybackTiming.GetPositionForWordIndex(duration, words, index),
        ReadingPlaybackTiming.GetPositionForWordIndex(duration, words, index + 1)));
    }

    return timings;
  }

  public ValueTask DisposeAsync() => alignmentService.DisposeAsync();

  private static InvalidOperationException CreateIncompleteTimingException() => new(
    "The audio could not be matched to every displayed word. Please prepare the section again.");
}
