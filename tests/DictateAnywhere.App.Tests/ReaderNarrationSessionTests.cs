using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Each ReaderNarrationSession under test takes ownership of its fake alignment service.")]
public sealed class ReaderNarrationSessionTests : IDisposable
{
  private readonly string audioDirectory = Path.Combine(
    Path.GetTempPath(),
    $"notype-reader-session-{Guid.NewGuid():N}");

  public ReaderNarrationSessionTests()
  {
    Directory.CreateDirectory(audioDirectory);
  }

  [Xunit.Fact]
  public async Task GetSpeechAsync_ComposesOneProviderRequestAndReusesItsAudio()
  {
    string audioPath = CreateAudioFile("narration.wav");
    RecordingSpeechService speechService = new(audioPath);
    RecordingAlignmentService alignmentService = new([]);
    await using ReaderNarrationSession session = new(speechService, alignmentService);
    ReadingSection section = ReadingTextLayout.Create(
      "Stories",
      [new ReadableDocumentSection("The Clever Fox", "A fox lived beside a forest.")]).Sections[0];
    ReaderNarrationProfile profile = CreateProfile();

    TextToSpeechResult first = await session.GetSpeechAsync(section, profile);
    TextToSpeechResult second = await session.GetSpeechAsync(section, profile);

    Xunit.Assert.Same(first, second);
    Xunit.Assert.Equal(1, speechService.CallCount);
    TextToSpeechRequest request = Xunit.Assert.Single(speechService.Requests);
    Xunit.Assert.Equal("The Clever Fox.\nA fox lived beside a forest.", request.Text);
    Xunit.Assert.Equal("en-us", request.Language);
    Xunit.Assert.Equal("kokoro-local", request.ProviderId);
    Xunit.Assert.Equal("af_bella", request.VoiceId);
    Xunit.Assert.Equal("Warm narration", request.Description);
  }

  [Xunit.Fact]
  public async Task GetSpeechAsync_DeduplicatesConcurrentRequestsForTheSameSection()
  {
    string audioPath = CreateAudioFile("concurrent.wav");
    GatedSpeechService speechService = new(audioPath);
    await using ReaderNarrationSession session = new(speechService, new RecordingAlignmentService([]));
    ReadingSection section = CreateSection("One two three.");
    ReaderNarrationProfile profile = CreateProfile();

    Task<TextToSpeechResult> first = session.GetSpeechAsync(section, profile);
    await speechService.WaitUntilCalledAsync();
    Task<TextToSpeechResult> second = session.GetSpeechAsync(section, profile);
    speechService.Release();

    TextToSpeechResult[] results = await Task.WhenAll(first, second);
    Xunit.Assert.Equal(1, speechService.CallCount);
    Xunit.Assert.Same(results[0], results[1]);
  }

  [Xunit.Fact]
  public async Task GetSpeechAsync_DoesNotReuseAudioForChangedTextOrVoice()
  {
    RecordingSpeechService speechService = new(CreateAudioFile("identity.wav"));
    await using ReaderNarrationSession session = new(speechService, new RecordingAlignmentService([]));
    ReadingSection original = CreateSection("Original words.");
    ReadingSection edited = CreateSection("Edited words.");

    _ = await session.GetSpeechAsync(original, CreateProfile());
    _ = await session.GetSpeechAsync(edited, CreateProfile());
    _ = await session.GetSpeechAsync(
      edited,
      new ReaderNarrationProfile(
        "en-us",
        "kokoro-local",
        "am_adam",
        "Warm narration",
        ReaderWordTimingStrategy.NativeWithDeterministicFallback));

    Xunit.Assert.Equal(3, speechService.CallCount);
  }

