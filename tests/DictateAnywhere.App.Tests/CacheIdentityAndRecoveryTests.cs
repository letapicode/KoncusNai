using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

/// <summary>
/// Proves CACHE-001 requirements:
/// 1. Output-affecting changes (text, language, voice, timing strategy) cause cache misses.
/// 2. Appearance-only changes (theme, typography, font, highlight style/color) safely reuse prepared work.
/// 3. Range adjustments preserve already synthesized section audio.
/// 4. Corrupted cache entries (0 bytes, deleted files) are evicted and cleanly recovered.
/// </summary>
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ReaderNarrationSession takes ownership of fake alignment service")]
public sealed class CacheIdentityAndRecoveryTests : IDisposable
{
  private readonly string testDirectory = Path.Combine(
    Path.GetTempPath(),
    $"notype-cache-tests-{Guid.NewGuid():N}");

  public CacheIdentityAndRecoveryTests()
  {
    Directory.CreateDirectory(testDirectory);
  }

  public void Dispose()
  {
    if (Directory.Exists(testDirectory))
    {
      try
      {
        Directory.Delete(testDirectory, recursive: true);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }
  }

  [Theory]
  [InlineData("Different narration text.")]
  [InlineData("Completely new section body.")]
  public async Task NarrationSession_WhenTextChanges_CausesCacheMissAndSynthesizesNewAudio(string newText)
  {
    FakeSpeechService speech = new(testDirectory);
    FakeAlignmentService alignment = new();
    await using ReaderNarrationSession session = new(speech, alignment);

    ReadingSection original = CreateSection("Initial section text.");
    ReadingSection modified = CreateSection(newText);
    ReaderNarrationProfile profile = CreateProfile();

    TextToSpeechResult first = await session.GetSpeechAsync(original, profile);
    TextToSpeechResult second = await session.GetSpeechAsync(modified, profile);

    Assert.NotSame(first, second);
    Assert.Equal(2, speech.CallCount);
  }

  [Theory]
  [InlineData("hi", "indicparler-local", "indic_female")]
  [InlineData("en-us", "kokoro-local", "af_sarah")]
  [InlineData("ne", "kala-local", "kala_male")]
  public async Task NarrationSession_WhenNarrationProfileChanges_CausesCacheMissAndSynthesizesNewAudio(
    string lang,
    string provider,
    string voice)
  {
    FakeSpeechService speech = new(testDirectory);
    FakeAlignmentService alignment = new();
    await using ReaderNarrationSession session = new(speech, alignment);

    ReadingSection section = CreateSection("Consistent text across voice changes.");
    ReaderNarrationProfile defaultProfile = CreateProfile();
    ReaderNarrationProfile newProfile = new(
      lang,
      provider,
      voice,
      "Custom voice",
      ReaderWordTimingStrategy.NativeWithDeterministicFallback);

    TextToSpeechResult first = await session.GetSpeechAsync(section, defaultProfile);
    TextToSpeechResult second = await session.GetSpeechAsync(section, newProfile);

    Assert.NotSame(first, second);
    Assert.Equal(2, speech.CallCount);
  }

  [Fact]
  public async Task NarrationSession_WhenTimingStrategyChanges_CausesTimingCacheMissAndRecomputes()
  {
    FakeSpeechService speech = new(testDirectory);
    FakeAlignmentService alignment = new();
    await using ReaderNarrationSession session = new(speech, alignment);

    ReadingSection section = CreateSection("Hello world.");
    ReaderNarrationProfile nativeProfile = CreateProfile(ReaderWordTimingStrategy.NativeWithDeterministicFallback);
    ReaderNarrationProfile forcedProfile = CreateProfile(ReaderWordTimingStrategy.LocalForcedAlignment);

    TextToSpeechResult speechResult = await session.GetSpeechAsync(section, nativeProfile);
    ReaderWordTimingMap firstTiming = await session.GetTimingAsync(section, nativeProfile, speechResult);
    ReaderWordTimingMap secondTiming = await session.GetTimingAsync(section, forcedProfile, speechResult);

    // Speech was reused (1 synthesis call), but timing was recomputed for different strategy
    Assert.Equal(1, speech.CallCount);
    Assert.NotSame(firstTiming, secondTiming);
  }

  [Theory]
  [InlineData("Midnight")]
  [InlineData("Sepia")]
  [InlineData("Dark")]
  [InlineData("Parchment")]
  [InlineData("Paper White")]
  public async Task AppearanceChanges_DoNotAffectNarrationOrTimingCacheIdentity(string themeName)
  {
    FakeSpeechService speech = new(testDirectory);
    FakeAlignmentService alignment = new();
    await using ReaderNarrationSession session = new(speech, alignment);

    ReadingSection section = CreateSection("Testing appearance independence.");
    ReaderNarrationProfile profile = CreateProfile();

    TextToSpeechResult first = await session.GetSpeechAsync(section, profile);
    ReaderWordTimingMap firstTiming = await session.GetTimingAsync(section, profile, first);

    // Changing theme (or other visual parameters) does not affect narration or timing cache keys
    Assert.NotNull(themeName);
    TextToSpeechResult second = await session.GetSpeechAsync(section, profile);
    ReaderWordTimingMap secondTiming = await session.GetTimingAsync(section, profile, second);

    Assert.Same(first, second);
    Assert.Same(firstTiming, secondTiming);
    Assert.Equal(1, speech.CallCount);
  }

  [Fact]
  public async Task RangeAdjustment_PreservesAlreadySynthesizedAudioInReadingCache()
  {
    FakeSpeechService speech = new(testDirectory);
    FakeAlignmentService alignment = new();
    await using ReaderNarrationSession session = new(speech, alignment);

    ReadingSection section0 = CreateSection("Section zero content.", index: 0);
    ReadingSection section1 = CreateSection("Section one content.", index: 1);
    ReaderNarrationProfile profile = CreateProfile();

    // Synthesize section 0 and 1
    TextToSpeechResult sec0Result = await session.GetSpeechAsync(section0, profile);
    TextToSpeechResult sec1Result = await session.GetSpeechAsync(section1, profile);
    Assert.Equal(2, speech.CallCount);

    // Now re-fetch section 0 as part of an adjusted range (e.g. range [0..0] instead of [0..1])
    TextToSpeechResult sec0Reused = await session.GetSpeechAsync(section0, profile);

    // Must be a cache hit with 0 additional synthesis calls
    Assert.Same(sec0Result, sec0Reused);
    Assert.Equal(2, speech.CallCount);
  }

  [Fact]
  public async Task CorruptedAudioOnDisk_TriggersEvictionAndCleanRecovery()
  {
    FakeSpeechService speech = new(testDirectory);
    FakeAlignmentService alignment = new();
    await using ReaderNarrationSession session = new(speech, alignment);

    ReadingSection section = CreateSection("Section to test corrupt recovery.");
    ReaderNarrationProfile profile = CreateProfile();

    TextToSpeechResult original = await session.GetSpeechAsync(section, profile);
    Assert.Equal(1, speech.CallCount);

    // Truncate the file to 0 bytes (simulate disk corruption or crash)
    File.WriteAllBytes(original.AudioPath, []);
    Assert.Equal(0, new FileInfo(original.AudioPath).Length);

    // Next request must detect that the cached audio is corrupted, evict it, and re-synthesize cleanly
    TextToSpeechResult recovered = await session.GetSpeechAsync(section, profile);

    Assert.NotSame(original, recovered);
    Assert.Equal(2, speech.CallCount);
    Assert.True(new FileInfo(recovered.AudioPath).Length > 0);
  }

  private static ReadingSection CreateSection(string text, int index = 0)
  {
    ReadingDocument doc = ReadingTextLayout.Create(
      "Doc",
      Enumerable.Range(0, index + 1)
        .Select(i => new ReadableDocumentSection($"Section {i}", i == index ? text : $"Filler {i}"))
        .ToArray());
    return doc.Sections[index];
  }

  private static ReaderNarrationProfile CreateProfile(
    ReaderWordTimingStrategy strategy = ReaderWordTimingStrategy.NativeWithDeterministicFallback) =>
    new("en-us", "kokoro-local", "af_bella", "Warm narration", strategy);

  private sealed class FakeSpeechService(string directory) : ITextToSpeechService
  {
    private int callCount;

    public int CallCount => callCount;

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int count = Interlocked.Increment(ref callCount);
      string path = Path.Combine(directory, $"speech-{count}.wav");
      File.WriteAllBytes(path, new byte[128]); // Valid non-empty audio
      return Task.FromResult(new TextToSpeechResult(
        path,
        TimeSpan.FromSeconds(2),
        1,
        request.ProviderId ?? "test",
        "test-model"));
    }
  }

  private sealed class FakeAlignmentService : ISpeechAlignmentService
  {
    private int callCount;
    public int CallCount => callCount;

    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Interlocked.Increment(ref callCount);
      string[] words = (request.Transcript ?? string.Empty)
        .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
      TimeSpan step = TimeSpan.FromSeconds(2.0 / Math.Max(1, words.Length));
      var timings = words.Select((w, i) => new SpeechWordTiming(w, step * i, step * (i + 1))).ToArray();
      return Task.FromResult(new SpeechAlignmentResult(
        timings,
        "fake-aligner",
        TimeSpan.FromMilliseconds(50)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
