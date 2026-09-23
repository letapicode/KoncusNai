using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Diagnostics;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>
/// Owns section/range preparation command state, arbitration, cancellation, and completion policy.
/// The operation, playback, narration, and prefetch sessions are borrowed. The media adapter is owned.
/// </summary>
internal sealed class ReaderPreparationController : IAsyncDisposable
{
  private readonly object sync = new();
  private readonly ReaderOperationSession operationSession;
  private readonly ReaderPlaybackSession playbackSession;
  private readonly ReaderNarrationSession narrationSession;
  private readonly ReaderNarrationPrefetchSession prefetchSession;
  private readonly IReaderPlaybackMedia media;
  private readonly IDiagnostics diagnostics;
  private readonly TimeProvider timeProvider;
  private ReaderOperationSession.ReaderOperation? activeOperation;
  private Task<ReaderPreparationResult>? activeTask;
  private ReaderPreparationRequest? preparedRequest;
  private ReaderPreparationState state;
  private long generation;
  private bool disposed;

  public ReaderPreparationController(
    ReaderOperationSession operationSession,
    ReaderPlaybackSession playbackSession,
    ReaderNarrationSession narrationSession,
    ReaderNarrationPrefetchSession prefetchSession,
    IReaderPlaybackMedia media,
    IDiagnostics diagnostics,
    TimeProvider? timeProvider = null)
  {
    this.operationSession = operationSession ?? throw new ArgumentNullException(nameof(operationSession));
    this.playbackSession = playbackSession ?? throw new ArgumentNullException(nameof(playbackSession));
    this.narrationSession = narrationSession ?? throw new ArgumentNullException(nameof(narrationSession));
    this.prefetchSession = prefetchSession ?? throw new ArgumentNullException(nameof(prefetchSession));
    this.media = media ?? throw new ArgumentNullException(nameof(media));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.timeProvider = timeProvider ?? TimeProvider.System;
    state = ReaderPreparationState.Idle(this.timeProvider.GetUtcNow());
  }

  public event EventHandler<ReaderPreparationState>? StateChanged;

  public ReaderPreparationState State
  {
    get
    {
      lock (sync)
      {
        return state;
      }
    }
  }