  [Xunit.Fact]
  public async Task GetTimingAsync_ReusesOnlyTimingsForTheSameAudioAndTranscript()
  {
    RecordingAlignmentService alignmentService = new(
      [
        new SpeechWordTiming("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(0.8)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.8)),
      ]);
    await using ReaderNarrationSession session = new(
      new RecordingSpeechService(CreateAudioFile("unused.wav")),
      alignmentService);
    ReadingSection section = CreateSection("Hello world.");
    ReaderNarrationProfile profile = CreateProfile(ReaderWordTimingStrategy.LocalForcedAlignment);
    TextToSpeechResult firstSpeech = CreateSpeech("first.wav");
    TextToSpeechResult secondSpeech = CreateSpeech("second.wav");

    ReaderWordTimingMap first = await session.GetTimingAsync(section, profile, firstSpeech);
    ReaderWordTimingMap cached = await session.GetTimingAsync(section, profile, firstSpeech);
    ReaderWordTimingMap second = await session.GetTimingAsync(section, profile, secondSpeech);

    Xunit.Assert.Same(first, cached);
    Xunit.Assert.NotSame(first, second);
    Xunit.Assert.Equal(2, alignmentService.CallCount);
    Xunit.Assert.All(alignmentService.Requests, request => Xunit.Assert.Equal("Hello world.", request.Transcript));
  }

  [Xunit.Fact]
  public async Task GetTimingAsync_WhenAudioFileIsDeleted_EvictsTimingAndRecomputes()
  {
    RecordingAlignmentService alignmentService = new(
      [
        new SpeechWordTiming("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(0.8)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.8)),
      ]);
    await using ReaderNarrationSession session = new(
      new RecordingSpeechService(CreateAudioFile("unused.wav")),
      alignmentService);
    ReadingSection section = CreateSection("Hello world.");
    ReaderNarrationProfile profile = CreateProfile(ReaderWordTimingStrategy.LocalForcedAlignment);
    string audioPath = CreateAudioFile("temp-to-delete.wav");
    TextToSpeechResult speech = new(audioPath, TimeSpan.FromSeconds(2), 1, "test-provider", "test-model");

    ReaderWordTimingMap first = await session.GetTimingAsync(section, profile, speech);
    Xunit.Assert.Equal(1, alignmentService.CallCount);

    File.Delete(audioPath); // Corrupt/delete the underlying audio

    ReaderWordTimingMap recomputed = await session.GetTimingAsync(section, profile, speech);
    Xunit.Assert.NotSame(first, recomputed);
    Xunit.Assert.Equal(2, alignmentService.CallCount); // Recomputed!
  }

  [Xunit.Fact]
  public async Task Clear_InvalidatesNarrationAndTimingTogether()
  {
    RecordingSpeechService speechService = new(CreateAudioFile("clear.wav"));
    RecordingAlignmentService alignmentService = new(
      [
        new SpeechWordTiming("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(0.8)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.8)),
      ]);
    await using ReaderNarrationSession session = new(speechService, alignmentService);
    ReadingSection section = CreateSection("Hello world.");
    ReaderNarrationProfile profile = CreateProfile(ReaderWordTimingStrategy.LocalForcedAlignment);
    TextToSpeechResult speech = await session.GetSpeechAsync(section, profile);
    _ = await session.GetTimingAsync(section, profile, speech);

    session.Clear();
    _ = await session.GetSpeechAsync(section, profile);
    _ = await session.GetTimingAsync(section, profile, speech);

    Xunit.Assert.Equal(2, speechService.CallCount);
    Xunit.Assert.Equal(2, alignmentService.CallCount);
  }

  [Xunit.Fact]
  public async Task Clear_DoesNotLetInFlightNarrationRepopulateThePreviousGeneration()
  {
    GatedSpeechService speechService = new(CreateAudioFile("generation.wav"));
    await using ReaderNarrationSession session = new(speechService, new RecordingAlignmentService([]));
    ReadingSection section = CreateSection("Do not restore stale work.");
    ReaderNarrationProfile profile = CreateProfile();

    Task<TextToSpeechResult> inFlight = session.GetSpeechAsync(section, profile);
    await speechService.WaitUntilCalledAsync();
    session.Clear();
    speechService.Release();
    _ = await inFlight;
    _ = await session.GetSpeechAsync(section, profile);

    Xunit.Assert.Equal(2, speechService.CallCount);
  }

  [Xunit.Fact]
  public async Task GetSpeechAsync_ReleasesSerializationAfterCancellation()
  {
    CancelOnceSpeechService speechService = new(CreateAudioFile("retry.wav"));
    await using ReaderNarrationSession session = new(speechService, new RecordingAlignmentService([]));
    ReadingSection section = CreateSection("Retry safely.");
    ReaderNarrationProfile profile = CreateProfile();
    using CancellationTokenSource cancellation = new();

    Task<TextToSpeechResult> cancelled = session.GetSpeechAsync(section, profile, cancellation.Token);
    await speechService.WaitUntilCalledAsync();
    cancellation.Cancel();
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

    TextToSpeechResult recovered = await session.GetSpeechAsync(section, profile);

    Xunit.Assert.Equal(2, speechService.CallCount);
    Xunit.Assert.EndsWith("retry.wav", recovered.AudioPath, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_DisposesOwnedAlignmentButNotBorrowedSpeechService()
  {
    DisposableSpeechService speechService = new(CreateAudioFile("dispose.wav"));
    RecordingAlignmentService alignmentService = new([]);
    ReaderNarrationSession session = new(speechService, alignmentService);

    await session.DisposeAsync();
    await session.DisposeAsync();

    Xunit.Assert.Equal(1, alignmentService.DisposeCount);
    Xunit.Assert.Equal(0, speechService.DisposeCount);
    Xunit.Assert.Throws<ObjectDisposedException>(() => session.Clear());
    await speechService.DisposeAsync();
  }

  public void Dispose()
  {
    if (Directory.Exists(audioDirectory))
    {
      Directory.Delete(audioDirectory, recursive: true);
    }
  }

  private ReadingSection CreateSection(string text) => ReadingTextLayout.Create("Test", text).Sections[0];

  private ReaderNarrationProfile CreateProfile(
    ReaderWordTimingStrategy strategy = ReaderWordTimingStrategy.NativeWithDeterministicFallback) => new(
      "en-us",
      "kokoro-local",
      "af_bella",
      "Warm narration",
      strategy);

  private string CreateAudioFile(string name)
  {
    string path = Path.Combine(audioDirectory, name);
    File.WriteAllBytes(path, new byte[64]);
    return path;
  }

  private TextToSpeechResult CreateSpeech(string name) => new(
    CreateAudioFile(name),
    TimeSpan.FromSeconds(2),
    1,
    "test-provider",
    "test-model");

  private class RecordingSpeechService(string audioPath) : ITextToSpeechService
  {
    private int callCount;

    public int CallCount => callCount;

    public ConcurrentQueue<TextToSpeechRequest> Requests { get; } = new();

    public virtual Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Interlocked.Increment(ref callCount);
      Requests.Enqueue(request);
      return Task.FromResult(CreateResult());
    }

    protected TextToSpeechResult CreateResult() => new(
      audioPath,
      TimeSpan.FromSeconds(2),
      1,
      "test-provider",
      "test-model");
  }

  private sealed class DisposableSpeechService(string audioPath) : RecordingSpeechService(audioPath), IAsyncDisposable
  {
    public int DisposeCount { get; private set; }

    public ValueTask DisposeAsync()
    {
      DisposeCount++;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class GatedSpeechService(string audioPath) : ITextToSpeechService
  {
    private readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public async Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      called.TrySetResult();
      await released.Task.WaitAsync(cancellationToken);
      return new TextToSpeechResult(audioPath, TimeSpan.FromSeconds(2), 1, "test-provider", "test-model");
    }

    public Task WaitUntilCalledAsync() => called.Task;

    public void Release() => released.TrySetResult();
  }

  private sealed class CancelOnceSpeechService(string audioPath) : ITextToSpeechService
  {
    private readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public async Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      if (CallCount == 1)
      {
        called.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      }

      return new TextToSpeechResult(audioPath, TimeSpan.FromSeconds(2), 1, "test-provider", "test-model");
    }

    public Task WaitUntilCalledAsync() => called.Task;
  }

  private sealed class RecordingAlignmentService(IReadOnlyList<SpeechWordTiming> words) : ISpeechAlignmentService
  {
    public int CallCount { get; private set; }

    public int DisposeCount { get; private set; }

    public List<SpeechAlignmentRequest> Requests { get; } = [];

    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      CallCount++;
      Requests.Add(request);
      return Task.FromResult(new SpeechAlignmentResult(words, "test-aligner", TimeSpan.Zero));
    }

    public ValueTask DisposeAsync()
    {
      DisposeCount++;
      return ValueTask.CompletedTask;
    }
  }
}
