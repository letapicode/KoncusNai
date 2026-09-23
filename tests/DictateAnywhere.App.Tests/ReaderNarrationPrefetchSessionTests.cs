using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Each ReaderNarrationSession under test takes ownership of its fake alignment service.")]
public sealed class ReaderNarrationPrefetchSessionTests : IDisposable
{
  private readonly string audioDirectory = Path.Combine(
    Path.GetTempPath(),
    $"notype-reader-prefetch-{Guid.NewGuid():N}");

  public ReaderNarrationPrefetchSessionTests()
  {
    Directory.CreateDirectory(audioDirectory);
  }

  [Xunit.Fact]
  public async Task Begin_PreparesNarrationAndTimingForTheExactSectionAndProfile()
  {
    ImmediateSpeechService speechService = new(CreateAudioFile("prepared.wav"));
    await using ReaderNarrationSession narrationSession = new(speechService, new UnusedAlignmentService());
    await using ReaderNarrationPrefetchSession prefetchSession = new(narrationSession);
    ReadingSection section = CreateSection("One two three.");
    ReaderNarrationProfile profile = CreateProfile();

    bool started = prefetchSession.Begin(section, profile);
    bool claimed = prefetchSession.TryTake(section, profile, out Task<TextToSpeechResult>? speechTask);
    TextToSpeechResult speech = await Xunit.Assert.IsAssignableFrom<Task<TextToSpeechResult>>(speechTask);

    Xunit.Assert.True(started);
    Xunit.Assert.True(claimed);
    Xunit.Assert.Equal(1, speechService.CallCount);
    Xunit.Assert.True(narrationSession.TryGetSpeech(section, profile, out TextToSpeechResult? cachedSpeech));
    Xunit.Assert.Same(speech, cachedSpeech);
    Xunit.Assert.True(narrationSession.TryGetTiming(section, profile, speech, out ReaderWordTimingMap? timing));
    Xunit.Assert.NotNull(timing);
    Xunit.Assert.False(prefetchSession.TryTake(section, profile, out _));
  }

  [Xunit.Fact]
  public async Task Begin_DoesNotDuplicateAnExistingBufferForTheSameIdentity()
  {
    ImmediateSpeechService speechService = new(CreateAudioFile("single.wav"));
    await using ReaderNarrationSession narrationSession = new(speechService, new UnusedAlignmentService());
    await using ReaderNarrationPrefetchSession prefetchSession = new(narrationSession);
    ReadingSection section = CreateSection("Prepare this once.");
    ReaderNarrationProfile profile = CreateProfile();

    Xunit.Assert.True(prefetchSession.Begin(section, profile));
    Xunit.Assert.False(prefetchSession.Begin(section, profile));
    Xunit.Assert.True(prefetchSession.TryTake(section, profile, out Task<TextToSpeechResult>? speechTask));
    _ = await Xunit.Assert.IsAssignableFrom<Task<TextToSpeechResult>>(speechTask);

    Xunit.Assert.Equal(1, speechService.CallCount);
  }

  [Xunit.Fact]
  public async Task Begin_ReplacingAClaimedBufferCancelsItsBackgroundWork()
  {
    CancelFirstSpeechService speechService = new(CreateAudioFile("replacement.wav"));
    await using ReaderNarrationSession narrationSession = new(speechService, new UnusedAlignmentService());
    await using ReaderNarrationPrefetchSession prefetchSession = new(narrationSession);
    ReadingSection firstSection = CreateSection("First section.");
    ReadingSection secondSection = CreateSection("Second section.");
    ReaderNarrationProfile profile = CreateProfile();

    Xunit.Assert.True(prefetchSession.Begin(firstSection, profile));
    Xunit.Assert.True(prefetchSession.TryTake(firstSection, profile, out Task<TextToSpeechResult>? firstTask));
    await speechService.FirstCallStarted;

    Xunit.Assert.True(prefetchSession.Begin(secondSection, profile));
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => Xunit.Assert.IsAssignableFrom<Task<TextToSpeechResult>>(firstTask));
    Xunit.Assert.True(prefetchSession.TryTake(secondSection, profile, out Task<TextToSpeechResult>? secondTask));
    _ = await Xunit.Assert.IsAssignableFrom<Task<TextToSpeechResult>>(secondTask);

