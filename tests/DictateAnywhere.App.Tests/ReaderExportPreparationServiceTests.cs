using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Each ReaderNarrationSession under test takes ownership of its fake alignment service.")]
public sealed class ReaderExportPreparationServiceTests : IDisposable
{
  private readonly string audioDirectory = Path.Combine(
    Path.GetTempPath(),
    $"notype-reader-export-preparation-{Guid.NewGuid():N}");

  public ReaderExportPreparationServiceTests()
  {
    Directory.CreateDirectory(audioDirectory);
  }

  [Xunit.Fact]
  public async Task PrepareAudioAsync_PreservesSectionOrderAndReusesPreparedNarration()
  {
    RecordingSpeechService speechService = new(audioDirectory);
    await using ReaderNarrationSession session = new(speechService, new RecordingAlignmentService([]));
    ReaderExportPreparationService service = new(session);
    IReadOnlyList<ReadingSection> sections = CreateSections("First section.", "Second section.");
    ReaderNarrationProfile profile = CreateProfile();
    TextToSpeechResult cached = await session.GetSpeechAsync(sections[0], profile);
    List<ReaderExportPreparationProgress> updates = [];

    IReadOnlyList<TextToSpeechResult> prepared = await service.PrepareAudioAsync(
      sections,
      profile,
      new InlineProgress<ReaderExportPreparationProgress>(updates.Add));

    Xunit.Assert.Equal(2, prepared.Count);
    Xunit.Assert.Same(cached, prepared[0]);
    Xunit.Assert.Contains("second-section", prepared[1].AudioPath, StringComparison.Ordinal);
    Xunit.Assert.Equal(2, speechService.CallCount);
    Xunit.Assert.Collection(
      updates,
      update => AssertUpdate(update, 0, 2, ReaderExportPreparationPhase.Narration, isCached: true),
      update => AssertUpdate(update, 0, 2, ReaderExportPreparationPhase.Complete, isCached: false),
      update => AssertUpdate(update, 1, 2, ReaderExportPreparationPhase.Narration, isCached: false),
      update => AssertUpdate(update, 1, 2, ReaderExportPreparationPhase.Complete, isCached: false));
  }

