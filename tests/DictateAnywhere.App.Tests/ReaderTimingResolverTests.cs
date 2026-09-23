using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Each ReaderTimingResolver under test takes ownership of its fake alignment service.")]
public sealed class ReaderTimingResolverTests
{
  [Fact]
  public async Task ResolveAsync_UsesCompleteNativeTimingsWithoutCallingTheAligner()
  {
    ReadingSection section = CreateSection("Hello world.");
    FakeSpeechAlignmentService aligner = new([]);
    await using ReaderTimingResolver resolver = new(aligner);
    TextToSpeechResult speech = CreateSpeech(
      TimeSpan.FromSeconds(2),
      [
        new SpeechWordTiming("Hello", TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.8)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.8)),
      ]);

    ReaderWordTimingMap map = await resolver.ResolveAsync(
      section,
      section.Text,
      speech,
      "en-us",
      ReaderWordTimingStrategy.NativeWithDeterministicFallback);

    Assert.Equal(ReaderWordTimingSource.Native, map.Source);
    Assert.True(map.IsAudioGrounded);
    Assert.Equal(0, aligner.CallCount);
  }

  [Fact]
  public async Task ResolveAsync_UsesDeterministicEstimateWhenNativeTimingsDoNotCoverTheText()
  {
    ReadingSection section = CreateSection("Hello world.");
    FakeSpeechAlignmentService aligner = new([]);
    await using ReaderTimingResolver resolver = new(aligner);
    TextToSpeechResult speech = CreateSpeech(
      TimeSpan.FromSeconds(2),
      [new SpeechWordTiming("different", TimeSpan.Zero, TimeSpan.FromSeconds(1))]);

    ReaderWordTimingMap map = await resolver.ResolveAsync(
      section,
      section.Text,
      speech,
      "en-us",
      ReaderWordTimingStrategy.NativeWithDeterministicFallback);

    Assert.Equal(ReaderWordTimingSource.DeterministicEstimate, map.Source);
    Assert.False(map.IsAudioGrounded);
    Assert.Equal(section.Words.Count, map.Words.Count);
    Assert.Equal(speech.Duration, map.Words[^1].End);
    Assert.Equal(0, aligner.CallCount);
  }

  [Fact]
  public async Task ResolveAsync_UsesExactNarrationTextAndIgnoresProviderEstimatesForLocalForcedAlignment()
  {
    ReadingDocument document = ReadingTextLayout.Create(
      "Stories",
      [new ReadableDocumentSection("A Title", "Hello world.")]);
    ReadingSection section = document.Sections[0];
    string narrationText = "A Title.\nHello world.";
    FakeSpeechAlignmentService aligner = new(
      [
        new SpeechWordTiming("A", TimeSpan.Zero, TimeSpan.FromSeconds(0.3)),
        new SpeechWordTiming("Title", TimeSpan.FromSeconds(0.3), TimeSpan.FromSeconds(0.8)),
        new SpeechWordTiming("Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.6)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(2.4)),
      ]);
    await using ReaderTimingResolver resolver = new(aligner);

    ReaderWordTimingMap map = await resolver.ResolveAsync(
      section,
      narrationText,
      CreateSpeech(
        TimeSpan.FromSeconds(2.5),
        [new SpeechWordTiming("estimated provider output", TimeSpan.Zero, TimeSpan.FromSeconds(2.5))]),
      "hi",
      ReaderWordTimingStrategy.LocalForcedAlignment);

    Assert.Equal(ReaderWordTimingSource.ForcedAlignment, map.Source);
    Assert.True(map.IsAudioGrounded);
    Assert.Equal(1, aligner.CallCount);
    Assert.NotNull(aligner.LastRequest);
    Assert.Equal(narrationText, aligner.LastRequest.Transcript);
    Assert.Equal("hi", aligner.LastRequest.Language);
  }

  [Fact]
  public async Task ResolveAsync_RejectsForcedAlignmentThatDoesNotCoverEveryDisplayToken()
  {
    ReadingSection section = CreateSection("Hello world.");
    FakeSpeechAlignmentService aligner = new(
      [new SpeechWordTiming("unrelated", TimeSpan.Zero, TimeSpan.FromSeconds(1))]);
    await using ReaderTimingResolver resolver = new(aligner);

    InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
      section,
      section.Text,
      CreateSpeech(TimeSpan.FromSeconds(2)),
      "hi",
      ReaderWordTimingStrategy.LocalForcedAlignment));

    Assert.Contains("every displayed word", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void TimingMap_NormalizesSupplementaryPlaneLettersByRune()
  {
    bool created = ReaderWordTimingMap.TryCreate(
      ["𠀀", "界"],
      [new SpeechWordTiming("𠀀界", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(1.2))],
      TimeSpan.FromSeconds(1.5),
      ReaderWordTimingSource.ForcedAlignment,
      out ReaderWordTimingMap? map);

    Assert.True(created);
    Assert.NotNull(map);
    Assert.Equal(2, map.Words.Count);
    Assert.Equal(TimeSpan.FromSeconds(0.2), map.Words[0].Start);
    Assert.Equal(TimeSpan.FromSeconds(0.7), map.Words[1].Start);
    Assert.Equal(TimeSpan.FromSeconds(1.2), map.Words[1].End);
    Assert.All(map.Words, word =>
    {
      Assert.True(word.Start >= TimeSpan.Zero);
      Assert.True(word.End >= word.Start);
      Assert.True(word.End <= TimeSpan.FromSeconds(1.5));
    });
  }

  [Fact]
  public async Task DisposeAsync_DisposesTheOwnedAlignmentService()
  {
    FakeSpeechAlignmentService aligner = new([]);
    ReaderTimingResolver resolver = new(aligner);

    await resolver.DisposeAsync();

    Assert.Equal(1, aligner.DisposeCount);
  }

  private static ReadingSection CreateSection(string text) => ReadingTextLayout.Create("Test", text).Sections[0];

  private static TextToSpeechResult CreateSpeech(
    TimeSpan duration,
    IReadOnlyList<SpeechWordTiming>? timings = null) => new(
      "C:\\audio\\section.wav",
      duration,
      1,
      "test-provider",
      "test-model",
      timings);

  private sealed class FakeSpeechAlignmentService(IReadOnlyList<SpeechWordTiming> words) : ISpeechAlignmentService
  {
    public int CallCount { get; private set; }

    public int DisposeCount { get; private set; }

    public SpeechAlignmentRequest? LastRequest { get; private set; }

    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      CallCount++;
      LastRequest = request;
      return Task.FromResult(new SpeechAlignmentResult(words, "fake-aligner", TimeSpan.Zero));
    }

    public ValueTask DisposeAsync()
    {
      DisposeCount++;
      return ValueTask.CompletedTask;
    }
  }
}