  public Task<ReaderPreparationResult> PrepareAsync(ReaderPreparationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ReaderOperationSession.ReaderOperation? operation;
    long requestGeneration;
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (activeTask is { IsCompleted: false })
      {
        return Task.FromResult(BusyResult());
      }

      operation = operationSession.TryBegin(request.Kind == ReaderPreparationKind.CurrentSection
        ? ReaderOperationKind.SectionPreparation
        : ReaderOperationKind.RangePreparation);
      if (operation is null)
      {
        return Task.FromResult(BusyResult());
      }

      requestGeneration = ++generation;
      activeOperation = operation;
      activeTask = ExecuteAndReleaseAsync(request, requestGeneration, operation);
      return activeTask;
    }
  }

  public void Cancel()
  {
    ReaderOperationSession.ReaderOperation? operation;
    lock (sync)
    {
      generation++;
      operation = activeOperation;
      state = ReaderPreparationState.Idle(timeProvider.GetUtcNow());
    }

    operation?.Cancel();
  }

  public void Invalidate(bool clearNarrationCache)
  {
    Cancel();
    prefetchSession.Cancel();
    media.Stop();
    playbackSession.InvalidatePreparation();
    if (clearNarrationCache)
    {
      narrationSession.Clear();
    }

    lock (sync)
    {
      preparedRequest = null;
    }
  }

  public bool BeginNextSectionPrefetch()
  {
    ReaderPreparationRequest? request;
    lock (sync)
    {
      if (disposed)
      {
        return false;
      }

      request = preparedRequest;
    }

    if (request is null)
    {
      return false;
    }

    int nextOffset = request.CurrentSectionOffset + 1;
    return nextOffset < request.SelectedSections.Count
      && prefetchSession.Begin(request.SelectedSections[nextOffset], request.NarrationProfile);
  }

  public async ValueTask DisposeAsync()
  {
    Task<ReaderPreparationResult>? task;
    lock (sync)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      generation++;
      activeOperation?.Cancel();
      task = activeTask;
    }

    prefetchSession.Cancel();
    if (task is not null)
    {
      try
      {
        _ = await task;
      }
      catch (OperationCanceledException)
      {
      }
    }

    media.Dispose();
  }

  private async Task<ReaderPreparationResult> ExecuteAndReleaseAsync(
    ReaderPreparationRequest request,
    long requestGeneration,
    ReaderOperationSession.ReaderOperation operation)
  {
    try
    {
      return await ExecuteAsync(request, requestGeneration, operation.CancellationToken);
    }
    finally
    {
      operation.Dispose();
      lock (sync)
      {
        if (ReferenceEquals(activeOperation, operation))
        {
          activeOperation = null;
        }
      }
    }
  }

  private async Task<ReaderPreparationResult> ExecuteAsync(
    ReaderPreparationRequest request,
    long requestGeneration,
    CancellationToken cancellationToken)
  {
    ReaderPreparationDirective startDirectives = ReaderPreparationDirective.StopVoicePreview
      | ReaderPreparationDirective.ResetPlaybackView;
    Publish(new ReaderPreparationState(
      ReaderPreparationPhase.WaitingForOperation,
      request.Kind == ReaderPreparationKind.CurrentSection
        ? ReaderPreparationStatus.PreparingSectionNarration
        : ReaderPreparationStatus.PreparingRangeNarration,
      request.Kind,
      0,
      request.Kind == ReaderPreparationKind.CurrentSection ? 1 : request.SelectedSections.Count,
      0d,
      0d,
      false,
      timeProvider.GetUtcNow(),
      TimeSpan.Zero,
      startDirectives), requestGeneration);
    playbackSession.BeginSectionPreparation();
    media.Stop();

    using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
      diagnostics,
      operationName: request.Kind == ReaderPreparationKind.CurrentSection
        ? "ReaderSectionPreparation"
        : "ReaderRangePreparation",
      provider: request.NarrationProfile.ProviderId,
      model: request.NarrationProfile.VoiceId);

    try
    {
      ReaderPreparationResult result = request.Kind == ReaderPreparationKind.CurrentSection
        ? await PrepareCurrentSectionAsync(request, requestGeneration, cancellationToken)
        : await PrepareRangeAsync(request, requestGeneration, cancellationToken);
      if (result.Outcome == ReaderPreparationOutcome.Succeeded)
      {
        opScope.Complete();
      }
      else if (result.Outcome == ReaderPreparationOutcome.Canceled)
      {
        opScope.Cancel("Reader preparation cancelled.");
      }
      else
      {
        opScope.Complete();
      }

      return result;
    }
    catch (OperationCanceledException)
    {
      opScope.Cancel("Reader preparation cancelled.");
      media.Stop();
      playbackSession.BeginSectionPreparation();
      if (!IsCurrent(requestGeneration))
      {
        return new ReaderPreparationResult(
          ReaderPreparationOutcome.Stale,
          ReaderPreparationStatus.Canceled,
          ReaderPreparationDirective.ResetPlaybackView);
      }

      PublishTerminal(ReaderPreparationPhase.Canceled, ReaderPreparationStatus.Canceled, request, requestGeneration);
      return new ReaderPreparationResult(
        ReaderPreparationOutcome.Canceled,
        ReaderPreparationStatus.Canceled,
        ReaderPreparationDirective.ResetPlaybackView);
    }
    catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.IO.IOException or UnauthorizedAccessException)
    {
      string operationName = request.Kind == ReaderPreparationKind.CurrentSection
        ? "Reader section preparation"
        : "Reader range preparation";
      opScope.Fail(ex, $"{operationName} failed: {ex.Message}");

      if (!IsCurrent(requestGeneration))
      {
        return new ReaderPreparationResult(
          ReaderPreparationOutcome.Stale,
          ReaderPreparationStatus.Canceled,
          ReaderPreparationDirective.ResetPlaybackView);
      }

      media.Stop();
      playbackSession.BeginSectionPreparation();
      diagnostics.Warning($"{operationName} failed: {ex}");
      ReaderPreparationStatus status = request.Kind == ReaderPreparationKind.CurrentSection
        ? ReaderPreparationStatus.SectionFailed
        : ReaderPreparationStatus.RangeFailed;
      PublishTerminal(ReaderPreparationPhase.Failed, status, request, requestGeneration);
      return new ReaderPreparationResult(
        ReaderPreparationOutcome.Failed,
        status,
        ReaderPreparationDirective.ResetPlaybackView);
    }
  }

  private async Task<ReaderPreparationResult> PrepareCurrentSectionAsync(
    ReaderPreparationRequest request,
    long requestGeneration,
    CancellationToken cancellationToken)
  {
    ReadingSection section = request.CurrentSection;
    TextToSpeechResult speech = await GetSpeechAsync(
      section,
      request.NarrationProfile,
      request,
      requestGeneration,
      0,
      1,
      cancellationToken);
    ThrowIfStale(requestGeneration, cancellationToken);
    PublishPhase(
      ReaderPreparationPhase.OpeningAudio,
      ReaderPreparationStatus.OpeningSectionAudio,
      request,
      0,
      1,
      0.52d,
      0.58d,
      false,
      TimeSpan.FromSeconds(1),
      requestGeneration);
    TimeSpan duration = await media.OpenAsync(speech.AudioPath, cancellationToken);
    ThrowIfStale(requestGeneration, cancellationToken);
    ReaderWordTimingMap timing = await GetTimingAsync(
      section,
      request.NarrationProfile,
      speech,
      request,
      requestGeneration,
      0,
      1,
      0.58d,
      0.97d,
      cancellationToken);
    ThrowIfStale(requestGeneration, cancellationToken);
    playbackSession.SetSectionDuration(duration);
    playbackSession.CompleteSectionPreparation(timing);
    return Complete(request, requestGeneration, speech, timing, 1);
  }

  private async Task<ReaderPreparationResult> PrepareRangeAsync(
    ReaderPreparationRequest request,
    long requestGeneration,
    CancellationToken cancellationToken)
  {
    prefetchSession.Cancel();
    TextToSpeechResult? firstSpeech = null;
    ReaderWordTimingMap? firstTiming = null;
    int total = request.SelectedSections.Count;
    for (int offset = 0; offset < total; offset++)
    {
      ThrowIfStale(requestGeneration, cancellationToken);
      ReadingSection section = request.SelectedSections[offset];
      double sectionStart = (double)offset / total;
      double sectionEnd = (double)(offset + 1) / total;
      TextToSpeechResult speech = await GetSpeechAsync(
        section,
        request.NarrationProfile,
        request,
        requestGeneration,
        offset,
        total,
        cancellationToken,
        sectionStart,
        sectionStart + ((sectionEnd - sectionStart) * 0.58d));
      ThrowIfStale(requestGeneration, cancellationToken);
      ReaderWordTimingMap timing = await GetTimingAsync(
        section,
        request.NarrationProfile,
        speech,
        request,
        requestGeneration,
        offset,
        total,
        sectionStart + ((sectionEnd - sectionStart) * 0.58d),
        sectionEnd * 0.997d,
        cancellationToken);
      if (offset == request.CurrentSectionOffset)
      {
        firstSpeech = speech;
        firstTiming = timing;
      }
    }

    ThrowIfStale(requestGeneration, cancellationToken);
    if (firstSpeech is null || firstTiming is null)
    {
      throw new InvalidOperationException("The first prepared section or its timing is unavailable.");
    }
    PublishPhase(
      ReaderPreparationPhase.OpeningAudio,
      ReaderPreparationStatus.OpeningPreparedRange,
      request,
      total,
      total,
      0.997d,
      1d,
      true,
      TimeSpan.FromSeconds(1),
      requestGeneration);
    TimeSpan duration = await media.OpenAsync(firstSpeech.AudioPath, cancellationToken);
    ThrowIfStale(requestGeneration, cancellationToken);
    playbackSession.SetSectionDuration(duration);
    playbackSession.CompleteSectionPreparation(firstTiming);
    return Complete(request, requestGeneration, firstSpeech, firstTiming, total);
  }

  private async Task<TextToSpeechResult> GetSpeechAsync(
    ReadingSection section,
    ReaderNarrationProfile profile,
    ReaderPreparationRequest request,
    long requestGeneration,
    int sectionIndex,
    int sectionCount,
    CancellationToken cancellationToken,
    double progressStart = 0d,
    double progressEnd = 0.52d)
  {
    if (!prefetchSession.IsFor(section, profile))
    {
      prefetchSession.Cancel();
    }

    if (prefetchSession.TryTake(section, profile, out Task<TextToSpeechResult>? prefetched) && prefetched is not null)
    {
      PublishNarration(request, sectionIndex, sectionCount, progressEnd, progressEnd, true, TimeSpan.Zero, requestGeneration);
      return await prefetched.WaitAsync(cancellationToken);
    }

    if (narrationSession.TryGetSpeech(section, profile, out TextToSpeechResult? cached) && cached is not null)
    {
      PublishNarration(request, sectionIndex, sectionCount, progressEnd, progressEnd, true, TimeSpan.Zero, requestGeneration);
      return cached;
    }

    PublishNarration(
      request,
      sectionIndex,
      sectionCount,
      progressStart,
      progressEnd,
      false,
      EstimateNarrationWork(section),
      requestGeneration);

    return await narrationSession.GetSpeechAsync(section, profile, cancellationToken);
  }

  private async Task<ReaderWordTimingMap> GetTimingAsync(
    ReadingSection section,
    ReaderNarrationProfile profile,
    TextToSpeechResult speech,
    ReaderPreparationRequest request,
    long requestGeneration,
    int sectionIndex,
    int sectionCount,
    double progressStart,
    double progressEnd,
    CancellationToken cancellationToken)
  {
    bool cached = narrationSession.TryGetTiming(section, profile, speech, out ReaderWordTimingMap? existing)
      && existing is not null;
    PublishPhase(
      ReaderPreparationPhase.PreparingTiming,
      request.Kind == ReaderPreparationKind.CurrentSection
        ? ReaderPreparationStatus.PreparingSectionTiming
        : ReaderPreparationStatus.PreparingRangeTiming,
      request,
      sectionIndex,
      sectionCount,
      cached ? progressEnd : progressStart,
      progressEnd,
      cached,
      cached ? TimeSpan.Zero : EstimateTimingWork(profile, speech),
      requestGeneration);
    ReaderWordTimingMap timing = existing
      ?? await narrationSession.GetTimingAsync(section, profile, speech, cancellationToken);
    return timing;
  }

  private ReaderPreparationResult Complete(
    ReaderPreparationRequest request,
    long requestGeneration,
    TextToSpeechResult speech,
    ReaderWordTimingMap timing,
    int count)
  {
    ThrowIfStale(requestGeneration, CancellationToken.None);
    ReaderPreparationDirective directives = request.PlayWhenReady
      ? ReaderPreparationDirective.PlayWhenReady
      : ReaderPreparationDirective.NotifyCompletion;
    ReaderPreparationStatus status = request.Kind == ReaderPreparationKind.CurrentSection
      ? ReaderPreparationStatus.SectionReady
      : ReaderPreparationStatus.RangeReady;
    lock (sync)
    {
      preparedRequest = request;
    }

    Publish(new ReaderPreparationState(
      ReaderPreparationPhase.Ready,
      status,
      request.Kind,
      count,
      count,
      1d,
      1d,
      true,
      timeProvider.GetUtcNow(),
      TimeSpan.Zero,
      directives), requestGeneration);
    return new ReaderPreparationResult(
      ReaderPreparationOutcome.Succeeded,
      status,
      directives,
      speech,
      timing,
      count);
  }

  private void PublishNarration(
    ReaderPreparationRequest request,
    int sectionIndex,
    int sectionCount,
    double start,
    double end,
    bool cached,
    TimeSpan estimate,
    long requestGeneration) => PublishPhase(
      ReaderPreparationPhase.PreparingNarration,
      request.Kind == ReaderPreparationKind.CurrentSection
        ? ReaderPreparationStatus.PreparingSectionNarration
        : ReaderPreparationStatus.PreparingRangeNarration,
      request,
      sectionIndex,
      sectionCount,
      start,
      end,
      cached,
      estimate,
      requestGeneration);

  private void PublishPhase(
    ReaderPreparationPhase phase,
    ReaderPreparationStatus status,
    ReaderPreparationRequest request,
    int completedSections,
    int totalSections,
    double progressStart,
    double progressEnd,
    bool cached,
    TimeSpan estimate,
    long requestGeneration) => Publish(new ReaderPreparationState(
      phase,
      status,
      request.Kind,
      completedSections,
      totalSections,
      progressStart,
      progressEnd,
      cached,
      timeProvider.GetUtcNow(),
      estimate,
      ReaderPreparationDirective.None), requestGeneration);

  private void PublishTerminal(
    ReaderPreparationPhase phase,
    ReaderPreparationStatus status,
    ReaderPreparationRequest request,
    long requestGeneration) => Publish(new ReaderPreparationState(
      phase,
      status,
      request.Kind,
      0,
      request.Kind == ReaderPreparationKind.CurrentSection ? 1 : request.SelectedSections.Count,
      0d,
      0d,
      false,
      timeProvider.GetUtcNow(),
      TimeSpan.Zero,
      ReaderPreparationDirective.ResetPlaybackView), requestGeneration);

  private void Publish(ReaderPreparationState next, long requestGeneration)
  {
    EventHandler<ReaderPreparationState>? handler;
    lock (sync)
    {
      if (disposed || generation != requestGeneration)
      {
        return;
      }

      state = next;
      handler = StateChanged;
    }

    handler?.Invoke(this, next);
  }

  private bool IsCurrent(long requestGeneration)
  {
    lock (sync)
    {
      return !disposed && generation == requestGeneration;
    }
  }

  private void ThrowIfStale(long requestGeneration, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!IsCurrent(requestGeneration))
    {
      throw new OperationCanceledException("The preparation request was superseded.", cancellationToken);
    }
  }

  private static ReaderPreparationResult BusyResult() => new(
    ReaderPreparationOutcome.Busy,
    ReaderPreparationStatus.None,
    ReaderPreparationDirective.None);

  private static TimeSpan EstimateNarrationWork(ReadingSection section)
  {
    double seconds = 4d + (section.Words.Count * 0.10d) + (section.Text.Length * 0.008d);
    return TimeSpan.FromSeconds(Math.Clamp(seconds, 5d, 240d));
  }

  private static TimeSpan EstimateTimingWork(ReaderNarrationProfile profile, TextToSpeechResult speech) =>
    profile.WordTimingStrategy switch
    {
      ReaderWordTimingStrategy.NativeWithDeterministicFallback => TimeSpan.FromSeconds(1),
      ReaderWordTimingStrategy.LocalForcedAlignment => TimeSpan.FromSeconds(
        Math.Clamp(5d + (speech.Duration.TotalSeconds * 2.3d), 7d, 300d)),
      _ => throw new ArgumentOutOfRangeException(
        nameof(profile),
        profile.WordTimingStrategy,
        "Unknown reader word-timing strategy."),
    };

}
