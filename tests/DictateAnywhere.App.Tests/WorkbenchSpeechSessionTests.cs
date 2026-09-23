using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Each WorkbenchSpeechSession under test takes ownership of its fake playback service and any created speech service.")]
public sealed class WorkbenchSpeechSessionTests
{
  [Xunit.Fact]
  public async Task ReadAloudController_MapsPreparationAndPlaybackToPresentationState()
  {
    RecordingPlaybackService playbackService = new();
    RecordingDiagnostics diagnostics = new();
    await using WorkbenchReadAloudController controller = new(
      new WorkbenchSpeechSession(
        () => new RecordingSpeechService(CreateSpeech("controller.wav")),
        playbackService),
      diagnostics);
    List<WorkbenchReadAloudState> states = [];
    controller.StateChanged += (_, state) => states.Add(state);

    await controller.ReadAsync("Read this response.", "this response");

    Xunit.Assert.True(states.Count >= 2);
    WorkbenchReadAloudState preparing = states[0];
    Xunit.Assert.True(preparing.IsPreparationVisible);
    Xunit.Assert.True(preparing.IsPreparing);
    Xunit.Assert.True(preparing.CanStop);
    Xunit.Assert.Contains("Preparing this response", preparing.StatusMessage, StringComparison.Ordinal);
    WorkbenchReadAloudState playing = states[^1];
    Xunit.Assert.False(playing.IsPreparationVisible);
    Xunit.Assert.True(playing.CanStop);
    Xunit.Assert.Equal("Reading this response aloud (1 segment).", playing.StatusMessage);
    Xunit.Assert.Empty(diagnostics.Errors);
  }

  [Xunit.Fact]
  public async Task ReadAloudController_ObservesUnexpectedBackgroundFailures()
  {
    RecordingDiagnostics diagnostics = new();
    InvalidDataException failure = new("Corrupt synthesized audio.");
    await using WorkbenchReadAloudController controller = new(
      new WorkbenchSpeechSession(
        () => new ThrowingSpeechService(failure),
        new RecordingPlaybackService()),
      diagnostics);
    List<WorkbenchReadAloudState> states = [];
    controller.StateChanged += (_, state) => states.Add(state);

    await controller.ReadAsync("Read this response.", "this response");

    Xunit.Assert.Contains(states, state => state.StatusMessage == "Could not prepare speech. The error was recorded in Diagnostics.");
    Xunit.Assert.Contains(diagnostics.Errors, item => ReferenceEquals(item.Exception, failure));
  }

