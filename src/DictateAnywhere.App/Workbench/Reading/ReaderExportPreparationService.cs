using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Prepares ordered narration artifacts for audio and video export workflows.</summary>
internal sealed class ReaderExportPreparationService
{
  private readonly ReaderNarrationSession narrationSession;

  public ReaderExportPreparationService(ReaderNarrationSession narrationSession)
  {
    this.narrationSession = narrationSession ?? throw new ArgumentNullException(nameof(narrationSession));
  }

  public async Task<IReadOnlyList<TextToSpeechResult>> PrepareAudioAsync(
    IReadOnlyList<ReadingSection> sections,
    ReaderNarrationProfile profile,
    IProgress<ReaderExportPreparationProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ValidateRequest(sections, profile);
    List<TextToSpeechResult> prepared = new(sections.Count);
    for (int index = 0; index < sections.Count; index++)
    {
      ReadingSection section = sections[index];
      TextToSpeechResult speech = await GetSpeechAsync(
        section,
        profile,
        index,
        sections.Count,
        progress,
        cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      prepared.Add(speech);
      progress?.Report(new ReaderExportPreparationProgress(
        index,
        sections.Count,
        ReaderExportPreparationPhase.Complete,
        section,
        speech,
        IsCached: false));
    }

    return prepared;
  }

  public async Task<IReadOnlyList<ReaderVideoExportSection>> PrepareVideoAsync(
    IReadOnlyList<ReadingSection> sections,
    ReaderNarrationProfile profile,
    ReaderTextDirection textDirection,
    IProgress<ReaderExportPreparationProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ValidateRequest(sections, profile);
    List<ReaderVideoExportSection> prepared = new(sections.Count);
    for (int index = 0; index < sections.Count; index++)
    {
      prepared.Add(await PrepareVideoSectionCoreAsync(
        sections[index],
        profile,
        textDirection,
        index,
        sections.Count,
        progress,
        cancellationToken).ConfigureAwait(false));
    }

    return prepared;
  }

  public Task<ReaderVideoExportSection> PrepareVideoSectionAsync(
    ReadingSection section,
    ReaderNarrationProfile profile,
    ReaderTextDirection textDirection,
    IProgress<ReaderExportPreparationProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(section);
    ArgumentNullException.ThrowIfNull(profile);
    return PrepareVideoSectionCoreAsync(
      section,
      profile,
      textDirection,
      sectionIndex: 0,
      sectionCount: 1,
      progress,
      cancellationToken);
  }

  private async Task<ReaderVideoExportSection> PrepareVideoSectionCoreAsync(
    ReadingSection section,
    ReaderNarrationProfile profile,
    ReaderTextDirection textDirection,
    int sectionIndex,
    int sectionCount,
    IProgress<ReaderExportPreparationProgress>? progress,
    CancellationToken cancellationToken)
  {
    TextToSpeechResult speech = await GetSpeechAsync(
      section,
      profile,
      sectionIndex,
      sectionCount,
      progress,
      cancellationToken).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    bool hasCachedTiming = narrationSession.TryGetTiming(section, profile, speech, out ReaderWordTimingMap? cachedTiming)
      && cachedTiming is not null;
    progress?.Report(new ReaderExportPreparationProgress(
      sectionIndex,
      sectionCount,
      ReaderExportPreparationPhase.Timing,
      section,
      speech,
      hasCachedTiming));
    ReaderWordTimingMap timing = hasCachedTiming
      ? cachedTiming!
      : await narrationSession.GetTimingAsync(section, profile, speech, cancellationToken).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    ReaderVideoExportSection prepared = new(
      section.Words,
      section.ParagraphStartWordIndices,
      timing.Words,
      speech.AudioPath,
      speech.Duration,
      section.GetParagraphDirections(textDirection));
    progress?.Report(new ReaderExportPreparationProgress(
      sectionIndex,
      sectionCount,
      ReaderExportPreparationPhase.Complete,
      section,
      speech,
      IsCached: false));
    return prepared;
  }

  private async Task<TextToSpeechResult> GetSpeechAsync(
    ReadingSection section,
    ReaderNarrationProfile profile,
    int sectionIndex,
    int sectionCount,
    IProgress<ReaderExportPreparationProgress>? progress,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    bool hasCachedSpeech = narrationSession.TryGetSpeech(section, profile, out TextToSpeechResult? cachedSpeech)
      && cachedSpeech is not null;
    progress?.Report(new ReaderExportPreparationProgress(
      sectionIndex,
      sectionCount,
      ReaderExportPreparationPhase.Narration,
      section,
      cachedSpeech,
      hasCachedSpeech));
    return hasCachedSpeech
      ? cachedSpeech!
      : await narrationSession.GetSpeechAsync(section, profile, cancellationToken).ConfigureAwait(false);
  }

  private static void ValidateRequest(
    IReadOnlyList<ReadingSection> sections,
    ReaderNarrationProfile profile)
  {
    ArgumentNullException.ThrowIfNull(sections);
    ArgumentNullException.ThrowIfNull(profile);
    if (sections.Count == 0)
    {
      throw new ArgumentException("At least one reading section is required for export preparation.", nameof(sections));
    }
  }
}

internal enum ReaderExportPreparationPhase
{
  Narration,
  Timing,
  Complete,
}

internal sealed record ReaderExportPreparationProgress(
  int SectionIndex,
  int SectionCount,
  ReaderExportPreparationPhase Phase,
  ReadingSection Section,
  TextToSpeechResult? Speech,
  bool IsCached)
{
  public int SectionNumber => SectionIndex + 1;
}