  [Xunit.Fact]
  public async Task PrepareVideoAsync_ProducesCompleteTimedSectionsAndTypedPhaseProgress()
  {
    RecordingSpeechService speechService = new(audioDirectory);
    RecordingAlignmentService alignmentService = new(
      [
        new SpeechWordTiming("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(0.7)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(0.7), TimeSpan.FromSeconds(1.8)),
      ]);
    await using ReaderNarrationSession session = new(speechService, alignmentService);
    ReaderExportPreparationService service = new(session);
    ReadingSection section = CreateSections("Hello world.")[0];
    ReaderNarrationProfile profile = CreateProfile(ReaderWordTimingStrategy.LocalForcedAlignment);
    List<ReaderExportPreparationProgress> updates = [];

    IReadOnlyList<ReaderVideoExportSection> prepared = await service.PrepareVideoAsync(
      [section],
      profile,
      ReaderTextDirection.LeftToRight,
      new InlineProgress<ReaderExportPreparationProgress>(updates.Add));

    ReaderVideoExportSection videoSection = Xunit.Assert.Single(prepared);
    Xunit.Assert.Equal(section.Words, videoSection.Words);
    Xunit.Assert.Equal(section.Words.Count, videoSection.Timings.Count);
    Xunit.Assert.NotNull(videoSection.ParagraphDirections);
    Xunit.Assert.Equal(ReaderTextDirection.LeftToRight, Xunit.Assert.Single(videoSection.ParagraphDirections));
    Xunit.Assert.Equal(1, speechService.CallCount);
    Xunit.Assert.Equal(1, alignmentService.CallCount);
    Xunit.Assert.Collection(
      updates,
      update => AssertUpdate(update, 0, 1, ReaderExportPreparationPhase.Narration, isCached: false),
      update => AssertUpdate(update, 0, 1, ReaderExportPreparationPhase.Timing, isCached: false),
      update => AssertUpdate(update, 0, 1, ReaderExportPreparationPhase.Complete, isCached: false));
  }

  [Xunit.Fact]
  public async Task PrepareVideoSectionAsync_ReusesBothPreparedStages()
  {
    RecordingSpeechService speechService = new(audioDirectory);
    RecordingAlignmentService alignmentService = new(
      [
        new SpeechWordTiming("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(0.7)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(0.7), TimeSpan.FromSeconds(1.8)),
      ]);
    await using ReaderNarrationSession session = new(speechService, alignmentService);
    ReaderExportPreparationService service = new(session);
    ReadingSection section = CreateSections("Hello world.")[0];
    ReaderNarrationProfile profile = CreateProfile(ReaderWordTimingStrategy.LocalForcedAlignment);
    _ = await service.PrepareVideoSectionAsync(section, profile, ReaderTextDirection.LeftToRight);
    List<ReaderExportPreparationProgress> updates = [];

    _ = await service.PrepareVideoSectionAsync(
      section,
      profile,
      ReaderTextDirection.LeftToRight,
      new InlineProgress<ReaderExportPreparationProgress>(updates.Add));

    Xunit.Assert.Equal(1, speechService.CallCount);
    Xunit.Assert.Equal(1, alignmentService.CallCount);
    Xunit.Assert.True(updates.Single(update => update.Phase == ReaderExportPreparationPhase.Narration).IsCached);
    Xunit.Assert.True(updates.Single(update => update.Phase == ReaderExportPreparationPhase.Timing).IsCached);
  }

  [Xunit.Fact]
  public async Task PrepareAudioAsync_StopsBeforeLaterSectionsAfterCancellation()
  {
    CancelOnSecondSpeechService speechService = new(audioDirectory);
    await using ReaderNarrationSession session = new(speechService, new RecordingAlignmentService([]));
    ReaderExportPreparationService service = new(session);
    IReadOnlyList<ReadingSection> sections = CreateSections("First section.", "Second section.", "Third section.");

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAudioAsync(
      sections,
      CreateProfile(),
      cancellationToken: CancellationToken.None));

    Xunit.Assert.Equal(2, speechService.CallCount);
  }

  [Xunit.Fact]
  public async Task PrepareAudioAsync_RejectsAnEmptySelection()
  {
    await using ReaderNarrationSession session = new(
      new RecordingSpeechService(audioDirectory),
      new RecordingAlignmentService([]));
    ReaderExportPreparationService service = new(session);

    await Xunit.Assert.ThrowsAsync<ArgumentException>(() => service.PrepareAudioAsync([], CreateProfile()));
  }

  public void Dispose()
  {
    if (Directory.Exists(audioDirectory))
    {
      Directory.Delete(audioDirectory, recursive: true);
    }
  }

  private static void AssertUpdate(
    ReaderExportPreparationProgress update,
    int sectionIndex,
    int sectionCount,
    ReaderExportPreparationPhase phase,
    bool isCached)
  {
    Xunit.Assert.Equal(sectionIndex, update.SectionIndex);
    Xunit.Assert.Equal(sectionIndex + 1, update.SectionNumber);
    Xunit.Assert.Equal(sectionCount, update.SectionCount);
    Xunit.Assert.Equal(phase, update.Phase);
    Xunit.Assert.Equal(isCached, update.IsCached);
  }

  private static IReadOnlyList<ReadingSection> CreateSections(params string[] texts) => texts
    .Select((text, index) => new ReadingSection(
      index,
      UnicodeReadingTextSegmenter.Segment(text)))
    .ToArray();

  private static ReaderNarrationProfile CreateProfile(
    ReaderWordTimingStrategy strategy = ReaderWordTimingStrategy.NativeWithDeterministicFallback) => new(
      "en-us",
      "kokoro-local",
      "af_bella",
      null,
      strategy);

  private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
  {
    public void Report(T value) => report(value);
  }

  private class RecordingSpeechService(string audioDirectory) : ITextToSpeechService
  {
    public int CallCount { get; protected set; }

    public virtual Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      CallCount++;
      return Task.FromResult(CreateResult(request));
    }

    protected TextToSpeechResult CreateResult(TextToSpeechRequest request)
    {
      string stem = string.Join(
        "-",
        request.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .ToLowerInvariant()
        .TrimEnd('.');
      string path = Path.Combine(audioDirectory, $"{stem}.wav");
      File.WriteAllBytes(path, new byte[64]);
      return new TextToSpeechResult(path, TimeSpan.FromSeconds(2), 1, "test-provider", "test-model");
    }
  }

  private sealed class CancelOnSecondSpeechService(string audioDirectory) : RecordingSpeechService(audioDirectory)
  {
    public override Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      return CallCount == 2
        ? Task.FromCanceled<TextToSpeechResult>(new CancellationToken(canceled: true))
        : Task.FromResult(CreateResult(request));
    }
  }

  private sealed class RecordingAlignmentService(IReadOnlyList<SpeechWordTiming> words) : ISpeechAlignmentService
  {
    public int CallCount { get; private set; }

    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      CallCount++;
      return Task.FromResult(new SpeechAlignmentResult(words, "test-aligner", TimeSpan.Zero));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