  [Xunit.Fact]
  public async Task ReadAloudController_PlaybackFailureKeepsTechnicalDetailOutOfUserStatus()
  {
    RecordingPlaybackService playbackService = new();
    RecordingDiagnostics diagnostics = new();
    await using WorkbenchReadAloudController controller = new(
      new WorkbenchSpeechSession(
        () => new RecordingSpeechService(CreateSpeech("playback-failure.wav")),
        playbackService),
      diagnostics);
    List<WorkbenchReadAloudState> states = [];
    controller.StateChanged += (_, state) => states.Add(state);
    InvalidOperationException failure = new("Internal endpoint 17 was disconnected.");

    await controller.ReadAsync("Read this response.", "this response");
    playbackService.FailPlayback(failure);

    Xunit.Assert.Equal(
      "Could not play speech. Check the audio device or see Diagnostics.",
      states[^1].StatusMessage);
    Xunit.Assert.DoesNotContain(failure.Message, states[^1].StatusMessage, StringComparison.Ordinal);
    Xunit.Assert.Contains(diagnostics.Warnings, message => message.Contains(failure.Message, StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task ReadAloudController_DisposeWaitsForTrackedBackgroundRead()
  {
    ControlledSpeechService speechService = new();
    WorkbenchReadAloudController controller = new(
      new WorkbenchSpeechSession(() => speechService, new RecordingPlaybackService()),
      new RecordingDiagnostics());
    controller.StartRead("Read this response.", "this response");
    await speechService.Started.Task;

    ValueTask disposal = controller.DisposeAsync();

    Xunit.Assert.False(disposal.IsCompleted);
    speechService.Completion.SetResult(CreateSpeech("cancelled-controller.wav"));
    await disposal;
    await controller.DisposeAsync();
  }

  [Xunit.Fact]
  public async Task ReadAsync_LazilySynthesizesAndStartsLocalPlayback()
  {
    RecordingSpeechService speechService = new(CreateSpeech("first.wav"));
    RecordingPlaybackService playbackService = new();
    int factoryCalls = 0;
    await using WorkbenchSpeechSession session = new(
      () =>
      {
        factoryCalls++;
        return speechService;
      },
      playbackService);

    Xunit.Assert.False(session.CanStop);
    TextToSpeechResult result = await session.ReadAsync("Read this response.");

    Xunit.Assert.Equal(1, factoryCalls);
    Xunit.Assert.Equal(1, speechService.CallCount);
    Xunit.Assert.Equal("Read this response.", Xunit.Assert.Single(speechService.Requests).Text);
    Xunit.Assert.Equal(result.AudioPath, Xunit.Assert.Single(playbackService.PlayedPaths));
    Xunit.Assert.False(session.IsPreparing);
    Xunit.Assert.True(session.IsPlaying);
    Xunit.Assert.True(session.CanStop);

    int stopsBeforeExplicitStop = playbackService.StopCount;
    session.Stop();

    Xunit.Assert.False(session.IsPlaying);
    Xunit.Assert.False(session.CanStop);
    Xunit.Assert.Equal(stopsBeforeExplicitStop + 1, playbackService.StopCount);
  }

  [Xunit.Fact]
  public async Task Stop_CancelsPreparationAndRejectsLateSpeech()
  {
    ControlledSpeechService speechService = new();
    RecordingPlaybackService playbackService = new();
    await using WorkbenchSpeechSession session = new(() => speechService, playbackService);
    Task<TextToSpeechResult> preparation = session.ReadAsync("Do not play late audio.");
    await speechService.Started.Task;

    Xunit.Assert.True(session.IsPreparing);
    session.Stop();
    speechService.Completion.SetResult(CreateSpeech("late.wav"));

    _ = await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
    Xunit.Assert.Empty(playbackService.PlayedPaths);
    Xunit.Assert.False(session.IsPreparing);
    Xunit.Assert.False(session.IsPlaying);
    Xunit.Assert.False(session.CanStop);
  }

  [Xunit.Fact]
  public async Task ReadAsync_RejectsConcurrentPreparation()
  {
    ControlledSpeechService speechService = new();
    RecordingPlaybackService playbackService = new();
    await using WorkbenchSpeechSession session = new(() => speechService, playbackService);
    Task<TextToSpeechResult> active = session.ReadAsync("First request.");
    await speechService.Started.Task;

    _ = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
      () => session.ReadAsync("Concurrent request."));

    session.Stop();
    speechService.Completion.SetResult(CreateSpeech("cancelled.wav"));
    _ = await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
    Xunit.Assert.Empty(playbackService.PlayedPaths);
  }

  [Xunit.Fact]
  public async Task ReadAsync_ReusesTheServiceAndReplacesExistingPlayback()
  {
    RecordingSpeechService speechService = new(CreateSpeech("reused.wav"));
    RecordingPlaybackService playbackService = new();
    int factoryCalls = 0;
    await using WorkbenchSpeechSession session = new(
      () =>
      {
        factoryCalls++;
        return speechService;
      },
      playbackService);

    _ = await session.ReadAsync("First response.");
    _ = await session.ReadAsync("Second response.");

    Xunit.Assert.Equal(1, factoryCalls);
    Xunit.Assert.Equal(2, speechService.CallCount);
    Xunit.Assert.Equal(2, playbackService.PlayedPaths.Count);
    Xunit.Assert.True(session.IsPlaying);
  }

  [Xunit.Fact]
  public async Task ReadAsync_PlaybackFailureDoesNotReportPlayingState()
  {
    RecordingSpeechService speechService = new(CreateSpeech("missing.wav"));
    RecordingPlaybackService playbackService = new()
    {
      PlayException = new IOException("Playback failed."),
    };
    await using WorkbenchSpeechSession session = new(() => speechService, playbackService);

    _ = await Xunit.Assert.ThrowsAsync<IOException>(() => session.ReadAsync("Unreadable response."));

    Xunit.Assert.False(session.IsPreparing);
    Xunit.Assert.False(session.IsPlaying);
    Xunit.Assert.False(session.CanStop);
  }

  [Xunit.Fact]
  public async Task PlaybackEnded_ClearsPlayingStateAndNotifiesOwner()
  {
    RecordingPlaybackService playbackService = new();
    await using WorkbenchSpeechSession session = new(
      () => new RecordingSpeechService(CreateSpeech("completed.wav")),
      playbackService);
    int notificationCount = 0;
    session.PlaybackEnded += (_, _) => notificationCount++;

    _ = await session.ReadAsync("Read this response.");
    playbackService.CompletePlayback();

    Xunit.Assert.False(session.IsPlaying);
    Xunit.Assert.False(session.CanStop);
    Xunit.Assert.Equal(1, notificationCount);
  }

  [Xunit.Fact]
  public async Task PlaybackFailed_ClearsPlayingStateAndForwardsFailure()
  {
    RecordingPlaybackService playbackService = new();
    await using WorkbenchSpeechSession session = new(
      () => new RecordingSpeechService(CreateSpeech("failed.wav")),
      playbackService);
    InvalidOperationException failure = new("Audio device unavailable.");
    Exception? reportedFailure = null;
    session.PlaybackFailed += (_, eventArgs) => reportedFailure = eventArgs.Exception;

    _ = await session.ReadAsync("Read this response.");
    playbackService.FailPlayback(failure);

    Xunit.Assert.False(session.IsPlaying);
    Xunit.Assert.False(session.CanStop);
    Xunit.Assert.Same(failure, reportedFailure);
  }

  [Xunit.Fact]
  public async Task Stop_IgnoresAStalePlaybackCompletion()
  {
    RecordingPlaybackService playbackService = new();
    await using WorkbenchSpeechSession session = new(
      () => new RecordingSpeechService(CreateSpeech("stopped.wav")),
      playbackService);
    int notificationCount = 0;
    session.PlaybackEnded += (_, _) => notificationCount++;

    _ = await session.ReadAsync("Read this response.");
    session.Stop();
    playbackService.CompletePlayback();

    Xunit.Assert.False(session.IsPlaying);
    Xunit.Assert.Equal(0, notificationCount);
  }

  [Xunit.Fact]
  public async Task ResetAsync_WaitsForCancellationBeforeDisposingAndAllowsAReplacementService()
  {
    DelayedDisposableSpeechService firstService = new();
    RecordingSpeechService secondService = new(CreateSpeech("replacement.wav"));
    Queue<ITextToSpeechService> services = new([firstService, secondService]);
    RecordingPlaybackService playbackService = new();
    await using WorkbenchSpeechSession session = new(() => services.Dequeue(), playbackService);
    Task<TextToSpeechResult> preparation = session.ReadAsync("Cancel during reset.");
    await firstService.Started.Task;

    Task reset = session.ResetAsync();

    Xunit.Assert.False(reset.IsCompleted);
    Xunit.Assert.Equal(0, firstService.DisposeCount);
    firstService.Completion.SetResult(CreateSpeech("stale.wav"));
    _ = await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
    await reset;

    Xunit.Assert.Equal(1, firstService.DisposeCount);
    Xunit.Assert.Empty(playbackService.PlayedPaths);
    _ = await session.ReadAsync("Use the replacement service.");
    Xunit.Assert.Equal(1, secondService.CallCount);
    Xunit.Assert.Equal("replacement.wav", Xunit.Assert.Single(playbackService.PlayedPaths));
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsPreparationDisposesDependenciesAndRejectsFutureWork()
  {
    DelayedDisposableSpeechService speechService = new();
    RecordingPlaybackService playbackService = new();
    WorkbenchSpeechSession session = new(() => speechService, playbackService);
    Task<TextToSpeechResult> preparation = session.ReadAsync("Dispose safely.");
    await speechService.Started.Task;

    ValueTask disposal = session.DisposeAsync();

    Xunit.Assert.False(disposal.IsCompleted);
    speechService.Completion.SetResult(CreateSpeech("disposed.wav"));
    _ = await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
    await disposal;

    Xunit.Assert.Equal(1, speechService.DisposeCount);
    Xunit.Assert.Equal(1, playbackService.DisposeCount);
    Xunit.Assert.Empty(playbackService.PlayedPaths);
    _ = await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReadAsync("Too late."));
    await session.DisposeAsync();
  }

  private static TextToSpeechResult CreateSpeech(string audioPath) => new(
    audioPath,
    TimeSpan.FromSeconds(2),
    1,
    "test-provider",
    "test-model");

  private sealed class RecordingSpeechService(TextToSpeechResult result) : ITextToSpeechService
  {
    public int CallCount { get; private set; }

    public List<TextToSpeechRequest> Requests { get; } = [];

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      CallCount++;
      Requests.Add(request);
      return Task.FromResult(result);
    }
  }

  private sealed class ThrowingSpeechService(Exception failure) : ITextToSpeechService
  {
    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default) => Task.FromException<TextToSpeechResult>(failure);
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<(string Message, Exception? Exception)> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public void Info(string message) { }

    public void Warning(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null) => Errors.Add((message, exception));
  }