    Xunit.Assert.Equal(2, speechService.CallCount);
  }

  [Xunit.Fact]
  public async Task TryTake_RejectsDifferentSectionsAndNarrationProfiles()
  {
    ImmediateSpeechService speechService = new(CreateAudioFile("identity.wav"));
    await using ReaderNarrationSession narrationSession = new(speechService, new UnusedAlignmentService());
    await using ReaderNarrationPrefetchSession prefetchSession = new(narrationSession);
    ReadingSection section = CreateSection("Identity matters.");
    ReaderNarrationProfile profile = CreateProfile();

    _ = prefetchSession.Begin(section, profile);

    Xunit.Assert.False(prefetchSession.TryTake(CreateSection("Identity matters."), profile, out _));
    Xunit.Assert.False(prefetchSession.TryTake(
      section,
      CreateProfile("am_adam"),
      out _));
    Xunit.Assert.True(prefetchSession.TryTake(section, profile, out Task<TextToSpeechResult>? speechTask));
    _ = await Xunit.Assert.IsAssignableFrom<Task<TextToSpeechResult>>(speechTask);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsAndWaitsForTheBufferAndRejectsNewWork()
  {
    NeverCompletingSpeechService speechService = new();
    await using ReaderNarrationSession narrationSession = new(speechService, new UnusedAlignmentService());
    ReaderNarrationPrefetchSession prefetchSession = new(narrationSession);
    ReadingSection section = CreateSection("Cancel on disposal.");
    ReaderNarrationProfile profile = CreateProfile();

    _ = prefetchSession.Begin(section, profile);
    Xunit.Assert.True(prefetchSession.TryTake(section, profile, out Task<TextToSpeechResult>? speechTask));
    await speechService.CallStarted;

    await prefetchSession.DisposeAsync();
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => Xunit.Assert.IsAssignableFrom<Task<TextToSpeechResult>>(speechTask));
    _ = Xunit.Assert.Throws<ObjectDisposedException>(() => prefetchSession.Begin(section, profile));
  }

  public void Dispose()
  {
    if (Directory.Exists(audioDirectory))
    {
      Directory.Delete(audioDirectory, recursive: true);
    }
  }

  private ReadingSection CreateSection(string text) => ReadingTextLayout.Create("Test", text).Sections[0];

  private static ReaderNarrationProfile CreateProfile(string voiceId = "af_bella") => new(
    "en-us",
    "kokoro-local",
    voiceId,
    "Warm narration",
    ReaderWordTimingStrategy.NativeWithDeterministicFallback);

  private string CreateAudioFile(string name)
  {
    string path = Path.Combine(audioDirectory, name);
    File.WriteAllBytes(path, new byte[64]);
    return path;
  }

  private sealed class ImmediateSpeechService(string audioPath) : ITextToSpeechService
  {
    public int CallCount { get; private set; }

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      CallCount++;
      return Task.FromResult(CreateSpeech(audioPath));
    }
  }

  private sealed class CancelFirstSpeechService(string audioPath) : ITextToSpeechService
  {
    private readonly TaskCompletionSource firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstCallStarted => firstCallStarted.Task;

    public int CallCount { get; private set; }

    public async Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      if (CallCount == 1)
      {
        firstCallStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      }

      return CreateSpeech(audioPath);
    }
  }

  private sealed class NeverCompletingSpeechService : ITextToSpeechService
  {
    private readonly TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task CallStarted => callStarted.Task;

    public async Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      callStarted.TrySetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      throw new InvalidOperationException("Unreachable after cancellation.");
    }
  }

  private sealed class UnusedAlignmentService : ISpeechAlignmentService
  {
    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromException<SpeechAlignmentResult>(new InvalidOperationException("Deterministic fallback should not align speech."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private static TextToSpeechResult CreateSpeech(string audioPath) => new(
    audioPath,
    TimeSpan.FromSeconds(2),
    1,
    "test-provider",
    "test-model");
}
