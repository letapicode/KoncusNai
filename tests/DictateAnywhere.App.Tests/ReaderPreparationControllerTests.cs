using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Each Harness takes ownership of the injected fake media adapter and disposes it through the controller.")]
public sealed class ReaderPreparationControllerTests
{
  [Xunit.Fact]
  public void PreparationState_CalculatesEstimatedProgressFromAnExplicitClockValue()
  {
    DateTimeOffset started = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    ReaderPreparationState state = new(
      ReaderPreparationPhase.PreparingNarration,
      ReaderPreparationStatus.PreparingSectionNarration,
      ReaderPreparationKind.CurrentSection,
      0,
      1,
      0.20d,
      0.60d,
      false,
      started,
      TimeSpan.FromSeconds(10),
      ReaderPreparationDirective.None);

    double progress = state.GetNormalizedProgress(started.AddSeconds(5));

    Xunit.Assert.InRange(progress, 0.43d, 0.46d);
  }

  [Xunit.Fact]
  public async Task CurrentSection_PreparesNarrationTimingAndMediaWithoutWpf()
  {
    await using Harness harness = new();
    List<ReaderPreparationDirective> directives = [];
    harness.Controller.StateChanged += (_, state) => directives.Add(state.Directives);

    ReaderPreparationResult result = await harness.Controller.PrepareAsync(harness.SectionRequest());

    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, result.Outcome);
    Xunit.Assert.Equal(1, harness.Speech.CallCount);
    Xunit.Assert.Equal(["audio-1.wav"], harness.Media.OpenedPaths.Select(Path.GetFileName));
    Xunit.Assert.True(harness.Playback.HasPreparedSection);
    Xunit.Assert.Equal(TimeSpan.FromSeconds(3), harness.Playback.SectionDuration);
    Xunit.Assert.Equal(ReaderPreparationPhase.Ready, harness.Controller.State.Phase);
    Xunit.Assert.Contains(directives, directive =>
      directive.HasFlag(ReaderPreparationDirective.StopVoicePreview)
      && directive.HasFlag(ReaderPreparationDirective.ResetPlaybackView));
    Xunit.Assert.True(result.Directives.HasFlag(ReaderPreparationDirective.NotifyCompletion));
  }

  [Xunit.Fact]
  public async Task CurrentSection_ReusesCachedNarration()
  {
    await using Harness harness = new();

    ReaderPreparationResult first = await harness.Controller.PrepareAsync(harness.SectionRequest());
    ReaderPreparationResult second = await harness.Controller.PrepareAsync(harness.SectionRequest());

    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, first.Outcome);
    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, second.Outcome);
    Xunit.Assert.Equal(1, harness.Speech.CallCount);
    Xunit.Assert.Equal(2, harness.Media.OpenedPaths.Count);
  }

  [Xunit.Fact]
  public async Task CurrentSection_ConsumesAnExactPrefetchedNarration()
  {
    await using Harness harness = new(sectionCount: 2);
    ReaderPreparationRequest request = harness.SectionRequest(currentOffset: 1);
    Xunit.Assert.True(harness.Prefetch.Begin(request.CurrentSection, request.NarrationProfile));
    await harness.Speech.WaitForCallsAsync(1);

    ReaderPreparationResult result = await harness.Controller.PrepareAsync(request);

    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, result.Outcome);
    Xunit.Assert.Equal(1, harness.Speech.CallCount);
    Xunit.Assert.False(harness.Prefetch.TryTake(request.CurrentSection, request.NarrationProfile, out _));
  }

  [Xunit.Fact]
  public async Task CurrentSection_CancelsAMismatchedPrefetch()
  {
    await using Harness harness = new(sectionCount: 2);
    Xunit.Assert.True(harness.Prefetch.Begin(harness.Sections[0], harness.Profile));
    await harness.Speech.WaitForCallsAsync(1);

    ReaderPreparationResult result = await harness.Controller.PrepareAsync(
      harness.SectionRequest(currentOffset: 1));

    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, result.Outcome);
    Xunit.Assert.False(harness.Prefetch.TryTake(harness.Sections[0], harness.Profile, out _));
    Xunit.Assert.Equal(2, harness.Speech.CallCount);
  }

  [Xunit.Fact]
  public async Task SelectedRange_PreparesInOrderAndReportsThePreparedCount()
  {
    await using Harness harness = new(sectionCount: 3);
    List<ReaderPreparationState> states = [];
    harness.Controller.StateChanged += (_, state) => states.Add(state);

    ReaderPreparationResult result = await harness.Controller.PrepareAsync(harness.RangeRequest());

    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, result.Outcome);
    Xunit.Assert.Equal(3, result.PreparedSectionCount);
    Xunit.Assert.Equal(3, harness.Speech.CallCount);
    Xunit.Assert.Equal(
      harness.Sections.Select(ReaderNarrationText.Create),
      harness.Speech.Requests.Select(request => request.Text));
    Xunit.Assert.Contains(states, state =>
      state.Status == ReaderPreparationStatus.PreparingRangeTiming
      && state.TotalSections == 3);
    Xunit.Assert.Equal("audio-1.wav", Path.GetFileName(Xunit.Assert.Single(harness.Media.OpenedPaths)));
  }

  [Xunit.Fact]
  public async Task SelectedRange_UsesExistingCacheAndStillActivatesTheFirstSection()
  {
    await using Harness harness = new(sectionCount: 3);
    ReaderNarrationProfile profile = harness.Profile;
    _ = await harness.Narration.GetSpeechAsync(harness.Sections[0], profile);
    Xunit.Assert.True(harness.Narration.TryGetSpeech(harness.Sections[0], profile, out _));

    ReaderPreparationResult result = await harness.Controller.PrepareAsync(harness.RangeRequest());

    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, result.Outcome);
    Xunit.Assert.Equal(3, harness.Speech.CallCount);
    Xunit.Assert.True(harness.Playback.HasPreparedSection);
    Xunit.Assert.Equal(0, harness.Playback.CurrentSectionIndex);
  }

  [Xunit.Fact]
  public async Task CancellationDuringNarrationRejectsTheOldCompletionAndAllowsRetry()
  {
    BlockingSpeechService speech = new();
    await using Harness harness = new(speechService: speech);
    Task<ReaderPreparationResult> preparation = harness.Controller.PrepareAsync(harness.SectionRequest());
    await speech.Started;

    harness.Controller.Cancel();
    ReaderPreparationResult canceled = await preparation;
    Xunit.Assert.Equal(ReaderPreparationOutcome.Stale, canceled.Outcome);
    Xunit.Assert.Equal(ReaderPreparationPhase.Idle, harness.Controller.State.Phase);

    speech.ReleaseWithSuccess();
    ReaderPreparationResult retry = await harness.Controller.PrepareAsync(harness.SectionRequest());

    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, retry.Outcome);
    Xunit.Assert.True(harness.Playback.HasPreparedSection);
  }

  [Xunit.Fact]
  public async Task CancellationDuringMediaOpenCleansUpAndDoesNotPreparePlayback()
  {
    BlockingMedia media = new();
    await using Harness harness = new(media: media);
    Task<ReaderPreparationResult> preparation = harness.Controller.PrepareAsync(harness.SectionRequest());
    await media.OpenStarted;

    harness.Controller.Cancel();
    ReaderPreparationResult result = await preparation;

    Xunit.Assert.Equal(ReaderPreparationOutcome.Stale, result.Outcome);
    Xunit.Assert.False(harness.Playback.HasPreparedSection);
    Xunit.Assert.True(media.OpenCancellationObserved);
  }

  [Xunit.Fact]
  public async Task CancellationDuringTimingDoesNotActivateTheOpenedMedia()
  {
    BlockingAlignmentService alignment = new();
    await using Harness harness = new(alignmentService: alignment);
    ReaderNarrationProfile forcedAlignment = new(
      "en-us",
      "kokoro-local",
      "af_bella",
      "Warm narration",
      ReaderWordTimingStrategy.LocalForcedAlignment);
    Task<ReaderPreparationResult> preparation = harness.Controller.PrepareAsync(
      harness.SectionRequest(profile: forcedAlignment));
    await alignment.Started;

    harness.Controller.Cancel();
    ReaderPreparationResult result = await preparation;

    Xunit.Assert.Equal(ReaderPreparationOutcome.Stale, result.Outcome);
    Xunit.Assert.False(harness.Playback.HasPreparedSection);
  }

  [Xunit.Fact]
  public async Task CancellationBetweenRangeSectionsDoesNotOpenTheFirstPreparedSection()
  {
    BlockingSecondSpeechService speech = new();
    await using Harness harness = new(sectionCount: 2, speechService: speech);
    Task<ReaderPreparationResult> preparation = harness.Controller.PrepareAsync(harness.RangeRequest());
    await speech.SecondCallStarted;

    harness.Controller.Cancel();
    ReaderPreparationResult result = await preparation;

    Xunit.Assert.Equal(ReaderPreparationOutcome.Stale, result.Outcome);
    Xunit.Assert.Empty(harness.Media.OpenedPaths);
    Xunit.Assert.False(harness.Playback.HasPreparedSection);
  }

  [Xunit.Fact]
  public async Task InvalidationRejectsLateWorkAndAChangedProfileCanRetry()
  {
    BlockingSpeechService speech = new();
    await using Harness harness = new(speechService: speech);
    int stateCount = 0;
    harness.Controller.StateChanged += (_, _) => stateCount++;
    Task<ReaderPreparationResult> oldPreparation = harness.Controller.PrepareAsync(harness.SectionRequest());
    await speech.Started;

    harness.Controller.Invalidate(clearNarrationCache: true);
    int countAfterInvalidation = stateCount;
    ReaderPreparationResult oldResult = await oldPreparation;
    Xunit.Assert.Equal(countAfterInvalidation, stateCount);
    speech.ReleaseWithSuccess();
    ReaderNarrationProfile changedProfile = new(
      "en-us",
      "kokoro-local",
      "am_adam",
      "Changed voice",
      ReaderWordTimingStrategy.NativeWithDeterministicFallback);
    ReaderPreparationResult retry = await harness.Controller.PrepareAsync(
      harness.SectionRequest(profile: changedProfile));

    Xunit.Assert.Equal(ReaderPreparationOutcome.Stale, oldResult.Outcome);
    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, retry.Outcome);
  }

  [Xunit.Fact]
  public async Task SharedOperationArbitrationRejectsPreparationOwnedByExport()
  {
    await using Harness harness = new();
    using ReaderOperationSession.ReaderOperation export = harness.Operations.TryBegin(
      ReaderOperationKind.AudioExport)!;

    ReaderPreparationResult result = await harness.Controller.PrepareAsync(harness.SectionRequest());

    Xunit.Assert.Equal(ReaderPreparationOutcome.Busy, result.Outcome);
    Xunit.Assert.Equal(0, harness.Speech.CallCount);
  }

  [Xunit.Fact]
  public async Task FailureIsDiagnosedWithTechnicalDetailButReturnsASafeStatusAndCanRetry()
  {
    FailingOnceSpeechService speech = new();
    await using Harness harness = new(speechService: speech);

    ReaderPreparationResult failed = await harness.Controller.PrepareAsync(harness.SectionRequest());
    ReaderPreparationResult retry = await harness.Controller.PrepareAsync(harness.SectionRequest());

    Xunit.Assert.Equal(ReaderPreparationOutcome.Failed, failed.Outcome);
    Xunit.Assert.Equal(ReaderPreparationStatus.SectionFailed, failed.Status);
    Xunit.Assert.Contains("synthesis-secret-detail", Xunit.Assert.Single(harness.Diagnostics.Warnings));
    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, retry.Outcome);
  }

  [Xunit.Fact]
  public async Task TimingFailureLeavesPlaybackUnpreparedAndReturnsASafeFailure()
  {
    await using Harness harness = new(alignmentService: new FailingAlignmentService());
    ReaderNarrationProfile forcedAlignment = new(
      "en-us",
      "kokoro-local",
      "af_bella",
      "Warm narration",
      ReaderWordTimingStrategy.LocalForcedAlignment);

    ReaderPreparationResult result = await harness.Controller.PrepareAsync(
      harness.SectionRequest(profile: forcedAlignment));

    Xunit.Assert.Equal(ReaderPreparationOutcome.Failed, result.Outcome);
    Xunit.Assert.False(harness.Playback.HasPreparedSection);
    Xunit.Assert.Contains("alignment-technical-detail", Xunit.Assert.Single(harness.Diagnostics.Warnings));
  }

  [Xunit.Fact]
  public async Task MediaOpenFailureLeavesPlaybackUnpreparedAndCanRetry()
  {
    FailingOnceMedia media = new();
    await using Harness harness = new(media: media);

    ReaderPreparationResult failure = await harness.Controller.PrepareAsync(harness.SectionRequest());
    ReaderPreparationResult retry = await harness.Controller.PrepareAsync(harness.SectionRequest());

    Xunit.Assert.Equal(ReaderPreparationOutcome.Failed, failure.Outcome);
    Xunit.Assert.False(failure.Directives.HasFlag(ReaderPreparationDirective.PlayWhenReady));
    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, retry.Outcome);
    Xunit.Assert.True(harness.Playback.HasPreparedSection);
  }

  [Xunit.Fact]
  public async Task SuccessfulPlaybackPrefetchesOnlyTheNextExactSectionAndProfile()
  {
    await using Harness harness = new(sectionCount: 2);
    ReaderPreparationResult result = await harness.Controller.PrepareAsync(harness.SectionRequest());

    bool began = harness.Controller.BeginNextSectionPrefetch();
    await harness.Speech.WaitForCallsAsync(2);

    Xunit.Assert.Equal(ReaderPreparationOutcome.Succeeded, result.Outcome);
    Xunit.Assert.True(began);
    Xunit.Assert.True(harness.Prefetch.IsFor(harness.Sections[1], harness.Profile));
  }

  [Xunit.Fact]
  public async Task DisposeCancelsAndAwaitsActiveWorkWithoutLateStateMutation()
  {
    BlockingSpeechService speech = new();
    Harness harness = new(speechService: speech);
    int stateCount = 0;
    harness.Controller.StateChanged += (_, _) => stateCount++;
    Task<ReaderPreparationResult> preparation = harness.Controller.PrepareAsync(harness.SectionRequest());
    await speech.Started;

    await harness.DisposeAsync();
    int stateCountAfterDispose = stateCount;
    ReaderPreparationResult result = await preparation;

    Xunit.Assert.Equal(ReaderPreparationOutcome.Stale, result.Outcome);
    Xunit.Assert.Equal(stateCountAfterDispose, stateCount);
    Xunit.Assert.True(harness.Media.Disposed);
  }

  private sealed class Harness : IAsyncDisposable
  {
    private readonly string audioDirectory = Path.Combine(
      Path.GetTempPath(),
      $"notype-reader-preparation-{Guid.NewGuid():N}");
    private bool disposed;

    public Harness(
      int sectionCount = 1,
      ITextToSpeechService? speechService = null,
      FakeMedia? media = null,
      ISpeechAlignmentService? alignmentService = null)
    {
      Directory.CreateDirectory(audioDirectory);
      ReadingDocument document = ReadingTextLayout.Create(
        "Test",
        Enumerable.Range(1, sectionCount)
          .Select(index => new ReadableDocumentSection($"Part {index}", $"Section {index} has useful words."))
          .ToArray());
      Sections = document.Sections;
      Speech = speechService as RecordingSpeechService ?? new RecordingSpeechService(audioDirectory);
      SpeechService = speechService ?? Speech;
      Operations = new ReaderOperationSession();
      Playback = new ReaderPlaybackSession(sectionCount);
      Playback.SelectRange(0, sectionCount - 1);
      Playback.ActivateSelection();
      Narration = new ReaderNarrationSession(
        SpeechService,
        alignmentService ?? new UnusedAlignmentService());
      Prefetch = new ReaderNarrationPrefetchSession(Narration);
      Media = media ?? new FakeMedia();
      Diagnostics = new RecordingDiagnostics();
      Controller = new ReaderPreparationController(
        Operations,
        Playback,
        Narration,
        Prefetch,
        Media,
        Diagnostics);
    }

    public IReadOnlyList<ReadingSection> Sections { get; }
    public RecordingSpeechService Speech { get; }
    public ITextToSpeechService SpeechService { get; }
    public ReaderOperationSession Operations { get; }
    public ReaderPlaybackSession Playback { get; }
    public ReaderNarrationSession Narration { get; }
    public ReaderNarrationPrefetchSession Prefetch { get; }
    public FakeMedia Media { get; }
    public RecordingDiagnostics Diagnostics { get; }
    public ReaderPreparationController Controller { get; }
    public ReaderNarrationProfile Profile { get; } = new(
      "en-us",
      "kokoro-local",
      "af_bella",
      "Warm narration",
      ReaderWordTimingStrategy.NativeWithDeterministicFallback);

    public ReaderPreparationRequest SectionRequest(
      int currentOffset = 0,
      ReaderNarrationProfile? profile = null) => new(
        ReaderPreparationKind.CurrentSection,
        Sections,
        currentOffset,
        profile ?? Profile,
        playWhenReady: false);

    public ReaderPreparationRequest RangeRequest() => new(
      ReaderPreparationKind.SelectedRange,
      Sections,
      0,
      Profile,
      playWhenReady: false);

    public async ValueTask DisposeAsync()
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      await Controller.DisposeAsync();
      await Prefetch.DisposeAsync();
      Operations.Dispose();
      await Narration.DisposeAsync();
      if (Directory.Exists(audioDirectory))
      {
        Directory.Delete(audioDirectory, recursive: true);
      }
    }
  }

  private class FakeMedia : IReaderPlaybackMedia
  {
    public event EventHandler? Ended { add { } remove { } }
    public event EventHandler<ReaderPlaybackMediaFailedEventArgs>? Failed { add { } remove { } }
    public List<string> OpenedPaths { get; } = [];
    public bool Disposed { get; private set; }
    public TimeSpan Position { get; set; }
    public double SpeedRatio { get; set; } = 1d;

    public virtual Task<TimeSpan> OpenAsync(string audioPath, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      OpenedPaths.Add(audioPath);
      Position = TimeSpan.Zero;
      return Task.FromResult(TimeSpan.FromSeconds(3));
    }

    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void Close() { }
    public void Dispose() => Disposed = true;
  }

  private sealed class BlockingMedia : FakeMedia
  {
    private readonly TaskCompletionSource openStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task OpenStarted => openStarted.Task;
    public bool OpenCancellationObserved { get; private set; }

    public override async Task<TimeSpan> OpenAsync(string audioPath, CancellationToken cancellationToken)
    {
      OpenedPaths.Add(audioPath);
      openStarted.TrySetResult();
      try
      {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      }
      catch (OperationCanceledException)
      {
        OpenCancellationObserved = true;
        throw;
      }

      throw new InvalidOperationException("Unreachable.");
    }
  }

  private sealed class FailingOnceMedia : FakeMedia
  {
    private bool failed;

    public override Task<TimeSpan> OpenAsync(string audioPath, CancellationToken cancellationToken)
    {
      if (!failed)
      {
        failed = true;
        return Task.FromException<TimeSpan>(new IOException("media-open-technical-detail"));
      }

      return base.OpenAsync(audioPath, cancellationToken);
    }
  }

  private sealed class RecordingSpeechService(string audioDirectory) : ITextToSpeechService
  {
    private readonly TaskCompletionSource firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<TextToSpeechRequest> Requests { get; } = [];
    public int CallCount => Requests.Count;

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Requests.Add(request);
      firstCall.TrySetResult();
      string path = Path.Combine(audioDirectory, $"audio-{Requests.Count}.wav");
      File.WriteAllBytes(path, new byte[64]);
      return Task.FromResult(CreateSpeech(Requests.Count, path));
    }

    public async Task WaitForCallsAsync(int count)
    {
      while (CallCount < count)
      {
        await firstCall.Task;
        await Task.Yield();
      }
    }
  }

  private sealed class BlockingSpeechService : ITextToSpeechService
  {
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool released;
    public Task Started => started.Task;

    public async Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      if (released)
      {
        return CreateSpeech(2);
      }

      started.TrySetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      throw new InvalidOperationException("Unreachable.");
    }

    public void ReleaseWithSuccess() => released = true;
  }

  private sealed class FailingOnceSpeechService : ITextToSpeechService
  {
    private int calls;

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      calls++;
      return calls == 1
        ? Task.FromException<TextToSpeechResult>(new InvalidOperationException("synthesis-secret-detail"))
        : Task.FromResult(CreateSpeech(calls));
    }
  }

  private sealed class BlockingSecondSpeechService : ITextToSpeechService
  {
    private readonly TaskCompletionSource secondCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int calls;
    public Task SecondCallStarted => secondCallStarted.Task;

    public async Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      calls++;
      if (calls == 2)
      {
        secondCallStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      }

      return CreateSpeech(calls);
    }
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<string> Warnings { get; } = [];
    public void Info(string message) { }
    public void Warning(string message) => Warnings.Add(message);
    public void Error(string message, Exception? exception = null) { }
  }

  private sealed class UnusedAlignmentService : ISpeechAlignmentService
  {
    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromException<SpeechAlignmentResult>(new InvalidOperationException("Forced alignment is not expected."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FailingAlignmentService : ISpeechAlignmentService
  {
    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromException<SpeechAlignmentResult>(new IOException("alignment-technical-detail"));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class BlockingAlignmentService : ISpeechAlignmentService
  {
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Started => started.Task;

    public async Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default)
    {
      started.TrySetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      throw new InvalidOperationException("Unreachable.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private static TextToSpeechResult CreateSpeech(int index, string? path = null) => new(
    path ?? $"audio-{index}.wav",
    TimeSpan.FromSeconds(3),
    1,
    "test-provider",
    "test-model");
}