  private sealed class ControlledSpeechService : ITextToSpeechService
  {
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<TextToSpeechResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      Started.TrySetResult();
      return Completion.Task;
    }
  }

  private sealed class DelayedDisposableSpeechService : ITextToSpeechService, IAsyncDisposable
  {
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<TextToSpeechResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int DisposeCount { get; private set; }

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      Started.TrySetResult();
      return Completion.Task;
    }

    public ValueTask DisposeAsync()
    {
      DisposeCount++;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class RecordingPlaybackService : IWavAudioPlaybackService
  {
    public event EventHandler? PlaybackEnded;

    public event EventHandler<WavAudioPlaybackFailedEventArgs>? PlaybackFailed;

    public List<string> PlayedPaths { get; } = [];

    public Exception? PlayException { get; init; }

    public int StopCount { get; private set; }

    public int DisposeCount { get; private set; }

    public void Play(string audioPath)
    {
      if (PlayException is not null)
      {
        throw PlayException;
      }

      PlayedPaths.Add(audioPath);
    }

    public void Stop()
    {
      StopCount++;
    }

    public void Dispose()
    {
      DisposeCount++;
    }

    public void CompletePlayback()
    {
      PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public void FailPlayback(Exception exception)
    {
      PlaybackFailed?.Invoke(this, new WavAudioPlaybackFailedEventArgs(exception));
    }
  }
}
